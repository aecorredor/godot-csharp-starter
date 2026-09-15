using Game.Network;
using Godot;

namespace Game.Demo;

public partial class PawnInput : Node
{
    private PawnMotor _pawn = null!;
    private NetworkManager _networkManager = null!;
    private bool _jumpRequestedThisFrame;

    public Vector2 MovementInput { get; private set; }
    public float LookDeltaX { get; private set; }

    public override void _Ready()
    {
        ProcessPriority = 100;
        _pawn = GetParent<PawnMotor>();
        _networkManager = GetNode<NetworkManager>("/root/NetworkManager");

        if (Multiplayer.IsServer() || !_pawn.IsMultiplayerAuthority())
        {
            SetProcess(false);
            SetProcessInput(false);
        }
    }

    public override void _Process(double delta)
    {
        if (!_pawn.IsMultiplayerAuthority() || Multiplayer.IsServer())
        {
            return;
        }

        MovementInput = Input.GetVector("left", "right", "forward", "backward");
        _jumpRequestedThisFrame =
            _jumpRequestedThisFrame || Input.IsActionJustPressed("jump");

        var input = new PawnTypes.PawnInputState(
            MovementInput,
            _pawn.Rotation.Y,
            _jumpRequestedThisFrame,
            (long)Time.GetTicksMsec()
        );
        RpcId(1, nameof(ServerReceiveInput), input.ToBytes());
        _jumpRequestedThisFrame = false;
        LookDeltaX = 0f;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            LookDeltaX += mouseMotion.Relative.X;
        }
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable
    )]
    private void ServerReceiveInput(byte[] inputData)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        var senderId = Multiplayer.GetRemoteSenderId();
        if (!_networkManager.IsAuthenticated(senderId))
        {
            return;
        }

        var motor = GetTree()
            .Root.GetNodeOrNull<PawnMotor>($"world/Pawn_{senderId}");
        if (motor is null)
        {
            return;
        }

        var input = PawnTypes.PawnInputState.FromBytes(inputData);
        if (!input.TryValidate(out var validated))
        {
            GD.PrintErr(
                $"[PawnInput] Rejected invalid input from peer {senderId}"
            );
            return;
        }

        motor.ProcessNetworkInput(validated);
    }
}
