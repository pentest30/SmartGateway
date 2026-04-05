using System.Collections.Concurrent;

namespace SmartGateway.Host.LoadBalancing;

public class LatencyTracker
{
    private readonly int _bufferSize;
    private readonly ConcurrentDictionary<string, CircularBuffer> _buffers = new();

    public LatencyTracker(int bufferSize = 100)
    {
        _bufferSize = bufferSize;
    }

    public void Record(string clusterId, string destinationId, TimeSpan latency)
    {
        var key = $"{clusterId}:{destinationId}";
        var buffer = _buffers.GetOrAdd(key, _ => new CircularBuffer(_bufferSize));
        buffer.Add(latency);
    }

    public TimeSpan GetP95(string clusterId, string destinationId)
    {
        var key = $"{clusterId}:{destinationId}";
        if (!_buffers.TryGetValue(key, out var buffer))
            return TimeSpan.Zero;

        return buffer.GetPercentile(0.95);
    }
}

internal class CircularBuffer
{
    private readonly TimeSpan[] _data;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public CircularBuffer(int size)
    {
        _data = new TimeSpan[size];
    }

    public void Add(TimeSpan value)
    {
        lock (_lock)
        {
            _data[_head] = value;
            _head = (_head + 1) % _data.Length;
            if (_count < _data.Length)
                _count++;
        }
    }

    public TimeSpan GetPercentile(double percentile)
    {
        lock (_lock)
        {
            if (_count == 0)
                return TimeSpan.Zero;

            var snapshot = new TimeSpan[_count];
            Array.Copy(_data, snapshot, _count);
            Array.Sort(snapshot);

            var index = (int)Math.Ceiling(percentile * _count) - 1;
            index = Math.Max(0, Math.Min(index, _count - 1));
            return snapshot[index];
        }
    }
}
