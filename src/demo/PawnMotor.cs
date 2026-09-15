using System.Collections.Generic;
using Game.Level;
using Game.Network;
using Godot;

namespace Game.Demo;

public partial class PawnMotor : CharacterBody3D
{
    private NetworkManager _networkManager = null!;
    private LevelManager _levelManager = null!;
    private PawnInput _pawnInput = null!;
    private Camera3D _camera = null!;
    private readonly Queue<PawnTypes.PawnInputState> _pendingNetworkInputs =
        new();
    private NetworkInterpolator<PawnTypes.PawnTransformState> _networkInterpolator =
        null!;
    private PawnTypes.PawnTransformState? _latestServerState;
    private Vector2 _networkMovementInput;
    private long _lastProcessedInputTimestamp;
    private float _gravity;
    private bool _networkJumpRequested;

    private bool UseNetworkInput =>
        Multiplayer.IsServer() || !IsMultiplayerAuthority();

    public override void _Ready()
    {
        _networkManager = GetNode<NetworkManager>("/root/NetworkManager");
        _levelManager = GetNode<LevelManager>("/root/world");
        _pawnInput = GetNode<PawnInput>("PawnInput");
        _camera = GetNode<Camera3D>("Camera3D");
        _gravity = ProjectSettings
            .GetSetting("physics/3d/default_gravity")
            .AsSingle();
        _networkInterpolator =
            new NetworkInterpolator<PawnTypes.PawnTransformState>(
                _networkManager
            );
        _networkManager.NetworkTick += BroadcastNetworkState;
    }

    public override void _ExitTree()
    {
        _networkManager.NetworkTick -= BroadcastNetworkState;
    }

    public override void _Process(double delta)
    {
        if (UseNetworkInput)
        {
            return;
        }

        if (_pawnInput.LookDeltaX == 0f)
        {
            return;
        }

        // Mouse motion is already per-frame; do not scale by delta.
        RotateY(-_pawnInput.LookDeltaX * PawnTypes.LookSensitivity);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.IsServer())
        {
            ProcessPendingNetworkInputs();
        }

        if (Multiplayer.IsServer() || IsMultiplayerAuthority())
        {
            SimulateMovement(delta);
        }

        if (!Multiplayer.IsServer())
        {
            ProcessClientTransform();
        }
    }

    private void ProcessPendingNetworkInputs()
    {
        while (_pendingNetworkInputs.Count > 0)
        {
            var input = _pendingNetworkInputs.Dequeue();
            if (input.Timestamp <= _lastProcessedInputTimestamp)
            {
                continue;
            }

            _lastProcessedInputTimestamp = input.Timestamp;
            _networkMovementInput = input.MovementInput;
            Rotation = new Vector3(0f, input.Yaw, 0f);
            _networkJumpRequested = input.JumpRequested;
        }
    }

    private void SimulateMovement(double delta)
    {
        var movement = UseNetworkInput
            ? _networkMovementInput
            : _pawnInput.MovementInput;
        var jump = UseNetworkInput
            ? _networkJumpRequested
            : Input.IsActionJustPressed("jump");
        _networkJumpRequested = false;

        var direction = (
            Transform.Basis * new Vector3(movement.X, 0f, movement.Y)
        ).Normalized();
        var velocity = Velocity;
        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * PawnTypes.WalkSpeed;
            velocity.Z = direction.Z * PawnTypes.WalkSpeed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0f, PawnTypes.WalkSpeed);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0f, PawnTypes.WalkSpeed);
        }

        if (!IsOnFloor())
        {
            velocity.Y -= _gravity * (float)delta;
        }
        else if (jump)
        {
            velocity.Y = PawnTypes.JumpVelocity;
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void ProcessClientTransform()
    {
        if (IsMultiplayerAuthority() && _latestServerState is not null)
        {
            var serverState = _latestServerState.Value;
            var positionError = serverState.Position - GlobalPosition;
            if (
                positionError.Length()
                <= PawnTypes.ReconcilePositionSnapDistance
            )
            {
                return;
            }

            GlobalPosition = serverState.Position;
            Velocity = serverState.Velocity;
            return;
        }

        if (
            _networkInterpolator.TryGetNextState(
                out var nextState,
                LerpTransformState
            )
        )
        {
            GlobalPosition = nextState.Position;
            Velocity = nextState.Velocity;
            Rotation = new Vector3(0f, nextState.Yaw, 0f);
        }
    }

    private PawnTypes.PawnTransformState LerpTransformState(
        PawnTypes.PawnTransformState from,
        PawnTypes.PawnTransformState to,
        float factor,
        NetworkStateOperation operation
    )
    {
        if (operation == NetworkStateOperation.Interpolation)
        {
            return new PawnTypes.PawnTransformState(
                from.Position.Lerp(to.Position, factor),
                from.Velocity.Lerp(to.Velocity, factor),
                Mathf.LerpAngle(from.Yaw, to.Yaw, factor),
                from.MovementInput.Lerp(to.MovementInput, factor),
                to.Timestamp
            );
        }

        return new PawnTypes.PawnTransformState(
            _networkInterpolator.Extrapolate(from, to, s => s.Position, factor),
            _networkInterpolator.Extrapolate(from, to, s => s.Velocity, factor),
            _networkInterpolator.Extrapolate(from, to, s => s.Yaw, factor),
            _networkInterpolator.Extrapolate(
                from,
                to,
                s => s.MovementInput,
                factor
            ),
            to.Timestamp
        );
    }

    private void BroadcastNetworkState()
    {
        if (!Multiplayer.IsServer())
        {
            _networkManager.NetworkTick -= BroadcastNetworkState;
            return;
        }

        var state = new PawnTypes.PawnTransformState(
            GlobalPosition,
            Velocity,
            Rotation.Y,
            _networkMovementInput,
            (long)Time.GetTicksMsec()
        );
        _levelManager.BroadcastPawnState(
            GetMultiplayerAuthority(),
            state.ToBytes()
        );
    }

    public void ProcessNetworkInput(PawnTypes.PawnInputState inputState)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        _pendingNetworkInputs.Enqueue(inputState);
    }

    public void ApplyNetworkState(byte[] stateData)
    {
        var state = PawnTypes.PawnTransformState.FromBytes(stateData);
        if (IsMultiplayerAuthority())
        {
            _latestServerState = state;
            return;
        }

        _networkInterpolator.AddState(state);
    }

    public void EnableLocalCamera()
    {
        _camera.Current = true;
    }
}
