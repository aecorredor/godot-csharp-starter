using Game.Network;
using Godot;

namespace Game.Demo;

public static class PawnTypes
{
    public const float WalkSpeed = 4f;
    public const float JumpVelocity = 4.5f;
    public const float LookSensitivity = 0.01f;
    public const float ReconcilePositionSnapDistance = 0.75f;

    public readonly struct PawnInputState : INetworkState, INetworkSerializable
    {
        public readonly Vector2 MovementInput;
        public readonly float Yaw;
        public readonly bool JumpRequested;
        public long Timestamp { get; }

        public static int ApproximateSize => 21;

        public PawnInputState(
            Vector2 movementInput,
            float yaw,
            bool jumpRequested,
            long timestamp
        )
        {
            MovementInput = movementInput;
            Yaw = yaw;
            JumpRequested = jumpRequested;
            Timestamp = timestamp;
        }

        public byte[] ToBytes()
        {
            var writer = new NetworkSerializer.Writer(ApproximateSize);
            writer.Write(MovementInput);
            writer.Write(Yaw);
            writer.Write(JumpRequested);
            writer.Write(Timestamp);

            var bytes = writer.ToArray();
            NetworkSerializer.DebugLog(this, bytes);
            writer.Dispose();
            return bytes;
        }

        public static PawnInputState FromBytes(byte[] data)
        {
            var reader = new NetworkSerializer.Reader(data);
            var state = new PawnInputState(
                movementInput: reader.ReadVector2(),
                yaw: reader.ReadFloat(),
                jumpRequested: reader.ReadBool(),
                timestamp: reader.ReadLong()
            );

            NetworkSerializer.DebugLogRead(state, data.Length);
            reader.Dispose();
            return state;
        }

        public bool TryValidate(out PawnInputState validated)
        {
            validated = default;

            if (
                float.IsNaN(MovementInput.X)
                || float.IsNaN(MovementInput.Y)
                || float.IsInfinity(MovementInput.X)
                || float.IsInfinity(MovementInput.Y)
                || MovementInput.X is < -1f or > 1f
                || MovementInput.Y is < -1f or > 1f
                || float.IsNaN(Yaw)
                || float.IsInfinity(Yaw)
            )
            {
                return false;
            }

            validated = this;
            return true;
        }
    }

    public readonly struct PawnTransformState
        : INetworkState,
            INetworkSerializable
    {
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly float Yaw;
        public readonly Vector2 MovementInput;
        public long Timestamp { get; }

        public static int ApproximateSize => 44;

        public PawnTransformState(
            Vector3 position,
            Vector3 velocity,
            float yaw,
            Vector2 movementInput,
            long timestamp
        )
        {
            Position = position;
            Velocity = velocity;
            Yaw = yaw;
            MovementInput = movementInput;
            Timestamp = timestamp;
        }

        public byte[] ToBytes()
        {
            var writer = new NetworkSerializer.Writer(ApproximateSize);
            writer.Write(Position);
            writer.Write(Velocity);
            writer.Write(Yaw);
            writer.Write(MovementInput);
            writer.Write(Timestamp);

            var bytes = writer.ToArray();
            NetworkSerializer.DebugLog(this, bytes);
            writer.Dispose();
            return bytes;
        }

        public static PawnTransformState FromBytes(byte[] data)
        {
            var reader = new NetworkSerializer.Reader(data);
            var state = new PawnTransformState(
                position: reader.ReadVector3(),
                velocity: reader.ReadVector3(),
                yaw: reader.ReadFloat(),
                movementInput: reader.ReadVector2(),
                timestamp: reader.ReadLong()
            );

            NetworkSerializer.DebugLogRead(state, data.Length);
            reader.Dispose();
            return state;
        }
    }
}
