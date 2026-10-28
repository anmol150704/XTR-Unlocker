using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
public sealed class HudAndCompanionFeatureTests
{
    #region Feature 11: In-Game Branded Emerald HUD ([ XTR ENGINE ] ..., F10 toggle)

    [TestMethod]
    public void F11_HudFormatter_FormatString_MatchesTemplate()
    {
        string formatted = XtrHudFormatterContract.FormatHudString(60.2f, 2, 16.64f, "RTX 2050");
        Assert.AreEqual("[ XTR ENGINE ] • FPS: 60 (FG 2X) • 16.6 ms • RTX 2050", formatted);

        string formattedNoFg = XtrHudFormatterContract.FormatHudString(144.0f, 1, 6.94f, "RTX 4090");
        Assert.AreEqual("[ XTR ENGINE ] • FPS: 144 • 6.9 ms • RTX 4090", formattedNoFg);
    }

    [TestMethod]
    public void F11_Hud_Hotkeys_MatchSpecification()
    {
        // Hotkey mappings:
        // ShortcutKey = 0x2D (Insert: Menu)
        // FpsShortcutKey = 0x79 (F10: Toggle HUD)
        // FpsCycleShortcutKey = 0x78 (F9: Cycle Detail Modes)
        // FGShortcutKey = 0x7B (F12: Toggle Frame Gen)
        Assert.AreEqual(0x2D, XtrHudFormatterContract.ShortcutKey_Insert);
        Assert.AreEqual(0x79, XtrHudFormatterContract.FpsShortcutKey_F10);
        Assert.AreEqual(0x78, XtrHudFormatterContract.FpsCycleShortcutKey_F9);
        Assert.AreEqual(0x7B, XtrHudFormatterContract.FGShortcutKey_F12);
    }

    [TestMethod]
    public void F11_Hud_Colors_MatchAppleEmerald()
    {
        // Apple Liquid Emerald palette:
        // Accent: (0.20, 0.78, 0.35)
        // Background: (0.08, 0.09, 0.11, 0.92) / overlay alpha 0.75
        Assert.AreEqual(0.20f, XtrHudFormatterContract.AccentColorR, 0.001f);
        Assert.AreEqual(0.78f, XtrHudFormatterContract.AccentColorG, 0.001f);
        Assert.AreEqual(0.35f, XtrHudFormatterContract.AccentColorB, 0.001f);
        Assert.AreEqual(0.08f, XtrHudFormatterContract.BGColorR, 0.001f);
        Assert.AreEqual(0.09f, XtrHudFormatterContract.BGColorG, 0.001f);
        Assert.AreEqual(0.11f, XtrHudFormatterContract.BGColorB, 0.001f);
        Assert.AreEqual(0.92f, XtrHudFormatterContract.BGColorA, 0.001f);
        Assert.AreEqual(0.75f, XtrHudFormatterContract.FpsOverlayAlpha, 0.001f);
    }

    [TestMethod]
    public void F11_Hud_FGMultiplier_DisplayMapping()
    {
        var p2x = new XtrTelemetryPacket { InGameFps = 120.0f, Multiplier = 2, InGameFrametimeMs = 8.33f };
        p2x.SetGpuName("RTX 3070");
        Assert.IsTrue(XtrHudFormatterContract.FormatFromPacket(p2x).Contains("(FG 2X)"));

        var p3x = new XtrTelemetryPacket { InGameFps = 180.0f, Multiplier = 3, InGameFrametimeMs = 5.55f };
        p3x.SetGpuName("RTX 4080");
        Assert.IsTrue(XtrHudFormatterContract.FormatFromPacket(p3x).Contains("(FG 3X)"));

        var pOff = new XtrTelemetryPacket { InGameFps = 60.0f, Multiplier = 1, InGameFrametimeMs = 16.67f };
        pOff.SetGpuName("RTX 3070");
        Assert.IsFalse(XtrHudFormatterContract.FormatFromPacket(pOff).Contains("(FG"));
    }

    [TestMethod]
    public async Task F11_DeploymentPlan_ConfiguresEmeraldHudHotkeysAndColors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f11_hudplan_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "f11-hud-test", "HUD Game", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("0x79", plan.PlannedConfigModifications.First(m => m.Section == "Menu" && m.Key == "FpsShortcutKey").Value);
            Assert.AreEqual("0x78", plan.PlannedConfigModifications.First(m => m.Section == "Menu" && m.Key == "FpsCycleShortcutKey").Value);
            Assert.AreEqual("0x7B", plan.PlannedConfigModifications.First(m => m.Section == "Menu" && m.Key == "FGShortcutKey").Value);
            Assert.AreEqual("0.20", plan.PlannedConfigModifications.First(m => m.Section == "Menu" && m.Key == "AccentColorR").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Feature 12: Background ProcessWatcher (<500ms launch detection, High priority elevation)

    [TestMethod]
    public void F12_ProcessWatcher_DetectsExecutableLaunch()
    {
        using var watcher = new XtrProcessWatcherContract();
        watcher.RegisterWatchedExecutable("ForzaHorizon6.exe");

        bool launched = false;
        int detectedPid = 0;
        watcher.OnGameLaunched += (pid, exe) =>
        {
            launched = true;
            detectedPid = pid;
        };

        bool handled = watcher.SimulateGameLaunch(7788, "ForzaHorizon6.exe", 150);

        Assert.IsTrue(handled);
        Assert.IsTrue(launched);
        Assert.AreEqual(7788, detectedPid);
    }

    [TestMethod]
    public void F12_ProcessWatcher_DetectionLatency_Under500ms()
    {
        using var watcher = new XtrProcessWatcherContract();
        watcher.RegisterWatchedExecutable("Cyberpunk2077.exe");

        // 350ms detection is within budget
        bool withinBudget = watcher.SimulateGameLaunch(1024, "Cyberpunk2077.exe", 350);
        Assert.IsTrue(withinBudget, "Launch detection under 500ms must be accepted.");

        // 600ms exceeds budget
        bool exceededBudget = watcher.SimulateGameLaunch(1025, "Cyberpunk2077.exe", 600);
        Assert.IsFalse(exceededBudget, "Launch detection over 500ms exceeds SLA limit.");
    }

    [TestMethod]
    public void F12_ProcessWatcher_PriorityElevation_TargetHigh()
    {
        Assert.AreEqual(ProcessPriorityClass.High, XtrProcessWatcherContract.TargetPriority);
    }

    [TestMethod]
    public void F12_ProcessWatcher_GameExit_RestoresState()
    {
        using var watcher = new XtrProcessWatcherContract();
        watcher.RegisterWatchedExecutable("ForzaHorizon6.exe");

        watcher.SimulateGameLaunch(4411, "ForzaHorizon6.exe", 200);
        Assert.IsTrue(watcher.IsMinimizedToTray, "Studio must minimize to tray on launch.");

        bool exitRaised = false;
        watcher.OnGameExited += (pid, exe) => { exitRaised = true; };

        watcher.SimulateGameExit(4411, "ForzaHorizon6.exe");
        Assert.IsFalse(watcher.IsMinimizedToTray, "Studio must restore from tray on game exit.");
        Assert.IsTrue(exitRaised);
    }

    [TestMethod]
    public void F12_ProcessWatcher_FiltersSystemProcesses()
    {
        Assert.IsTrue(XtrProcessWatcherContract.IsSystemProcess("explorer.exe"));
        Assert.IsTrue(XtrProcessWatcherContract.IsSystemProcess("svchost.exe"));
        Assert.IsTrue(XtrProcessWatcherContract.IsSystemProcess("taskmgr.exe"));
        Assert.IsFalse(XtrProcessWatcherContract.IsSystemProcess("ForzaHorizon6.exe"));
        Assert.IsFalse(XtrProcessWatcherContract.IsSystemProcess("Cyberpunk2077.exe"));
    }

    #endregion

    #region Feature 13: Unified Native Fluent Studio (wpfui, <45MB RAM, ~200ms cold startup, 0.0% CPU)

    [TestMethod]
    public void F13_Studio_NoWebviewOrElectronDependencies()
    {
        Assert.IsTrue(XtrStudioFootprintContract.IsProhibitedWebDependency("Microsoft.Web.WebView2.Core"));
        Assert.IsTrue(XtrStudioFootprintContract.IsProhibitedWebDependency("CefSharp.Wpf"));
        Assert.IsTrue(XtrStudioFootprintContract.IsProhibitedWebDependency("Electron"));
        Assert.IsFalse(XtrStudioFootprintContract.IsProhibitedWebDependency("Wpf.Ui"));
    }

    [TestMethod]
    public void F13_Studio_TrayMinimization_PausesRendering()
    {
        using var watcher = new XtrProcessWatcherContract();
        watcher.RegisterWatchedExecutable("game.exe");

        bool trayCallbackFired = false;
        watcher.OnTrayStateChanged += isMinimized => { trayCallbackFired = isMinimized; };

        watcher.SimulateGameLaunch(1234, "game.exe", 100);

        Assert.IsTrue(trayCallbackFired, "Tray state changed callback must be invoked on minimize.");
        Assert.IsTrue(watcher.IsMinimizedToTray);
    }

    [TestMethod]
    public void F13_Studio_MicaBackdrop_DwmAttributeConstants()
    {
        Assert.AreEqual(38, XtrStudioFootprintContract.DWMWA_SYSTEMBACKDROP_TYPE);
        Assert.AreEqual(2, XtrStudioFootprintContract.DWMSBT_MAINWINDOW, "Mica backdrop type constant must be 2.");
        Assert.AreEqual(3, XtrStudioFootprintContract.DWMSBT_TRANSIENTWINDOW, "Acrylic backdrop type constant must be 3.");
    }

    [TestMethod]
    public void F13_Studio_WorkingSetBudget_Validation()
    {
        Assert.AreEqual(45.0, XtrStudioFootprintContract.MaxWorkingSetMbBudget);
        Assert.AreEqual(35.0, XtrStudioFootprintContract.MinWorkingSetMbBudget);
        Assert.AreEqual(0.0, XtrStudioFootprintContract.BackgroundGamingCpuPercentBudget);
    }

    [TestMethod]
    public void F13_Studio_ColdStartupBudget_Validation()
    {
        Assert.AreEqual(200, XtrStudioFootprintContract.TargetColdStartupBudgetMs);
        Assert.AreEqual(250, XtrStudioFootprintContract.MaxColdStartupBudgetMs);
    }

    #endregion
}
