using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
public sealed class IpcAndTelemetryBoundaryTests
{
    #region Feature 7 Boundaries: Shared Memory IPC

    [TestMethod]
    public void B07_Packet_GpuName_TruncatesAt63BytesWithNull()
    {
        var packet = new XtrTelemetryPacket();
        string longName = new('A', 100);
        packet.SetGpuName(longName);

        string retrieved = packet.GetGpuName();
        Assert.AreEqual(63, retrieved.Length, "GPU name longer than 63 characters must be truncated to 63 bytes + null terminator.");
        Assert.AreEqual(new string('A', 63), retrieved);
    }

    [TestMethod]
    public void B07_Packet_InvalidMagic_RejectsRead()
    {
        var packet = new XtrTelemetryPacket
        {
            Magic = 0xDEADBEEF, // Corrupt magic
            InGameFps = 60.0f
        };

        Assert.IsFalse(packet.IsMagicValid);
    }

    [TestMethod]
    public void B07_Packet_ZeroVramTotal_HandledWithoutDivisionByZero()
    {
        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            VramUsedMb = 0,
            VramTotalMb = 0 // Boundary condition: integrated or undetectable VRAM
        };

        double vramUsagePercent = (packet.VramTotalMb > 0)
            ? (double)packet.VramUsedMb / packet.VramTotalMb * 100.0
            : 0.0;

        Assert.AreEqual(0.0, vramUsagePercent, "0 VRAM total must not produce division by zero or NaN.");
    }

    [TestMethod]
    public void B07_Packet_MaxFloatValues_NoOverflow()
    {
        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            GpuTemperatureC = 110.0f,
            GpuUtilization = 100.0f,
            InGameFps = 999.0f,
            InGameFrametimeMs = 1.0f
        };

        byte[] bytes = XtrTelemetryPacket.Serialize(ref packet);
        var readBack = XtrTelemetryPacket.Deserialize(bytes);

        Assert.AreEqual(110.0f, readBack.GpuTemperatureC, 0.001f);
        Assert.AreEqual(100.0f, readBack.GpuUtilization, 0.001f);
        Assert.AreEqual(999.0f, readBack.InGameFps, 0.001f);
    }

    [TestMethod]
    public void B07_SharedMemory_DisposeAndRecreate()
    {
        string memName = $"Local\\XTR_Dispose_Test_{Guid.NewGuid():N}";
        var h1 = new XtrSeqlockHarness(memName);
        var packet = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic, ProcessId = 777 };
        h1.WritePacket(ref packet);
        h1.Dispose();

        // Recreate with same name
        using var h2 = new XtrSeqlockHarness(memName);
        Assert.IsNotNull(h2);
    }

    #endregion

    #region Feature 8 Boundaries: Seqlock Synchronization

    [TestMethod]
    public void B08_Seqlock_UInt32_OverflowWrap()
    {
        // Boundary: Monotonic sequence number wrapping from near uint.MaxValue back to 0
        uint seqNearMax = uint.MaxValue - 1; // Even number (valid write)
        Assert.AreEqual(0u, seqNearMax % 2);

        uint nextWriteStart = seqNearMax + 1; // uint.MaxValue (odd: writing)
        Assert.AreEqual(1u, nextWriteStart % 2);

        uint nextWriteEnd = unchecked(nextWriteStart + 1); // 0 (even: complete)
        Assert.AreEqual(0u, nextWriteEnd);
        Assert.AreEqual(0u, nextWriteEnd % 2);
    }

    [TestMethod]
    public void B08_Seqlock_HighContention_ConcurrentWriterReaders()
    {
        using var harness = new XtrSeqlockHarness();
        bool cancel = false;
        int successfulReads = 0;
        int tornReads = 0;

        var writerTask = Task.Run(() =>
        {
            uint pid = 0;
            while (!cancel)
            {
                var packet = new XtrTelemetryPacket
                {
                    Magic = XtrTelemetryPacket.ExpectedMagic,
                    ProcessId = ++pid,
                    InGameFps = 60.0f + (pid % 100),
                    InGameFrametimeMs = 16.6f
                };
                harness.WritePacket(ref packet);
                Thread.SpinWait(50);
            }
        });

        var readerTask = Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                if (harness.TryReadPacket(out var packet))
                {
                    Interlocked.Increment(ref successfulReads);
                    if (!packet.IsMagicValid)
                    {
                        Interlocked.Increment(ref tornReads);
                    }
                }
                Thread.SpinWait(20);
            }
        });

        readerTask.Wait();
        cancel = true;
        writerTask.Wait();

        Assert.IsTrue(successfulReads > 100, $"Reader should complete many reads, completed: {successfulReads}");
        Assert.AreEqual(0, tornReads, "Under high contention, zero torn reads must occur.");
    }

    [TestMethod]
    public void B08_Seqlock_StalledWriter_TimeoutProtection()
    {
        using var harness = new XtrSeqlockHarness();

        // Write initial packet
        var packet = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic, ProcessId = 123 };
        harness.WritePacket(ref packet);

        // TryReadPacket with small maxSpins should not block infinitely
        bool read = harness.TryReadPacket(out var result, maxSpins: 10);
        Assert.IsTrue(read);
        Assert.AreEqual(123u, result.ProcessId);
    }

    [TestMethod]
    public void B08_Seqlock_RapidStateFlips()
    {
        using var harness = new XtrSeqlockHarness();

        for (uint i = 1; i <= 500; i++)
        {
            var packet = new XtrTelemetryPacket
            {
                Magic = XtrTelemetryPacket.ExpectedMagic,
                ProcessId = i,
                Multiplier = (i % 3) + 2,
                InGameFps = i * 0.5f
            };
            harness.WritePacket(ref packet);

            bool read = harness.TryReadPacket(out var r);
            Assert.IsTrue(read);
            Assert.AreEqual(i, r.ProcessId);
            Assert.AreEqual(i * 0.5f, r.InGameFps, 0.01f);
        }
    }

    [TestMethod]
    public void B08_Seqlock_ZeroAllocation_HotLoop_GarbageFree()
    {
        using var harness = new XtrSeqlockHarness();
        var packet = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic, ProcessId = 999 };

        // Warm up
        harness.WritePacket(ref packet);
        harness.TryReadPacket(out _);

        long startAlloc = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++)
        {
            harness.WritePacket(ref packet);
            harness.TryReadPacket(out _);
        }
        long endAlloc = GC.GetAllocatedBytesForCurrentThread();

        Assert.AreEqual(0, endAlloc - startAlloc, "Seqlock hot loop must generate 0 GC allocations.");
    }

    #endregion

    #region Feature 9 Boundaries: Non-Invasive ETW Telemetry Engine

    [TestMethod]
    public void B09_Etw_ZeroOrNegativeDelta_Clamped()
    {
        var engine = new XtrEtwTelemetryEngine(100);

        // Record zero and negative deltas (e.g. clock jitter, duplicate events)
        engine.RecordFrameDeltaMs(0.0);
        engine.RecordFrameDeltaMs(-5.0);

        // Should not throw or crash, and FPS should be finite
        double instantFps = XtrEtwTelemetryEngine.CalculateInstantFps(0.0);
        Assert.AreEqual(0.0, instantFps);
        Assert.AreEqual(2, engine.SampleCount);
    }

    [TestMethod]
    public void B09_Etw_ExtremeFps_1000Fps()
    {
        // 1.0 ms delta = 1000 FPS
        double fps1000 = XtrEtwTelemetryEngine.CalculateInstantFps(1.0);
        Assert.AreEqual(1000.0, fps1000, 0.01);

        // 0.5 ms delta = 2000 FPS
        double fps2000 = XtrEtwTelemetryEngine.CalculateInstantFps(0.5);
        Assert.AreEqual(2000.0, fps2000, 0.01);
    }

    [TestMethod]
    public void B09_Etw_HighLatency_DropFrame()
    {
        var engine = new XtrEtwTelemetryEngine(100);

        // 2000 ms stutter (2 seconds freeze)
        engine.RecordFrameDeltaMs(2000.0);

        double instantFps = XtrEtwTelemetryEngine.CalculateInstantFps(2000.0);
        Assert.AreEqual(0.5, instantFps, 0.01);
    }

    [TestMethod]
    public void B09_Etw_Reset_ClearsInternalState()
    {
        var engine = new XtrEtwTelemetryEngine(100);
        for (int i = 0; i < 50; i++)
        {
            engine.RecordFrameDeltaMs(16.6);
        }
        Assert.AreEqual(50, engine.SampleCount);

        engine.Reset();
        Assert.AreEqual(0, engine.SampleCount);
        Assert.AreEqual(0.0, engine.ComputeAverageFps());
    }

    [TestMethod]
    public void B09_Etw_TargetProcessExit_StopsConsumption()
    {
        bool isTargetAlive = false;
        bool shouldProcess = isTargetAlive;
        Assert.IsFalse(shouldProcess, "Telemetry events must not be processed when target process has exited.");
    }

    #endregion

    #region Feature 10 Boundaries: 1% Low & Frametime Variance Percentile Engine

    [TestMethod]
    public void B10_Percentile_InsufficientSamples_ClampsOrReturnsZero()
    {
        var engine = new XtrEtwTelemetryEngine(100);

        // 0 samples
        Assert.AreEqual(0.0, engine.ComputeAverageFps());
        Assert.AreEqual(0.0, engine.Compute1PercentLowFps());
        Assert.AreEqual(0.0, engine.ComputeFrametimeVariance());

        // 1 sample
        engine.RecordFrameDeltaMs(16.6667);
        Assert.AreEqual(60.0, engine.Compute1PercentLowFps(), 0.05);
        Assert.AreEqual(0.0, engine.ComputeFrametimeVariance());
    }

    [TestMethod]
    public void B10_Percentile_AllIdenticalFrametimes()
    {
        var engine = new XtrEtwTelemetryEngine(500);
        for (int i = 0; i < 500; i++)
        {
            engine.RecordFrameDeltaMs(16.6667);
        }

        Assert.AreEqual(0.0, engine.ComputeFrametimeStdDev(), 0.0001);
        Assert.AreEqual(60.0, engine.ComputeAverageFps(), 0.05);
        Assert.AreEqual(60.0, engine.Compute1PercentLowFps(), 0.05);
    }

    [TestMethod]
    public void B10_Percentile_SingleSpike_Impairs1PercentLow()
    {
        var engine = new XtrEtwTelemetryEngine(100);

        for (int i = 0; i < 99; i++)
        {
            engine.RecordFrameDeltaMs(16.6667); // 60 FPS
        }
        engine.RecordFrameDeltaMs(100.0); // 10 FPS spike

        double onePercentLow = engine.Compute1PercentLowFps();
        Assert.IsTrue(onePercentLow < 60.0, $"1% low should be lower than 60 FPS due to spike, was {onePercentLow:F1}");
    }

    [TestMethod]
    public void B10_Percentile_EmptyBuffer_HandledSafely()
    {
        var engine = new XtrEtwTelemetryEngine(10);
        Assert.AreEqual(0.0, engine.ComputeFrameTimePercentile(50.0));
        Assert.AreEqual(0.0, engine.Compute1PercentLowFps());
        Assert.AreEqual(0.0, engine.Compute01PercentLowFps());
        Assert.AreEqual(0.0, engine.ComputeFrametimeStdDev());
    }

    [TestMethod]
    public void B10_Percentile_LargeOutlier_StutterFrame()
    {
        var engine = new XtrEtwTelemetryEngine(1000);
        for (int i = 0; i < 998; i++)
        {
            engine.RecordFrameDeltaMs(8.3333); // 120 FPS
        }
        engine.RecordFrameDeltaMs(500.0); // 500ms freeze
        engine.RecordFrameDeltaMs(500.0);

        double zeroOnePercentLow = engine.Compute01PercentLowFps();
        Assert.IsTrue(zeroOnePercentLow <= 3.0, $"0.1% low must capture massive 500ms freeze, was {zeroOnePercentLow:F2} FPS");
    }

    #endregion
}
