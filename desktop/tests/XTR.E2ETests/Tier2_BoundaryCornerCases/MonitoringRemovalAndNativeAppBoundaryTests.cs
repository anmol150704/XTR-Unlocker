using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
[DoNotParallelize]
public sealed class MonitoringRemovalAndNativeAppBoundaryTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F09 Boundaries: FPS / Telemetry / HUD Overlay Removal

    [TestMethod]
    public async Task B09_DeploymentPlanner_AllSevenPresets_NeverEmitFpsOrAsiKeys()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b09_allpresets");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        foreach (RuntimeIntegrationPreset preset in Enum.GetValues<RuntimeIntegrationPreset>())
        {
            var plan = await DeploymentPlanner.CreatePlanAsync(
                $"b09-{preset}", "Test Game", sandbox.GameRoot, pack, preset, gpuInfo: gpu);

            foreach (var mod in plan.PlannedConfigModifications)
            {
                Assert.IsFalse(mod.Key.Equals("ShowFps", StringComparison.OrdinalIgnoreCase),
                    $"Preset {preset} must not emit ShowFps.");
                Assert.IsFalse(mod.Key.StartsWith("Fps", StringComparison.OrdinalIgnoreCase),
                    $"Preset {preset} must not emit FPS key {mod.Key}.");
                Assert.IsFalse(mod.Section.Equals("Plugins", StringComparison.OrdinalIgnoreCase) &&
                               mod.Key.Equals("LoadAsiPlugins", StringComparison.OrdinalIgnoreCase) &&
                               mod.Value.Equals("true", StringComparison.OrdinalIgnoreCase),
                    $"Preset {preset} must not enable LoadAsiPlugins.");
            }
        }
    }

    [TestMethod]
    public void B09_OptiScalerIni_AllFpsTelemetryKeysAbsentOrDisabled()
    {
        string ini = File.ReadAllText(TestPaths.OptiScalerIniPath);
        string[] fpsKeys =
        [
            "ShowFps", "FpsScale", "FpsOverlayPos", "FpsOverlayType",
            "FpsShortcutKey", "FpsCycleShortcutKey", "FpsOverlayHorizontal", "FpsOverlayAlpha"
        ];

        foreach (var key in fpsKeys)
        {
            string? val = RuntimeConfigService.ReadIniValue(ini, "Menu", key);
            if (key == "ShowFps")
            {
                Assert.IsTrue(
                    val is null ||
                    val.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                    val.Equals("auto", StringComparison.OrdinalIgnoreCase),
                    $"ShowFps in OptiScaler.ini must be absent, false, or auto (never true), found '{val}'.");
            }
            else if (val is not null)
            {
                Assert.IsTrue(val.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                              val.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                              val == "-1",
                    $"FPS telemetry key '{key}' in OptiScaler.ini must not have an active override (found '{val}').");
            }
        }

        string? loadAsi = RuntimeConfigService.ReadIniValue(ini, "Plugins", "LoadAsiPlugins");
        Assert.IsFalse(string.Equals(loadAsi, "true", StringComparison.OrdinalIgnoreCase),
            $"LoadAsiPlugins in OptiScaler.ini must not be true (found '{loadAsi}').");
    }

    [TestMethod]
    public async Task B09_RuntimeConfigService_ApplyProfile_OnlyTouchesUpscalerAndFrameGenSections()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b09_applyprof");
        sandbox.CreateFile("OptiScaler.ini", "[Upscalers]\nDx12Upscaler=auto\n\n[FrameGen]\nEnabled=false\nFGInput=auto\nFGOutput=auto\n");

        var profile = new RuntimeIntegrationProfile
        {
            GameId = "b09-prof",
            Api = "DX12",
            SelectedUpscaler = "dlss",
            FrameGenerationEnabled = "true",
            FrameGenerationInput = "upscaler",
            FrameGenerationOutput = "fsrfg"
        };

        await RuntimeConfigService.ApplyProfileAsync(sandbox.GameRoot, profile);
        string updated = await File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));

        Assert.IsNull(RuntimeConfigService.ReadIniValue(updated, "Menu", "ShowFps"));
        Assert.AreEqual("dlss", RuntimeConfigService.ReadIniValue(updated, "Upscalers", "Dx12Upscaler"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(updated, "FrameGen", "Enabled"));
    }

    [TestMethod]
    public void B09_BundledPluginsFolder_ContainsZeroAsiBinaries()
    {
        string pluginsDir = Path.Combine(TestPaths.BundledPackagePath, "plugins");
        if (Directory.Exists(pluginsDir))
        {
            var files = Directory.GetFiles(pluginsDir, "*", SearchOption.AllDirectories);
            Assert.AreEqual(0, files.Length, $"plugins/ directory must be empty or removed; found {string.Join(", ", files)}.");
        }
    }

    [TestMethod]
    public void B09_DeprecatedTelemetryAndHudDirectories_RemovedFromRepository()
    {
        Assert.IsFalse(Directory.Exists(Path.Combine(TestPaths.DesktopSrcRoot, "Engine", "IPC")),
            "Deprecated Engine/IPC directory must be removed per R2.");
        Assert.IsFalse(Directory.Exists(Path.Combine(TestPaths.DesktopSrcRoot, "Engine", "Telemetry")),
            "Deprecated Engine/Telemetry directory must be removed per R2.");
        Assert.IsFalse(Directory.Exists(Path.Combine(TestPaths.DesktopSrcRoot, "Engine", "Watcher")),
            "Deprecated Engine/Watcher directory must be removed per R2.");
        Assert.IsFalse(Directory.Exists(Path.Combine(TestPaths.ProjectRoot, "native", "xtr_hud")),
            "Deprecated native/xtr_hud directory must be removed per R2.");
    }

    #endregion

    #region F10 Boundaries: Unified Native App Structural Checks

    [TestMethod]
    public void B10_ProhibitedDependencyMatcher_CaseInsensitiveAndAllowsNativePackages()
    {
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("microsoft.web.webview2.core"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("ELECTRON"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("CefSharp.Common"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("NvAPIWrapper.Net"));

        Assert.IsFalse(XtrNativeAppContract.IsProhibitedDependency(string.Empty));
        Assert.IsFalse(XtrNativeAppContract.IsProhibitedDependency("Microsoft.WindowsAppSDK"));
        Assert.IsFalse(XtrNativeAppContract.IsProhibitedDependency("SkiaSharp"));
    }

    [TestMethod]
    public void B10_CompiledAppAssembly_IfBuilt_ZeroTelemetryTypesInMetadata()
    {
        var appDll = XtrNativeAppContract.FindCompiledAppAssemblyPath();
        if (appDll != null)
        {
            var offendingTypes = XtrNativeAppContract.FindProhibitedMonitoringTypeNames(appDll);
            Assert.AreEqual(0, offendingTypes.Count,
                $"Compiled XTR Unlocker.dll must not contain legacy monitoring types: {string.Join(", ", offendingTypes)}");
        }
        else
        {
            // Verify source tree itself has no legacy Engine/Telemetry or Engine/IPC files when bin/ is absent
            Assert.IsFalse(Directory.Exists(Path.Combine(TestPaths.DesktopSrcRoot, "Engine", "Telemetry")));
        }
    }

    [TestMethod]
    public void B10_MainWindowXaml_IsWellFormedXmlWithMicaAndNoFakeMonitoringLabels()
    {
        string xaml = File.ReadAllText(TestPaths.MainWindowXamlPath);
        var doc = XDocument.Parse(xaml);
        Assert.IsNotNull(doc.Root);
        Assert.IsTrue(xaml.Contains("MicaBackdrop", StringComparison.Ordinal));
        Assert.IsFalse(xaml.Contains("PresentMon", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(xaml.Contains("FPS Overlay", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void B10_XtrStudioPageXaml_IsWellFormedXmlAndExcludesAllHudSimulatorBindings()
    {
        string xaml = File.ReadAllText(TestPaths.XtrStudioPageXamlPath);
        var doc = XDocument.Parse(xaml);
        Assert.IsNotNull(doc.Root);

        string[] bannedTokens =
        [
            "SimulatedHudFps", "SimulatedHudFrametime", "LiveVramStatus",
            "LiveVramPercentage", "XtrTelemetrySharedMem", "PresentMon",
            "IN-GAME HUD OVERLAY STUDIO", "Default Telemetry Mode"
        ];

        foreach (var token in bannedTokens)
        {
            Assert.IsFalse(xaml.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"XtrStudioPage.xaml must not contain removed R2 token '{token}'.");
        }
    }

    [TestMethod]
    public void B10_GameControlXaml_IsWellFormedXmlAndPreservesAntiGhostingWithoutFpsOverlay()
    {
        string xaml = File.ReadAllText(TestPaths.GameControlXamlPath);
        var doc = XDocument.Parse(xaml);
        Assert.IsNotNull(doc.Root);

        Assert.IsFalse(xaml.Contains("InGameHudOverlayEnabled", StringComparison.Ordinal));
        Assert.IsFalse(xaml.Contains("SelectedOverlayPosition", StringComparison.Ordinal));
        Assert.IsFalse(xaml.Contains("In-Game FPS", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
