using System;
using System.Collections.Generic;
using Godot;

namespace Game.Network;

public sealed class PendingStateBuffer<TId>
    where TId : notnull
{
    private readonly int _maxPerEntity;
    private readonly long _ttlMs;
    private readonly Dictionary<TId, Queue<(byte[] Payload, long At)>> _buffer =
        new();

    public PendingStateBuffer(int maxPerEntity = 4, long ttlMs = 2000)
    {
        _maxPerEntity = maxPerEntity;
        _ttlMs = ttlMs;
    }

    public void Enqueue(TId id, byte[] payload)
    {
        if (!_buffer.TryGetValue(id, out var queue))
        {
            _buffer[id] = queue = new Queue<(byte[], long)>();
        }

        queue.Enqueue((payload, (long)Time.GetTicksMsec()));
        while (queue.Count > _maxPerEntity)
        {
            queue.Dequeue();
        }
    }

    public void Flush(TId id, Action<byte[]> apply)
    {
        if (!_buffer.Remove(id, out var queue))
        {
            return;
        }

        var now = (long)Time.GetTicksMsec();
        while (queue.Count > 0)
        {
            var (payload, at) = queue.Dequeue();
            if (now - at > _ttlMs)
            {
                continue;
            }

            apply(payload);
        }
    }

    public void Remove(TId id) => _buffer.Remove(id);

    public void Clear() => _buffer.Clear();
}
