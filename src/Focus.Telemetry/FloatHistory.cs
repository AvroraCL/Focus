using Focus.Core.Metrics;

namespace Focus.Telemetry;

/// <summary>Fixed-size ring buffer of float samples used for HUD graphs.</summary>
public sealed class FloatHistory
{
    private readonly float[] _data;
    private readonly object _gate = new();
    private int _start;
    private int _count;

    public FloatHistory(int capacity)
    {
        _data = new float[Math.Max(8, capacity)];
    }

    public void Add(float value)
    {
        lock (_gate)
        {
            int index = (_start + _count) % _data.Length;
            if (_count == _data.Length)
            {
                _data[_start] = value;
                _start = (_start + 1) % _data.Length;
            }
            else
            {
                _data[index] = value;
                _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _start = 0;
            _count = 0;
        }
    }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    /// <summary>Returns the newest <paramref name="maxSamples"/> samples, oldest first.</summary>
    public float[] Snapshot(int maxSamples = int.MaxValue)
    {
        lock (_gate)
        {
            int take = Math.Min(_count, Math.Max(1, maxSamples));
            var result = new float[take];
            int offset = _count - take;
            for (int i = 0; i < take; i++) result[i] = _data[(_start + offset + i) % _data.Length];
            return result;
        }
    }

    public float LastOr(float fallback)
    {
        lock (_gate)
        {
            return _count == 0 ? fallback : _data[(_start + _count - 1) % _data.Length];
        }
    }

    public bool TryLast(out float value)
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                value = 0;
                return false;
            }
            value = _data[(_start + _count - 1) % _data.Length];
            return true;
        }
    }
}

public static class MetricHistory
{
    public static bool IsFrameMetric(MetricId id) => id is MetricId.Fps or MetricId.FrameTime;
}
