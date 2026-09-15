using System.Collections.Generic;
using Game.Demo;
using Game.Network;
using Godot;

namespace Game.Level;

public partial class LevelManager : Node3D
{
    [Export]
    private PackedScene _pawnScene = null!;

    [Export]
    private Node3D _spawnPointsContainer = null!;

    private NetworkManager _networkManager = null!;
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<Vector3> _spawnPoints = new();
    private readonly Dictionary<long, Node3D> _spawnedPawns = new();
    private readonly Dictionary<long, int> _pawnSpawnAssignments = new();
    private readonly PendingStateBuffer<long> _pendingPawnState = new();
    private bool _serverSpawnInitialized;

    public override void _Ready()
    {
        _rng.Randomize();
        _networkManager = GetNode<NetworkManager>("/root/NetworkManager");
        _networkManager.PlayerConnected += OnPlayerConnected;
        _networkManager.PlayerDisconnected += OnPlayerDisconnected;
        _networkManager.ServerStarted += OnServerStarted;

        if (NetworkManager.IsAuthoritativeServer(Multiplayer))
        {
            OnServerStarted();
        }
    }

    public override void _ExitTree()
    {
        _networkManager.PlayerConnected -= OnPlayerConnected;
        _networkManager.PlayerDisconnected -= OnPlayerDisconnected;
        _networkManager.ServerStarted -= OnServerStarted;
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered
    )]
    private void ReceivePawnState(long playerId, byte[] stateData)
    {
        if (Multiplayer.GetRemoteSenderId() != 1)
        {
            return;
        }

        DispatchPawnState(playerId, stateData);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void InstantiatePawn(long playerId, Vector3 spawnPosition)
    {
        if (_spawnedPawns.ContainsKey(playerId))
        {
            return;
        }

        var pawn = _pawnScene.Instantiate<PawnMotor>();
        pawn.Name = $"Pawn_{playerId}";
        pawn.SetMultiplayerAuthority((int)playerId);
        AddChild(pawn);
        pawn.GlobalPosition = spawnPosition;

        _spawnedPawns[playerId] = pawn;
        _pendingPawnState.Flush(
            playerId,
            payload => DispatchPawnState(playerId, payload)
        );

        if (Multiplayer.GetUniqueId() == playerId)
        {
            pawn.EnableLocalCamera();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    private void RemovePawn(long playerId)
    {
        _spawnedPawns.Remove(playerId);

        var pawnNode = GetNodeOrNull<Node3D>($"Pawn_{playerId}");
        if (pawnNode is null)
        {
            return;
        }

        pawnNode.QueueFree();
    }

    private void DispatchPawnState(long playerId, byte[] stateData)
    {
        if (!_spawnedPawns.TryGetValue(playerId, out var pawnNode))
        {
            _pendingPawnState.Enqueue(playerId, stateData);
            return;
        }

        if (pawnNode is PawnMotor motor)
        {
            motor.ApplyNetworkState(stateData);
        }
    }

    private void CollectSpawnPoints()
    {
        foreach (Node3D spawnPoint in _spawnPointsContainer.GetChildren())
        {
            _spawnPoints.Add(spawnPoint.GlobalPosition);
        }
    }

    private Vector3 GetSpawnPointForPlayer(long playerId)
    {
        if (_pawnSpawnAssignments.TryGetValue(playerId, out var assignedIndex))
        {
            return _spawnPoints[assignedIndex];
        }

        var availableIndices = new List<int>();
        for (var i = 0; i < _spawnPoints.Count; i++)
        {
            if (!_pawnSpawnAssignments.ContainsValue(i))
            {
                availableIndices.Add(i);
            }
        }

        if (availableIndices.Count == 0)
        {
            var randomIndex = _rng.RandiRange(0, _spawnPoints.Count - 1);
            _pawnSpawnAssignments[playerId] = randomIndex;
            return _spawnPoints[randomIndex];
        }

        var randomAvailableIndex = availableIndices[
            _rng.RandiRange(0, availableIndices.Count - 1)
        ];
        _pawnSpawnAssignments[playerId] = randomAvailableIndex;
        return _spawnPoints[randomAvailableIndex];
    }

    private void OnServerStarted()
    {
        if (_serverSpawnInitialized)
        {
            return;
        }

        _serverSpawnInitialized = true;
        CollectSpawnPoints();
        CallDeferred(nameof(SpawnExistingPlayers));
    }

    private void OnPlayerConnected(long playerId)
    {
        if (!NetworkManager.IsAuthoritativeServer(Multiplayer))
        {
            return;
        }

        SpawnPlayer(playerId);

        foreach (var (existingId, existingPawn) in _spawnedPawns)
        {
            if (existingId == playerId)
            {
                continue;
            }

            RpcId(
                playerId,
                nameof(InstantiatePawn),
                existingId,
                existingPawn.GlobalPosition
            );
        }
    }

    private void OnPlayerDisconnected(long playerId)
    {
        if (!NetworkManager.IsAuthoritativeServer(Multiplayer))
        {
            return;
        }

        _pawnSpawnAssignments.Remove(playerId);
        _pendingPawnState.Remove(playerId);
        Rpc(nameof(RemovePawn), playerId);
    }

    private void SpawnExistingPlayers()
    {
        foreach (var playerId in _networkManager.Players.Keys)
        {
            SpawnPlayer(playerId);
        }
    }

    private void SpawnPlayer(long playerId)
    {
        if (_spawnedPawns.ContainsKey(playerId))
        {
            return;
        }

        var spawnPosition = GetSpawnPointForPlayer(playerId);
        Rpc(nameof(InstantiatePawn), playerId, spawnPosition);
    }

    public void BroadcastPawnState(long playerId, byte[] stateData)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        Rpc(nameof(ReceivePawnState), playerId, stateData);
    }
}
