using System;
using System.IO;
using System.Runtime.CompilerServices;
using Godot;

namespace Game.Network;

/// <summary>
/// Provides efficient binary serialization for network state types.
/// Supports common Godot types and provides debug output capabilities.
/// </summary>
public static class NetworkSerializer
{
    /// <summary>
    /// Enable to log serialized data for debugging purposes.
    /// </summary>
    public static bool DebugMode { get; set; } = false;

    /// <summary>
    /// A ref struct writer that efficiently serializes data to a byte array.
    /// Uses stack-allocated buffers for small payloads.
    /// </summary>
    public ref struct Writer
    {
        private readonly MemoryStream _stream;
        private readonly BinaryWriter _writer;

        public Writer(int initialCapacity = 128)
        {
            _stream = new MemoryStream(initialCapacity);
            _writer = new BinaryWriter(_stream);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(bool value) => _writer.Write(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte value) => _writer.Write(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(int value) => _writer.Write(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(long value) => _writer.Write(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(float value) => _writer.Write(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(double value) => _writer.Write(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(string value) => _writer.Write(value ?? string.Empty);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Vector2 value)
        {
            _writer.Write(value.X);
            _writer.Write(value.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Vector3 value)
        {
            _writer.Write(value.X);
            _writer.Write(value.Y);
            _writer.Write(value.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(Quaternion value)
        {
            _writer.Write(value.X);
            _writer.Write(value.Y);
            _writer.Write(value.Z);
            _writer.Write(value.W);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write<TEnum>(TEnum value)
            where TEnum : struct, Enum
        {
            _writer.Write(Unsafe.As<TEnum, int>(ref value));
        }

        public byte[] ToArray()
        {
            _writer.Flush();
            return _stream.ToArray();
        }

        public void Dispose()
        {
            _writer.Dispose();
            _stream.Dispose();
        }
    }

    /// <summary>
    /// A ref struct reader that efficiently deserializes data from a byte array.
    /// </summary>
    public ref struct Reader
    {
        private readonly BinaryReader _reader;
        private readonly MemoryStream _stream;

        public Reader(byte[] data)
        {
            _stream = new MemoryStream(data);
            _reader = new BinaryReader(_stream);
        }

        public Reader(ReadOnlySpan<byte> data)
        {
            _stream = new MemoryStream(data.ToArray());
            _reader = new BinaryReader(_stream);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool() => _reader.ReadBoolean();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte() => _reader.ReadByte();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt() => _reader.ReadInt32();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadLong() => _reader.ReadInt64();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat() => _reader.ReadSingle();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble() => _reader.ReadDouble();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString() => _reader.ReadString();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2 ReadVector2() =>
            new(_reader.ReadSingle(), _reader.ReadSingle());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ReadVector3() =>
            new(
                _reader.ReadSingle(),
                _reader.ReadSingle(),
                _reader.ReadSingle()
            );

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion ReadQuaternion() =>
            new(
                _reader.ReadSingle(),
                _reader.ReadSingle(),
                _reader.ReadSingle(),
                _reader.ReadSingle()
            );

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TEnum ReadEnum<TEnum>()
            where TEnum : struct, Enum
        {
            var value = _reader.ReadInt32();
            return Unsafe.As<int, TEnum>(ref value);
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }

    /// <summary>
    /// Logs serialized state data if debug mode is enabled.
    /// </summary>
    /// <typeparam name="T">The state type.</typeparam>
    /// <param name="state">The state to log.</param>
    /// <param name="bytes">The serialized bytes.</param>
    /// <param name="caller">The calling method name.</param>
    public static void DebugLog<T>(
        T state,
        byte[] bytes,
        [CallerMemberName] string? caller = null
    )
    {
        if (!DebugMode)
            return;

        GD.Print($"[NetworkSerializer] {caller}: {typeof(T).Name}");
        GD.Print($"  Size: {bytes.Length} bytes");
        GD.Print($"  Data: {state}");
    }

    /// <summary>
    /// Logs deserialized state data if debug mode is enabled.
    /// </summary>
    /// <typeparam name="T">The state type.</typeparam>
    /// <param name="state">The deserialized state.</param>
    /// <param name="bytesRead">Number of bytes read.</param>
    /// <param name="caller">The calling method name.</param>
    public static void DebugLogRead<T>(
        T state,
        int bytesRead,
        [CallerMemberName] string? caller = null
    )
    {
        if (!DebugMode)
            return;

        GD.Print(
            $"[NetworkSerializer] {caller}: Deserialized {typeof(T).Name}"
        );
        GD.Print($"  Size: {bytesRead} bytes");
        GD.Print($"  Data: {state}");
    }
}

/// <summary>
/// Interface for types that can be serialized over the network.
/// </summary>
public interface INetworkSerializable
{
    /// <summary>
    /// Serializes this instance to a byte array.
    /// </summary>
    byte[] ToBytes();

    /// <summary>
    /// Gets the approximate serialized size in bytes for buffer allocation hints.
    /// </summary>
    static virtual int ApproximateSize => 64;
}
