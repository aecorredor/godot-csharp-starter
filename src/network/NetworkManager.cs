using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Game.Network;

public partial class NetworkManager : Node
{
    private const string MainScenePath = "res://scenes/main.tscn";
    private const int HandshakePeerIdTimeoutMs = 8000;

    [Signal]
    public delegate void PlayerConnectedEventHandler(long playerId);

    [Signal]
    public delegate void PlayerDisconnectedEventHandler(long playerId);

    [Signal]
    public delegate void ConnectionFailedEventHandler();

    [Signal]
    public delegate void ServerStartedEventHandler();

    [Signal]
    public delegate void NetworkTickEventHandler();

    private const int NetworkTickRate = 30;
    private readonly float _networkSyncInterval = 1f / NetworkTickRate;
    private float _networkSyncTimer;

    private int _port = 7000;
    private int _maxPlayers = 8;

    private long _latency;

    private double _deltaLatency;

    private double _decimalCollector;

    private readonly List<long> _latencyBuffer = new();

    private long _clientClock;

    private readonly HashSet<long> _authenticatedPeers = new();
    private readonly Dictionary<long, PlayerData> _players = new();

    private string _gameVersion = "0.0.1";
    private bool _devMode;
    private bool _sessionValidateDisabled;
    private bool _allowServerStart;
    private bool _shutdownRequested;
    private int _connectedPeerCount;
    private int _connectHandshakeGeneration;
    private Timer? _latencyTimer;

    public IReadOnlyDictionary<long, PlayerData> Players => _players;

    public long ClientClock
    {
        get
        {
            if (!IsTransportConnected(Multiplayer))
            {
                return _clientClock;
            }

            return Multiplayer.IsServer()
                ? (long)Time.GetTicksMsec()
                : _clientClock;
        }
        private set => _clientClock = value;
    }

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _ExitTree()
    {
        CloseMultiplayerSession();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            CloseMultiplayerSession();
        }
    }

    /// <summary>
    /// Closes the transport so the remote host receives PeerDisconnected (editor stop, scene exit).
    /// </summary>
    private void CloseMultiplayerSession()
    {
        CancelPendingClientHandshake();
        StopLatencyTimer();

        if (Multiplayer.MultiplayerPeer is null)
        {
            return;
        }

        var peer = Multiplayer.MultiplayerPeer;
        Multiplayer.MultiplayerPeer = null;
        peer.Close();
    }

    public override void _PhysicsProcess(double delta)
    {
        TickClientClock(delta);
    }

    public override void _Process(double delta)
    {
        if (
            Multiplayer.MultiplayerPeer is null
            || Multiplayer.MultiplayerPeer.GetConnectionStatus()
                != MultiplayerPeer.ConnectionStatus.Connected
            || !Multiplayer.IsServer()
        )
        {
            return;
        }

        _networkSyncTimer += (float)delta;
        if (_networkSyncTimer >= _networkSyncInterval)
        {
            EmitSignal(SignalName.NetworkTick);
            _networkSyncTimer = 0f;
        }
    }

    public void ApplyBootConfig(BootConfig config)
    {
        _port = config.Port;
        _maxPlayers = config.MaxPlayers;
        _gameVersion = config.GameVersion;
        _devMode = config.DevMode || OS.HasFeature("editor");
        _sessionValidateDisabled = config.SessionValidateDisabled;
        _allowServerStart = config.IsServer;
    }

    public bool IsAuthenticated(long peerId) =>
        _authenticatedPeers.Contains(peerId);

    public Task PrepareClientMainSceneAsync() => EnsureMainSceneLoadedAsync();

    /// <summary>
    /// Godot <c>ENetMultiplayerPeer.create_client</c> uses <c>generate_unique_id()</c>
    /// (values in [2, 0x7FFFFFFF]); only 0 and 1 are reserved. Not limited to max_clients.
    /// </summary>
    private static bool IsValidPeerId(long peerId) =>
        peerId is >= 2 and <= 0x7FFFFFFF;

    public void RequestShutdown()
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        _shutdownRequested = true;
        Rpc(nameof(ClientReceiveServerShutdown));

        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
        {
            foreach (var peerId in new List<long>(_players.Keys))
            {
                enet.DisconnectPeer((int)peerId);
            }
        }
    }

    public bool IsShutdownDrainComplete()
    {
        if (!_shutdownRequested)
        {
            return false;
        }

        return _connectedPeerCount <= 0 || Multiplayer.MultiplayerPeer is null;
    }

    public void StartServer()
    {
        if (!_allowServerStart && !OS.HasFeature("dedicated_server"))
        {
            GD.PushError(
                "[NetworkManager] StartServer is dedicated-server only; use `docker compose up` (see README) or --server"
            );
            return;
        }

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(_port, _maxPlayers);

        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to start server: {error}");
            return;
        }

        var tls = CreateServerTlsOptions();
        if (peer.Host is null)
        {
            GD.PushError(
                "[NetworkManager] ENet host missing after CreateServer; cannot enable DTLS"
            );
            peer.Close();
            return;
        }

        error = peer.Host.DtlsServerSetup(tls);
        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to configure DTLS server: {error}");
            peer.Close();
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        _connectedPeerCount = 0;
        GD.Print($"Server started on port {_port} (DTLS enabled)");
        EmitSignal(SignalName.ServerStarted);
    }

    public void JoinServer(string host, int port)
    {
        // DTLS is configured on peer.Host after CreateClient (same pattern as DtlsServerSetup on server).
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(host, port);
        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to create ENet client: {error}");
            EmitSignal(SignalName.ConnectionFailed);
            return;
        }

        if (peer.Host is null)
        {
            GD.PushError(
                "[NetworkManager] ENet host missing after CreateClient; cannot enable DTLS"
            );
            peer.Close();
            EmitSignal(SignalName.ConnectionFailed);
            return;
        }

        error = peer.Host.DtlsClientSetup(host, TlsOptions.ClientUnsafe());
        if (error != Error.Ok)
        {
            GD.PrintErr($"Failed to configure DTLS client: {error}");
            peer.Close();
            EmitSignal(SignalName.ConnectionFailed);
            return;
        }

        _port = port;
        Multiplayer.MultiplayerPeer = peer;
        GD.Print(
            $"Connecting to {host}:{port} (DTLS); handshake on ConnectedToServer"
        );
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    private void ServerReceiveHandshake(
        string sessionToken,
        string gameVersion,
        string displayName
    )
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        var peerId = Multiplayer.GetRemoteSenderId();

        if (_shutdownRequested)
        {
            RejectHandshake(peerId, "Server is shutting down");
            return;
        }

        if (
            !string.Equals(
                gameVersion,
                _gameVersion,
                System.StringComparison.Ordinal
            )
        )
        {
            RejectHandshake(
                peerId,
                $"Game version mismatch (expected {_gameVersion})"
            );
            return;
        }

        if (
            !StubSessionValidator.TryValidate(
                sessionToken,
                _devMode,
                _sessionValidateDisabled,
                out var accountId,
                out var validatedDisplayName
            )
        )
        {
            RejectHandshake(peerId, "Invalid session token");
            return;
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? validatedDisplayName
            : displayName.Trim();

        _authenticatedPeers.Add(peerId);
        _players[peerId] = new PlayerData
        {
            Id = peerId,
            AccountId = accountId,
            DisplayName = resolvedDisplayName,
        };

        RpcId(peerId, nameof(ClientReceiveHandshakeResult), true, string.Empty);
        EmitSignal(SignalName.PlayerConnected, peerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void ClientReceiveHandshakeResult(bool accepted, string reason)
    {
        if (Multiplayer.IsServer())
        {
            return;
        }

        if (!accepted)
        {
            GD.PrintErr($"Handshake rejected: {reason}");
            EmitSignal(SignalName.ConnectionFailed);
            Multiplayer.MultiplayerPeer?.Close();
            return;
        }

        var localId = Multiplayer.GetUniqueId();
        _authenticatedPeers.Add(localId);
        BeginClientSession(localId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void ClientReceiveServerShutdown()
    {
        GD.Print("Server requested shutdown");
        Multiplayer.MultiplayerPeer?.Close();
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable
    )]
    private void RequestServerTime(long clientTimestamp)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        var senderId = Multiplayer.GetRemoteSenderId();
        if (!IsAuthenticated(senderId))
        {
            return;
        }

        RpcId(
            senderId,
            nameof(ReceiveServerTime),
            (long)Time.GetTicksMsec(),
            clientTimestamp
        );
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    private void ReceiveServerTime(long serverTimestamp, long clientTimestamp)
    {
        if (Multiplayer.IsServer())
        {
            return;
        }

        ClientClock = serverTimestamp + GetLatency(clientTimestamp);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable
    )]
    private void RequestLatency(long clientTimestamp)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        var senderId = Multiplayer.GetRemoteSenderId();
        if (!IsAuthenticated(senderId))
        {
            return;
        }

        RpcId(senderId, nameof(ReceiveLatency), clientTimestamp);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable
    )]
    private void ReceiveLatency(long clientTimestamp)
    {
        if (Multiplayer.IsServer())
        {
            return;
        }

        _latencyBuffer.Add(GetLatency(clientTimestamp));

        if (_latencyBuffer.Count < 9)
        {
            return;
        }

        var totalLatency = 0L;
        _latencyBuffer.Sort();
        var midPoint = _latencyBuffer[4];

        for (int i = _latencyBuffer.Count - 1; i >= 0; i--)
        {
            if (_latencyBuffer[i] > midPoint * 2 && _latencyBuffer[i] > 20)
            {
                _latencyBuffer.RemoveAt(i);
            }
            else
            {
                totalLatency += _latencyBuffer[i];
            }
        }

        var newLatency = totalLatency / _latencyBuffer.Count;
        _deltaLatency = newLatency - _latency;
        _latency = newLatency;
        _latencyBuffer.Clear();
    }

    private void TickClientClock(double delta)
    {
        if (!IsTransportConnected(Multiplayer) || Multiplayer.IsServer())
        {
            return;
        }

        var deltaMs = delta * 1000;
        ClientClock += (long)(deltaMs + _deltaLatency);
        _deltaLatency = 0;
        _decimalCollector += deltaMs - (long)deltaMs;

        if (_decimalCollector >= 1.0f)
        {
            ClientClock += 1;
            _decimalCollector -= 1.0f;
        }
    }

    private void OnPeerConnected(long peerId)
    {
        _connectedPeerCount++;
    }

    private void OnPeerDisconnected(long peerId)
    {
        _connectedPeerCount = System.Math.Max(0, _connectedPeerCount - 1);

        GD.Print($"Player {peerId} disconnected");
        _authenticatedPeers.Remove(peerId);
        _players.Remove(peerId);

        if (IsAuthoritativeServer(Multiplayer))
        {
            EmitSignal(SignalName.PlayerDisconnected, peerId);
        }
    }

    private void OnConnectedToServer()
    {
        var generation = ++_connectHandshakeGeneration;
        _ = WaitAndBeginHandshakeAsync(generation);
    }

    private async Task WaitAndBeginHandshakeAsync(int generation)
    {
        var deadlineMs = (long)Time.GetTicksMsec() + HandshakePeerIdTimeoutMs;
        var tree = GetTree();

        while ((long)Time.GetTicksMsec() < deadlineMs)
        {
            if (generation != _connectHandshakeGeneration)
            {
                return;
            }

            if (!IsTransportConnected(Multiplayer))
            {
                return;
            }

            var uniqueId = Multiplayer.GetUniqueId();
            if (IsValidPeerId(uniqueId))
            {
                GD.Print(
                    $"Peer {uniqueId} transport connected; starting handshake"
                );
                BeginClientHandshake();
                return;
            }

            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        if (generation != _connectHandshakeGeneration)
        {
            return;
        }

        GD.PushError(
            "[NetworkManager] Timed out waiting for valid peer id after connect"
        );
        Multiplayer.MultiplayerPeer?.Close();
        EmitSignal(SignalName.ConnectionFailed);
    }

    private void BeginClientHandshake()
    {
        var token =
            _devMode || _sessionValidateDisabled ? "dev-local" : string.Empty;
        RpcId(
            1,
            nameof(ServerReceiveHandshake),
            token,
            _gameVersion,
            $"Player {Multiplayer.GetUniqueId()}"
        );
    }

    private void BeginClientSession(long localId)
    {
        GD.Print($"Player {localId} authenticated");
        Input.MouseMode = Input.MouseModeEnum.Captured;

        RpcId(1, nameof(RequestServerTime), (long)Time.GetTicksMsec());

        _latencyTimer = new Timer { WaitTime = 0.5, OneShot = false };
        _latencyTimer.Timeout += () =>
            RpcId(1, nameof(RequestLatency), (long)Time.GetTicksMsec());
        AddChild(_latencyTimer);
        _latencyTimer.Start();
    }

    private void StopLatencyTimer()
    {
        if (_latencyTimer is null)
        {
            return;
        }

        _latencyTimer.Stop();
        _latencyTimer.QueueFree();
        _latencyTimer = null;
    }

    private async Task EnsureMainSceneLoadedAsync()
    {
        var tree = GetTree();
        if (tree.CurrentScene?.SceneFilePath == MainScenePath)
        {
            return;
        }

        tree.CallDeferred(
            SceneTree.MethodName.ChangeSceneToFile,
            MainScenePath
        );
        while (
            tree.CurrentScene is null
            || tree.CurrentScene.SceneFilePath != MainScenePath
        )
        {
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private void CancelPendingClientHandshake()
    {
        _connectHandshakeGeneration++;
    }

    private void OnConnectionFailed()
    {
        CancelPendingClientHandshake();
        StopLatencyTimer();
    }

    private void OnServerDisconnected()
    {
        CancelPendingClientHandshake();
        StopLatencyTimer();
        GD.Print("Server disconnected");
    }

    private void RejectHandshake(long peerId, string reason)
    {
        RpcId(peerId, nameof(ClientReceiveHandshakeResult), false, reason);

        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet)
        {
            enet.DisconnectPeer((int)peerId);
        }
    }

    private static long GetLatency(long startTime) =>
        ((long)Time.GetTicksMsec() - startTime) / 2;

    private static TlsOptions CreateServerTlsOptions()
    {
        var crypto = new Crypto();
        var key = crypto.GenerateRsa(2048);
        var cert = crypto.GenerateSelfSignedCertificate(
            key,
            "CN=starter-template"
        );
        return TlsOptions.Server(key, cert);
    }

    /// <summary>
    /// Godot reports <see cref="MultiplayerApi.IsServer"/> as true when no peer
    /// is assigned. Gate server-only replication on an active, connected
    /// transport.
    /// </summary>
    public static bool IsTransportConnected(MultiplayerApi multiplayer) =>
        multiplayer.MultiplayerPeer?.GetConnectionStatus()
        == MultiplayerPeer.ConnectionStatus.Connected;

    public static bool IsAuthoritativeServer(MultiplayerApi multiplayer) =>
        multiplayer.MultiplayerPeer is ENetMultiplayerPeer
        && IsTransportConnected(multiplayer)
        && multiplayer.IsServer();
}

public class PlayerData
{
    public long Id { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
