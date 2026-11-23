using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DLSS_Swapper.Engine.IPC;
using DLSS_Swapper.Engine.Telemetry;
using DLSS_Swapper.Engine.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class XtrEngineTests
{
    [TestMethod]
    public void XtrSharedMemory_WriteAndReadPayload_MatchesExactData()
    {
        string mapName = $"Local\\XtrTestSharedMemory_{Guid.NewGuid():N}";
        using var shm = new XtrSharedMemory(mapName);

        var telemetry = new XtrTelemetryData
        {
            Fps = 144.2f,
            FrameTimeMs = 6.93f,
            CpuFrameTimeMs = 2.45f,
            GpuFrameTimeMs = 4.48f,
            PresentLatencyMs = 7.12f,
            DisplayLatencyMs = 9.85f,
            VramBudgetBytes = 4096UL * 1024UL * 1024UL,
            VramUsedBytes = 2850UL * 1024UL * 1024UL,
            GpuTemperatureC = 64.5f,
            GpuPowerW = 65.0f,
            GpuClockMhz = 1750,
            TargetFrameRate = 144
        };

        var state = new XtrEngineState
        {
            TargetProcessId = (uint)Environment.ProcessId,
            GameTitle = "Cyberpunk 2077",
            ActiveUpscaler = 1, // DLSS
            UpscalerMode = 0,   // Quality
            ActiveDlssPreset = 11, // Preset K (Ampere/2050)
            FrameGenerationActive = 1,
            FrameGenMultiplier = 2,
            InGameHudEnabled = 1,
            InGameHudPosition = 0,
            HudHotkey = 0x2D, // VK_INSERT
            TurboBoostActive = 1
        };

        shm.WritePayload(ref telemetry, ref state);

        bool readSuccess = shm.TryReadPayload(out var readPayload);
        Assert.IsTrue(readSuccess, "Shared memory payload must be readable.");
        Assert.AreEqual(XtrSharedMemory.ExpectedMagic, readPayload.Header.Magic);
        Assert.AreEqual(144.2f, readPayload.Telemetry.Fps, 0.05f);
        Assert.AreEqual(6.93f, readPayload.Telemetry.FrameTimeMs, 0.05f);
        Assert.AreEqual(64.5f, readPayload.Telemetry.GpuTemperatureC, 0.05f);
        Assert.AreEqual((uint)Environment.ProcessId, readPayload.State.TargetProcessId);
        Assert.AreEqual("Cyberpunk 2077", readPayload.State.GameTitle);
        Assert.AreEqual((byte)11, readPayload.State.ActiveDlssPreset);
        Assert.AreEqual((byte)1, readPayload.State.FrameGenerationActive);
        Assert.AreEqual((uint)1, readPayload.State.TurboBoostActive);
    }

    [TestMethod]
    public void XtrSharedMemory_UpdateTelemetryOnly_PreservesExistingState()
    {
        string mapName = $"Local\\XtrTestShmUpdate_{Guid.NewGuid():N}";
        using var shm = new XtrSharedMemory(mapName);

        var state = new XtrEngineState
        {
            TargetProcessId = 12345,
            GameTitle = "Forza Horizon 5",
            ActiveUpscaler = 1,
            ActiveDlssPreset = 13,
            TurboBoostActive = 1
        };
        var initialTelemetry = new XtrTelemetryData { Fps = 60.0f };
        shm.WritePayload(ref initialTelemetry, ref state);

        var updatedTelemetry = new XtrTelemetryData
        {
            Fps = 120.0f,
            FrameTimeMs = 8.33f,
            GpuTemperatureC = 70.0f
        };
        shm.UpdateTelemetry(ref updatedTelemetry);

        bool readSuccess = shm.TryReadPayload(out var payload);
        Assert.IsTrue(readSuccess);
        Assert.AreEqual(120.0f, payload.Telemetry.Fps, 0.05f);
        Assert.AreEqual("Forza Horizon 5", payload.State.GameTitle);
        Assert.AreEqual(12345u, payload.State.TargetProcessId);
        Assert.AreEqual((byte)13, payload.State.ActiveDlssPreset);
    }

    [TestMethod]
    public async Task PresentMonTelemetryEngine_FrameDeltaRecording_CalculatesPercentilesCorrectly()
    {
        using var engine = new PresentMonTelemetryEngine(100);

        // Record a series of 60 FPS (16.66ms) frames with a few 30 FPS (33.33ms) dips
        for (int i = 0; i < 90; i++)
        {
            engine.RecordFrameDelta(16.666);
        }
        for (int i = 0; i < 10; i++)
        {
            engine.RecordFrameDelta(33.333);
        }

        XtrTelemetryFrame? lastFrame = null;
        engine.TelemetryUpdated += (s, f) => lastFrame = f;

        engine.Start((uint)Environment.ProcessId, sampleIntervalMs: 50);
        await Task.Delay(150);
        engine.Stop();

        Assert.IsNotNull(lastFrame, "Engine must produce at least one telemetry frame.");
        Assert.IsTrue(lastFrame.Fps >= 45.0f && lastFrame.Fps <= 65.0f, $"Average FPS must reflect mixed samples, was {lastFrame.Fps}");
        Assert.IsTrue(lastFrame.OnePercentLowFps > 0.0f, "1% Low FPS must be computed.");
        Assert.IsTrue(lastFrame.PointOnePercentLowFps > 0.0f, "0.1% Low FPS must be computed.");
    }

    [TestMethod]
    public void XtrTrayCompanion_TrimWorkingSet_ExecutesSafely()
    {
        // Must execute without exceptions across Windows platforms
        XtrTrayCompanion.TrimWorkingSet();

        using var currentProc = Process.GetCurrentProcess();
        currentProc.Refresh();
        Assert.IsTrue(currentProc.WorkingSet64 > 0, "Working set must remain active and valid.");
    }
}
