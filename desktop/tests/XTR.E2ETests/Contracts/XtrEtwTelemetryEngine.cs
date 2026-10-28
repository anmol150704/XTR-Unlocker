using System;
using System.Collections.Generic;
using System.Linq;

namespace XTR.E2ETests.Contracts;

public sealed class XtrEtwTelemetryEngine
{
    public static readonly Guid DxgiProviderGuid = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    public static readonly Guid D3D12ProviderGuid = new("5D8087DD-3A9B-4F94-AA1E-164706CA557F");
    public static readonly Guid DwmCoreProviderGuid = new("9E9B37A2-111E-47C0-B776-E217E623B640");

    private readonly double[] _ringBuffer;
    private int _head;
    private int _count;
    private readonly int _capacity;
    private readonly object _lock = new();

    public XtrEtwTelemetryEngine(int capacity = 1000)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _ringBuffer = new double[capacity];
    }

    public int Capacity => _capacity;
    public int SampleCount => _count;

    public void RecordFrameDeltaMs(double deltaMs)
    {
        lock (_lock)
        {
            if (deltaMs <= 0.0) deltaMs = 0.001; // Clamp zero/negative to prevent NaN
            _ringBuffer[_head] = deltaMs;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity)
            {
                _count++;
            }
        }
    }

    public static double CalculateInstantFps(double deltaMs)
    {
        if (deltaMs <= 0.0001) return 0.0;
        return 1000.0 / deltaMs;
    }

    public double ComputeAverageFps()
    {
        lock (_lock)
        {
            if (_count == 0) return 0.0;
            double sum = 0;
            for (int i = 0; i < _count; i++)
            {
                sum += _ringBuffer[i];
            }
            double avgDelta = sum / _count;
            return CalculateInstantFps(avgDelta);
        }
    }

    public double ComputeFrameTimePercentile(double percentile)
    {
        lock (_lock)
        {
            if (_count == 0) return 0.0;
            if (percentile < 0.0 || percentile > 100.0)
                throw new ArgumentOutOfRangeException(nameof(percentile));

            double[] samples = new double[_count];
            Array.Copy(_ringBuffer, samples, _count);
            Array.Sort(samples);

            if (_count == 1) return samples[0];

            double rank = (percentile / 100.0) * _count;
            int index = Math.Clamp((int)Math.Floor(rank), 0, _count - 1);
            return samples[index];
        }
    }

    public double Compute1PercentLowFps()
    {
        // 1% low is the 99th percentile frame time inverted
        double p99 = ComputeFrameTimePercentile(99.0);
        return CalculateInstantFps(p99);
    }

    public double Compute01PercentLowFps()
    {
        // 0.1% low is the 99.9th percentile frame time inverted
        double p999 = ComputeFrameTimePercentile(99.9);
        return CalculateInstantFps(p999);
    }

    public double ComputeFrametimeVariance()
    {
        lock (_lock)
        {
            if (_count < 2) return 0.0;
            double sum = 0.0;
            for (int i = 0; i < _count; i++)
            {
                sum += _ringBuffer[i];
            }
            double mean = sum / _count;

            double sumSqDiff = 0.0;
            for (int i = 0; i < _count; i++)
            {
                double diff = _ringBuffer[i] - mean;
                sumSqDiff += diff * diff;
            }

            return sumSqDiff / _count;
        }
    }

    public double ComputeFrametimeStdDev()
    {
        return Math.Sqrt(ComputeFrametimeVariance());
    }

    public void Reset()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
            Array.Clear(_ringBuffer, 0, _capacity);
        }
    }
}
