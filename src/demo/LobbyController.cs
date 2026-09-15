using Game.Network;
using Godot;
using Utils;

namespace Game.UI;

public partial class LobbyController : Control
{
    private NetworkManager _networkManager = null!;
    private Button _joinServerButton = null!;
    private LineEdit _addressInput = null!;
    private Label _statusLabel = null!;
    private VBoxContainer _playersContainer = null!;

    public override void _Ready()
    {
        _joinServerButton = GetNode<Button>(
            "VBoxContainer/ClientSection/JoinServerButton"
        );
        _addressInput = GetNode<LineEdit>(
            "VBoxContainer/ClientSection/HBoxContainer/AddressInput"
        );
        _statusLabel = GetNode<Label>("VBoxContainer/StatusLabel");
        _playersContainer = GetNode<VBoxContainer>(
            "VBoxContainer/PlayersList/PlayersContainer"
        );

        _joinServerButton.Pressed += OnJoinServerPressed;

        _networkManager = GetNode<NetworkManager>("/root/NetworkManager");
        _networkManager.PlayerConnected += OnPlayerConnected;
        _networkManager.PlayerDisconnected += OnPlayerDisconnected;
        _networkManager.ConnectionFailed += OnConnectionFailed;
    }

    public override void _ExitTree()
    {
        _networkManager.PlayerConnected -= OnPlayerConnected;
        _networkManager.PlayerDisconnected -= OnPlayerDisconnected;
        _networkManager.ConnectionFailed -= OnConnectionFailed;
        _joinServerButton.Pressed -= OnJoinServerPressed;
    }

    private async void OnJoinServerPressed()
    {
        var raw = _addressInput.Text.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            _statusLabel.Text =
                "Status: Please enter server address (host:port)";
            return;
        }

        if (!EndpointParser.TryParse(raw, out var endpoint))
        {
            _statusLabel.Text =
                "Status: Invalid address — use host:port (e.g. 127.0.0.1:7000)";
            return;
        }

        _joinServerButton.Disabled = true;
        _statusLabel.Text = "Status: Loading level...";

        await _networkManager.PrepareClientMainSceneAsync();

        _networkManager.ApplyBootConfig(BootConfig.Parse());
        _networkManager.JoinServer(endpoint.Host, endpoint.Port);
    }

    private bool IsLobbyUiAlive() =>
        IsInsideTree() && IsInstanceValid(_statusLabel);

    private void OnPlayerConnected(long playerId)
    {
        GD.Print(
            $"Lobby: player {playerId} connected. Local: {Multiplayer.GetUniqueId()}. Total players: {_networkManager.Players.Count}."
        );
        if (!IsLobbyUiAlive())
        {
            return;
        }

        _statusLabel.Text = "Status: Connected";
        _joinServerButton.Disabled = true;
        UpdatePlayersList();
    }

    private void OnPlayerDisconnected(long playerId)
    {
        GD.Print($"Player {playerId} disconnected");
        if (!IsLobbyUiAlive())
        {
            return;
        }

        UpdatePlayersList();
    }

    private void OnConnectionFailed()
    {
        if (!IsLobbyUiAlive())
        {
            return;
        }

        _statusLabel.Text = "Status: Connection failed";
        _joinServerButton.Disabled = false;
    }

    private void UpdatePlayersList()
    {
        if (!IsLobbyUiAlive())
        {
            return;
        }

        foreach (Node child in _playersContainer.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var player in _networkManager.Players.Values)
        {
            var label = new Label
            {
                Text = $"• {player.DisplayName} (ID: {player.Id})",
            };
            _playersContainer.AddChild(label);
        }
    }
}
