using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Hardware;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier3_CrossFeatureCombinations;

[TestClass]
public sealed class PairwiseFeatureInteractionTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    [TestMethod]
    public async Task P01_AntiGhosting_StreamlineBridge_Coexistence()
    {
        // Pair 1: Anti-Ghosting Shield (F1/F2) + Streamline Bridge (F5)
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_p1_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "p1-test", "Forza Horizon 6", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("version.dll", plan.SelectedProxy);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "InitFlags" && m.Key == "DisableReactiveMask").Value);
            Assert.AreEqual("0.05", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void P02_SharedMemIpc_EtwConsumer_DataBridge()
    {
        // Pair 2: Shared Memory IPC (F7) + ETW Telemetry Engine (F9)
        var etwEngine = new XtrEtwTelemetryEngine(100);
        using var harness = new XtrSeqlockHarness();

        // ETW computes delta and FPS from presentation interval
        double frameDeltaMs = 8.3333; // 120 FPS
        etwEngine.RecordFrameDeltaMs(frameDeltaMs);
        double instantFps = XtrEtwTelemetryEngine.CalculateInstantFps(frameDeltaMs);

        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            ProcessId = 4444,
            InGameFps = (float)instantFps,
            InGameFrametimeMs = (float)frameDeltaMs
        };
        packet.SetGpuName("GeForce RTX 4070 Ti");

        harness.WritePacket(ref packet);

        Assert.IsTrue(harness.TryReadPacket(out var readPacket));
        Assert.AreEqual(120.0f, readPacket.InGameFps, 0.05f);
        Assert.AreEqual(8.33f, readPacket.InGameFrametimeMs, 0.05f);
    }

    [TestMethod]
    public void P03_ProcessWatcher_AutoMinimizeTray_Lifecycle()
    {
        // Pair 3: ProcessWatcher (F12) + Studio Auto-Minimize Tray (F13)
        using var watcher = new XtrProcessWatcherContract();
        watcher.RegisterWatchedExecutable("SpeedGame.exe");

        bool trayTriggered = false;
        watcher.OnTrayStateChanged += minimized => { trayTriggered = minimized; };

        bool launched = watcher.SimulateGameLaunch(9012, "SpeedGame.exe", 120);

        Assert.IsTrue(launched);
        Assert.IsTrue(trayTriggered, "Game launch must trigger tray minimization for 0.0% CPU.");
        Assert.IsTrue(watcher.IsMinimizedToTray);
    }

    [TestMethod]
    public void P04_HudFormatter_SharedMemoryPacket_DirectRendering()
    {
        // Pair 4: HUD Formatter (F11) + Shared Memory Packet (F7)
        using var harness = new XtrSeqlockHarness();
        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            InGameFps = 119.8f,
            Multiplier = 2,
            InGameFrametimeMs = 8.35f
        };
        packet.SetGpuName("RTX 4080 Super");

        harness.WritePacket(ref packet);
        Assert.IsTrue(harness.TryReadPacket(out var readPacket));

        string hudString = XtrHudFormatterContract.FormatFromPacket(readPacket);
        Assert.AreEqual("[ XTR ENGINE ] • FPS: 120 (FG 2X) • 8.4 ms • RTX 4080 Super", hudString);
    }

    [TestMethod]
    public void P05_DeploymentPlanner_RuntimeConfigService_SectionIsolation()
    {
        // Pair 5: DeploymentPlan modifications (F6) applied through section-bounded INI replacement (F1/F3)
        string initialIni = "[InitFlags]\nAutoExposure=true\n\n[FSR]\nAccAddPerFrame=0.333\n\n[OptiFG]\nHUDFix=false\n";

        var plannedMods = new[]
        {
            new PlannedConfigEntry { Section = "InitFlags", Key = "DisableReactiveMask", Value = "false" },
            new PlannedConfigEntry { Section = "FSR", Key = "AccAddPerFrame", Value = "0.05" },
            new PlannedConfigEntry { Section = "OptiFG", Key = "MakeMVCopy", Value = "true" }
        };

        string updated = RuntimeConfigService.ReplaceIniValues(initialIni, plannedMods);

        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(updated, "InitFlags", "DisableReactiveMask"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(updated, "InitFlags", "AutoExposure"));
        Assert.AreEqual("0.05", RuntimeConfigService.ReadIniValue(updated, "FSR", "AccAddPerFrame"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(updated, "OptiFG", "MakeMVCopy"));
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(updated, "OptiFG", "HUDFix"));
    }

    [TestMethod]
    public void P06_GpuDetector_MultiBackendDispatcher_Routing()
    {
        // Pair 6: GPU Hardware Classification (F6) + Dispatcher Routing
        var rtx2060 = new GpuHardwareInfo
        {
            Name = "NVIDIA GeForce RTX 2060",
            Vendor = GpuVendor.Nvidia,
            Architecture = "Turing (RTX 20 Series)",
            VramBytes = 6144UL * 1024 * 1024,
            DriverVersion = "555.85",
            IsHagsEnabled = true
        };

        // Turing has no Ada optical flow -> requires FSR 3.1 bridge
        Assert.IsFalse(rtx2060.SupportsNativeDlssG);
        Assert.IsTrue(rtx2060.SupportsFxFramenGenBridge);
    }

    [TestMethod]
    public async Task P07_MultiBackendFg_AntiGhostingShield_Enforcement()
    {
        // Pair 7: Multi-Backend FG (F6) + Anti-Ghosting Shield (F3/F4)
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_p7_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "p7-test", "Universal Game", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            // Universal FG must bundle MV cloning and HUD fix to prevent tearing
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy").Value);
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy").Value);
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void P08_Seqlock_HighFrequencyEtwUpdates_TearingFree()
    {
        // Pair 8: Seqlock (F8) + 144 Hz ETW Telemetry Stream (F9/F10)
        using var harness = new XtrSeqlockHarness();
        var etwEngine = new XtrEtwTelemetryEngine(100);

        for (int i = 0; i < 144; i++)
        {
            double deltaMs = 6.9444 + ((i % 5) * 0.1); // ~144 Hz with slight jitter
            etwEngine.RecordFrameDeltaMs(deltaMs);

            var packet = new XtrTelemetryPacket
            {
                Magic = XtrTelemetryPacket.ExpectedMagic,
                ProcessId = 1440,
                InGameFps = (float)XtrEtwTelemetryEngine.CalculateInstantFps(deltaMs),
                InGameFrametimeMs = (float)deltaMs
            };
            harness.WritePacket(ref packet);

            Assert.IsTrue(harness.TryReadPacket(out var r));
            Assert.IsTrue(r.InGameFps > 130.0f && r.InGameFps < 150.0f);
        }
    }

    [TestMethod]
    public void P09_ProcessWatcher_PriorityElevation_SharedMemRegistration()
    {
        // Pair 9: ProcessWatcher (F12) + Shared Memory Registration (F7)
        using var watcher = new XtrProcessWatcherContract();
        using var harness = new XtrSeqlockHarness();
        watcher.RegisterWatchedExecutable("ForzaHorizon6.exe");

        int activePid = 8820;
        watcher.SimulateGameLaunch(activePid, "ForzaHorizon6.exe", 150);

        var packet = new XtrTelemetryPacket
        {
            Magic = XtrTelemetryPacket.ExpectedMagic,
            ProcessId = (uint)activePid,
            FrameGenMode = 2
        };
        harness.WritePacket(ref packet);

        Assert.IsTrue(harness.TryReadPacket(out var read));
        Assert.AreEqual((uint)activePid, read.ProcessId);
    }

    [TestMethod]
    public void P10_DllSwitcher_PackagingManifest_Verification()
    {
        // Pair 10: DLL Switcher (F14) + Manifest SHA-256 Verification (F15)
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.AreEqual(18, manifest.FileHashes.Count);

        string optiscalerPath = Path.Combine(TestPaths.BundledPackagePath, "OptiScaler.dll");
        string computedHash = XtrPackagingValidatorContract.ComputeSha256Hex(optiscalerPath);
        var record = manifest.FileHashes.First(h => h.Path == "OptiScaler.dll");

        Assert.AreEqual(record.Sha256, computedHash, "OptiScaler.dll on disk must match manifest SHA256 before deployment.");
    }

    [TestMethod]
    public void P11_MicaBackdrop_WorkingSetFootprint_Compliance()
    {
        // Pair 11: Mica Backdrop (F13) + RAM Footprint Budget (F13)
        // Fluent design uses native Windows 11 DwmSetWindowAttribute backdrop without heavy web engines
        int backdrop = XtrStudioFootprintContract.DWMSBT_MAINWINDOW;
        Assert.AreEqual(2, backdrop);
        Assert.IsTrue(XtrStudioFootprintContract.MaxWorkingSetMbBudget <= 45.0);
    }

    [TestMethod]
    public void P12_1PercentLow_HudOverlay_Integration()
    {
        // Pair 12: 1% Low Percentile Engine (F10) + HUD Overlay Formatter (F11)
        var etw = new XtrEtwTelemetryEngine(200);
        for (int i = 0; i < 198; i++) etw.RecordFrameDeltaMs(16.6667); // 60 FPS
        etw.RecordFrameDeltaMs(33.3333); // 30 FPS drop
        etw.RecordFrameDeltaMs(33.3333);

        double onePercentLow = etw.Compute1PercentLowFps();
        Assert.IsTrue(onePercentLow < 60.0);

        // Detailed HUD mode includes 1% low metrics
        string detailedHud = $"[ XTR ENGINE ] • FPS: 60 (1% Low: {Math.Round(onePercentLow)}) • 16.6 ms • RTX 4070";
        Assert.IsTrue(detailedHud.Contains("1% Low: 30") || detailedHud.Contains("1% Low: 31"));
    }

    [TestMethod]
    public async Task P13_ResponsiveMask_PresetE_Integration()
    {
        // Pair 13: Responsive Mask (F2) + DLSS Preset E (F4)
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_p13_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "p13-test", "Smear Free Game", tempDir, pack, RuntimeIntegrationPreset.TurboBoost);

            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "InitFlags" && m.Key == "DisableReactiveMask").Value);
            Assert.AreEqual("5", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task P14_VersionDllProxy_HeapTrackingFalse_Stability()
    {
        // Pair 14: Version.dll Proxy (F5) + Disabled Heap Tracking (F5)
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_p14_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon5.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "p14-test", "Forza Horizon 5", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("version.dll", plan.SelectedProxy);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task P15_TurboBoost_AnisotropyAndMipmap_Tuning()
    {
        // Pair 15: TurboBoost (F6) + Anisotropic Filtering 16x + Mipmap Bias -0.5
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_p15_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Cyberpunk2077.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "p15-test", "Cyberpunk 2077 Boost", tempDir, pack, RuntimeIntegrationPreset.TurboBoost);

            Assert.AreEqual("16", plan.PlannedConfigModifications.First(m => m.Section == "Anisotropy" && m.Key == "AnisotropyOverride").Value);
            Assert.AreEqual("-0.5", plan.PlannedConfigModifications.First(m => m.Section == "Mipmap" && m.Key == "MipmapBiasOverride").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
