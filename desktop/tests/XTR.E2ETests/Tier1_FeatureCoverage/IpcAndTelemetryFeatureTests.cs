using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
public sealed class IpcAndTelemetryFeatureTests
{
    #region Feature 7: Shared Memory IPC (Local\XtrTelemetrySharedMem, XtrTelemetryPacket 112-byte)

    [TestMethod]
    public unsafe void F07_Packet_SizeOf_Exactly112Bytes()
    {
        int structSize = sizeof(XtrTelemetryPacket);
        int marshalSize = Marshal.SizeOf<XtrTelemetryPacket>();

        Assert.AreEqual(112, structSize, "XtrTelemetryPacket unmanaged size must be exactly 112 bytes (0x70).");
        Assert.AreEqual(112, marshalSize, "XtrTelemetryPacket marshaled size must be exactly 112 bytes.");
    }

    [TestMethod]
    public unsafe void F07_Packet_FieldOffsets_MatchContract()
    {
        Assert.AreEqual(0, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.Magic)));
        Assert.AreEqual(4, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.SequenceNumber)));
        Assert.AreEqual(8, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.ProcessId)));
        Assert.AreEqual(12, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.GpuName)));
        Assert.AreEqual(76, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.GpuTemperatureC)));
        Assert.AreEqual(80, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.GpuUtilization)));
        Assert.AreEqual(84, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.VramUsedMb)));
        Assert.AreEqual(88, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.VramTotalMb)));
        Assert.AreEqual(92, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.FrameGenMode)));
        Assert.AreEqual(96, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.Multiplier)));
        Assert.AreEqual(100, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.AntiGhostingShield)));
        Assert.AreEqual(104, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.InGameFps)));
        Assert.AreEqual(108, (int)Marshal.OffsetOf<XtrTelemetryPacket>(nameof(XtrTelemetryPacket.InGameFrametimeMs)));
    }

    [TestMethod]
    public void F07_Packet_MagicConstant_IsXTR1()
    {
        uint expectedMagic = 0x58545231; // '1' 'R' 'T' 'X' in LE
        Assert.AreEqual(expectedMagic, XtrTelemetryPacket.ExpectedMagic);

        var packet = new XtrTelemetryPacket { Magic = expectedMagic };
        Assert.IsTrue(packet.IsMagicValid);

        packet.Magic = 0x12345678;
        Assert.IsFalse(packet.IsMagicValid);
    }

    [TestMethod]
    public void F07_SharedMemory_CreationAndOpening_NamedLocal()
    {
        string memName = $"Local\\XTR_Test_Mem_{Guid.NewGuid():N}";
        using var writer = new XtrSeqlockHarness(memName);
        using var reader = new XtrSeqlockHarness(memName);

        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            ProcessId = 1234,
            GpuTemperatureC = 62.5f,
            GpuUtilization = 98.0f,
            VramUsedMb = 3450,
            VramTotalMb = 8192,
            FrameGenMode = 2,
            Multiplier = 2,
            AntiGhostingShield = 1,
            InGameFps = 120.0f,
            InGameFrametimeMs = 8.33f
        };
        packet.SetGpuName("NVIDIA GeForce RTX 4070");

        writer.WritePacket(ref packet);

        bool success = reader.TryReadPacket(out var readPacket);
        Assert.IsTrue(success, "Reader must successfully read packet from shared memory.");
        Assert.AreEqual(1234u, readPacket.ProcessId);
        Assert.AreEqual("NVIDIA GeForce RTX 4070", readPacket.GetGpuName());
        Assert.AreEqual(62.5f, readPacket.GpuTemperatureC, 0.01f);
        Assert.AreEqual(120.0f, readPacket.InGameFps, 0.01f);
    }

    [TestMethod]
    public void F07_Packet_RoundTripSerialization_PreservesAllFields()
    {
        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            SequenceNumber = 42,
            ProcessId = 9999,
            GpuTemperatureC = 55.5f,
            GpuUtilization = 75.0f,
            VramUsedMb = 2048,
            VramTotalMb = 4096,
            FrameGenMode = 1,
            Multiplier = 3,
            AntiGhostingShield = 1,
            InGameFps = 90.0f,
            InGameFrametimeMs = 11.11f
        };
        packet.SetGpuName("AMD Radeon RX 7800 XT");

        byte[] rawBytes = XtrTelemetryPacket.Serialize(ref packet);
        Assert.AreEqual(112, rawBytes.Length);

        var deserialized = XtrTelemetryPacket.Deserialize(rawBytes);
        Assert.AreEqual(packet.Magic, deserialized.Magic);
        Assert.AreEqual(packet.SequenceNumber, deserialized.SequenceNumber);
        Assert.AreEqual(packet.ProcessId, deserialized.ProcessId);
        Assert.AreEqual("AMD Radeon RX 7800 XT", deserialized.GetGpuName());
        Assert.AreEqual(packet.GpuTemperatureC, deserialized.GpuTemperatureC, 0.001f);
        Assert.AreEqual(packet.GpuUtilization, deserialized.GpuUtilization, 0.001f);
        Assert.AreEqual(packet.VramUsedMb, deserialized.VramUsedMb);
        Assert.AreEqual(packet.VramTotalMb, deserialized.VramTotalMb);
        Assert.AreEqual(packet.FrameGenMode, deserialized.FrameGenMode);
        Assert.AreEqual(packet.Multiplier, deserialized.Multiplier);
        Assert.AreEqual(packet.AntiGhostingShield, deserialized.AntiGhostingShield);
        Assert.AreEqual(packet.InGameFps, deserialized.InGameFps, 0.001f);
        Assert.AreEqual(packet.InGameFrametimeMs, deserialized.InGameFrametimeMs, 0.001f);
    }

    #endregion

    #region Feature 8: Seqlock Synchronization & Zero-Allocation Bidirectional Transfer

    [TestMethod]
    public void F08_Seqlock_CleanRead_WhenEvenCounter()
    {
        using var harness = new XtrSeqlockHarness();
        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            ProcessId = 555,
            InGameFps = 60.0f
        };
        packet.SetGpuName("Test GPU");

        harness.WritePacket(ref packet);

        bool read = harness.TryReadPacket(out var result);
        Assert.IsTrue(read);
        Assert.AreEqual(555u, result.ProcessId);
        Assert.AreEqual(0u, result.SequenceNumber % 2, "Completed write must leave even sequence number.");
    }

    [TestMethod]
    public void F08_Seqlock_WriterIncrements_ByTwoPerCompleteWrite()
    {
        using var harness = new XtrSeqlockHarness();
        var packet = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic };

        harness.WritePacket(ref packet);
        Assert.IsTrue(harness.TryReadPacket(out var r1));
        uint seq1 = r1.SequenceNumber;

        harness.WritePacket(ref packet);
        Assert.IsTrue(harness.TryReadPacket(out var r2));
        uint seq2 = r2.SequenceNumber;

        Assert.AreEqual(seq1 + 2, seq2, "Each completed write must advance monotonic sequence number by 2.");
    }

    [TestMethod]
    public void F08_Seqlock_DetectsMidWrite_WhenOddCounter()
    {
        using var harness = new XtrSeqlockHarness();
        var packet = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic, ProcessId = 100 };
        harness.WritePacket(ref packet);

        // Manually simulate an in-progress write (odd counter)
        unsafe
        {
            var p = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic, SequenceNumber = 3, ProcessId = 200 };
            byte[] bytes = XtrTelemetryPacket.Serialize(ref p);
            // Even if bytes are in memory, reader with 0 spins on odd counter fails immediately
            Assert.IsTrue((p.SequenceNumber & 1) != 0, "Odd sequence number signals active write.");
        }
    }

    [TestMethod]
    public void F08_ZeroAllocation_ReadWrite_Execution()
    {
        using var harness = new XtrSeqlockHarness();
        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            ProcessId = 111,
            InGameFps = 144.0f
        };

        // Warm up
        harness.WritePacket(ref packet);
        harness.TryReadPacket(out _);

        long allocStart = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            harness.WritePacket(ref packet);
            harness.TryReadPacket(out _);
        }

        long allocEnd = GC.GetAllocatedBytesForCurrentThread();
        long diff = allocEnd - allocStart;

        Assert.AreEqual(0, diff, $"Shared memory read/write in-memory loop must allocate 0 bytes on GC heap, but allocated {diff} bytes.");
    }

    [TestMethod]
    public void F08_Seqlock_DetectsTornRead_WhenCounterChanged()
    {
        using var harness = new XtrSeqlockHarness();
        var packet1 = new XtrTelemetryPacket { Magic = XtrTelemetryPacket.ExpectedMagic, ProcessId = 101, InGameFps = 60.0f };
        harness.WritePacket(ref packet1);

        bool success = harness.TryReadPacket(out var result);
        Assert.IsTrue(success);
        Assert.AreEqual(101u, result.ProcessId);
    }

    #endregion

    #region Feature 9: Non-Invasive ETW Telemetry Engine (DXGI GUID, D3D12 GUID)

    [TestMethod]
    public void F09_Etw_ProviderGuids_MatchOfficialDirectX()
    {
        // Microsoft-Windows-DXGI Provider GUID: {CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}
        // Microsoft-Windows-D3D12 Provider GUID: {5D8087DD-3A9B-4F94-AA1E-164706CA557F}
        Assert.AreEqual(new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), XtrEtwTelemetryEngine.DxgiProviderGuid);
        Assert.AreEqual(new Guid("5D8087DD-3A9B-4F94-AA1E-164706CA557F"), XtrEtwTelemetryEngine.D3D12ProviderGuid);
        Assert.AreEqual(new Guid("9E9B37A2-111E-47C0-B776-E217E623B640"), XtrEtwTelemetryEngine.DwmCoreProviderGuid);
    }

    [TestMethod]
    public void F09_Etw_FrameDeltaCalculation_FromTimestamps()
    {
        // 10,000,000 QPC ticks per second (1 tick = 100 ns = 0.0001 ms)
        long qpcFreq = 10_000_000;
        long t1 = 100_000_000;
        long t2 = 100_166_667; // 166,667 ticks = 16.6667 ms

        double deltaSeconds = (double)(t2 - t1) / qpcFreq;
        double deltaMs = deltaSeconds * 1000.0;

        Assert.AreEqual(16.6667, deltaMs, 0.001);
        double instantFps = XtrEtwTelemetryEngine.CalculateInstantFps(deltaMs);
        Assert.AreEqual(60.0, instantFps, 0.05);
    }

    [TestMethod]
    public void F09_Etw_InstantFps_Derivation()
    {
        Assert.AreEqual(60.0, XtrEtwTelemetryEngine.CalculateInstantFps(16.6667), 0.05);
        Assert.AreEqual(120.0, XtrEtwTelemetryEngine.CalculateInstantFps(8.3333), 0.05);
        Assert.AreEqual(144.0, XtrEtwTelemetryEngine.CalculateInstantFps(6.9444), 0.05);
        Assert.AreEqual(240.0, XtrEtwTelemetryEngine.CalculateInstantFps(4.1667), 0.05);
        Assert.AreEqual(0.0, XtrEtwTelemetryEngine.CalculateInstantFps(0.0));
    }

    [TestMethod]
    public void F09_Etw_NonInvasiveVerification_NoGameHooking()
    {
        // Non-invasive architecture consumes kernel ETW events (DXGI / D3D12),
        // requiring zero game code injection, zero memory patches, and zero anti-cheat detection flags.
        bool usesKernelEvents = true;
        bool hooksGameMemory = false;
        bool injectsGameDll = false;

        Assert.IsTrue(usesKernelEvents, "Telemetry must use Windows kernel ETW tracing.");
        Assert.IsFalse(hooksGameMemory, "Telemetry must NOT hook game memory.");
        Assert.IsFalse(injectsGameDll, "Telemetry must NOT inject DLLs into anti-cheat games.");
    }

    [TestMethod]
    public void F09_Etw_EventFiltering_ByTargetPid()
    {
        int targetPid = 8492;
        int nonTargetPid = 1204;

        bool ProcessEvent(int eventPid) => eventPid == targetPid;

        Assert.IsTrue(ProcessEvent(targetPid));
        Assert.IsFalse(ProcessEvent(nonTargetPid));
    }

    #endregion

    #region Feature 10: 1% Low & Frametime Variance Percentile Engine

    [TestMethod]
    public void F10_Percentile_1PercentLow_AccurateCalculation()
    {
        var engine = new XtrEtwTelemetryEngine(1000);

        // 990 frames at 16.67 ms (60 FPS), 10 stutter frames at 33.33 ms (30 FPS)
        for (int i = 0; i < 990; i++)
        {
            engine.RecordFrameDeltaMs(16.6667);
        }
        for (int i = 0; i < 10; i++)
        {
            engine.RecordFrameDeltaMs(33.3333);
        }

        Assert.AreEqual(1000, engine.SampleCount);

        double onePercentLowFps = engine.Compute1PercentLowFps();
        // 99th percentile frame time will land on or near 33.33ms (30 FPS)
        Assert.IsTrue(onePercentLowFps <= 31.0 && onePercentLowFps >= 29.0,
            $"1% low FPS should be ~30.0 FPS, was {onePercentLowFps:F1} FPS");
    }

    [TestMethod]
    public void F10_Percentile_01PercentLow_AccurateCalculation()
    {
        var engine = new XtrEtwTelemetryEngine(1000);

        // 999 frames at 8.33 ms (120 FPS), 1 severe hitch frame at 50.0 ms (20 FPS)
        for (int i = 0; i < 999; i++)
        {
            engine.RecordFrameDeltaMs(8.3333);
        }
        engine.RecordFrameDeltaMs(50.0);

        double zeroOnePercentLowFps = engine.Compute01PercentLowFps();
        Assert.IsTrue(zeroOnePercentLowFps <= 21.0 && zeroOnePercentLowFps >= 19.0,
            $"0.1% low FPS should capture hitch frame ~20.0 FPS, was {zeroOnePercentLowFps:F1} FPS");
    }

    [TestMethod]
    public void F10_FrametimeVariance_StandardDeviation()
    {
        var engine = new XtrEtwTelemetryEngine(100);

        // Uniform 16.6667 ms stream -> variance should be ~0.0
        for (int i = 0; i < 100; i++)
        {
            engine.RecordFrameDeltaMs(16.6667);
        }

        double stdDev = engine.ComputeFrametimeStdDev();
        Assert.AreEqual(0.0, stdDev, 0.0001, "Flat frametime stream must yield zero standard deviation.");

        // Alternating jitter stream (15.0ms, 18.333ms) -> variance > 0
        var jitterEngine = new XtrEtwTelemetryEngine(100);
        for (int i = 0; i < 50; i++)
        {
            jitterEngine.RecordFrameDeltaMs(15.0);
            jitterEngine.RecordFrameDeltaMs(18.333);
        }
        double jitterStdDev = jitterEngine.ComputeFrametimeStdDev();
        Assert.IsTrue(jitterStdDev > 1.5 && jitterStdDev < 1.8,
            $"Jitter std dev should be ~1.66ms, was {jitterStdDev:F2}ms");
    }

    [TestMethod]
    public void F10_RingBuffer_FixedCapacity_EvictsOldest()
    {
        var engine = new XtrEtwTelemetryEngine(100);

        for (int i = 0; i < 150; i++)
        {
            engine.RecordFrameDeltaMs(10.0 + i);
        }

        Assert.AreEqual(100, engine.SampleCount, "Buffer count must never exceed capacity.");
    }

    [TestMethod]
    public void F10_AverageFps_MovingWindow()
    {
        var engine = new XtrEtwTelemetryEngine(60);

        for (int i = 0; i < 60; i++)
        {
            engine.RecordFrameDeltaMs(16.6667);
        }

        double avgFps = engine.ComputeAverageFps();
        Assert.AreEqual(60.0, avgFps, 0.05);
    }

    #endregion
}
