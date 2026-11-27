using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
[DoNotParallelize]
public sealed class MonitoringRemovalAndNativeAppFeatureTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F09: FPS / Telemetry / HUD Overlay Removal in Runtime Config, Pack & Scripts

    [TestMethod]
    public void F09_01_OptiScalerIni_DoesNotEnableFpsOverlayOrShowFps()
    {
        string ini = File.ReadAllText(TestPaths.OptiScalerIniPath);

        string? showFps = RuntimeConfigService.ReadIniValue(ini, "Menu", "ShowFps");
        Assert.IsTrue(showFps is null || showFps.Equals("false", StringComparison.OrdinalIgnoreCase) || showFps.Equals("auto", StringComparison.OrdinalIgnoreCase),
            $"OptiScaler.ini must not enable ShowFps=true; found ShowFps={showFps}");

        string? loadAsi = RuntimeConfigService.ReadIniValue(ini, "Plugins", "LoadAsiPlugins");
        Assert.IsFalse(string.Equals(loadAsi, "true", StringComparison.OrdinalIgnoreCase),
            "OptiScaler.ini must not force LoadAsiPlugins=true.");
    }

    [TestMethod]
    public async Task F09_02_DeploymentPlanner_DoesNotInjectFpsOverlayOrAsiPluginKeys()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f09_no_fps_plan");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        foreach (var preset in new[]
        {
            RuntimeIntegrationPreset.LatestDlssOnly,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            RuntimeIntegrationPreset.NativeDlssG,
            RuntimeIntegrationPreset.TurboBoost
        })
        {
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "f09-plan", "Standard Game", sandbox.GameRoot, pack, preset, gpuInfo: gpu);

            Assert.IsFalse(plan.PlannedConfigModifications.Any(m =>
                m.Key.Equals("ShowFps", StringComparison.OrdinalIgnoreCase) ||
                m.Key.StartsWith("Fps", StringComparison.OrdinalIgnoreCase)),
                $"Preset {preset} must not emit any FPS overlay keys.");

            Assert.IsFalse(plan.PlannedConfigModifications.Any(m =>
                m.Section.Equals("Plugins", StringComparison.OrdinalIgnoreCase) &&
                m.Key.Equals("LoadAsiPlugins", StringComparison.OrdinalIgnoreCase) &&
                m.Value.Equals("true", StringComparison.OrdinalIgnoreCase)),
                $"Preset {preset} must not emit LoadAsiPlugins=true.");
        }
    }

    [TestMethod]
    public void F09_03_BundledPackAndManifest_ExcludeXtrHudAsi()
    {
        Assert.IsFalse(File.Exists(TestPaths.XtrHudAsiPath),
            $"Deprecated plugins/xtr_hud.asi must be deleted from {TestPaths.XtrHudAsiPath}.");

        var asiFiles = Directory.GetFiles(TestPaths.BundledPackagePath, "*.asi", SearchOption.AllDirectories);
        Assert.AreEqual(0, asiFiles.Length, "Bundled OptiScaler runtime pack must contain zero .asi files.");

        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.IsFalse(manifest.FileHashes.Any(f => f.Path.Contains("xtr_hud.asi", StringComparison.OrdinalIgnoreCase)),
            "xtr-runtime.json must not pin plugins/xtr_hud.asi.");
    }

    [TestMethod]
    public void F09_04_BuildWindowsScript_DoesNotRequireXtrHudAsiOrAdvertiseHudPrompt()
    {
        string script = File.ReadAllText(TestPaths.BuildWindowsScriptPath);

        Assert.IsFalse(script.Contains("xtr_hud.asi", StringComparison.OrdinalIgnoreCase),
            "scripts/build-windows.ps1 must not check for or require xtr_hud.asi.");
        Assert.IsFalse(script.Contains("In-Game Apple Glass HUD", StringComparison.OrdinalIgnoreCase),
            "Installer BeginPrompt in build-windows.ps1 must not advertise removed In-Game Apple Glass HUD.");
    }

    [TestMethod]
    public void F09_05_RuntimeHealthReport_OverlayGuide_DoesNotAdvertiseFpsOverlay()
    {
        var report = new RuntimeHealthReport();
        Assert.IsFalse(report.InGameOverlayGuide.Contains("FPS", StringComparison.OrdinalIgnoreCase),
            $"RuntimeHealthReport.InGameOverlayGuide must not reference FPS overlay: '{report.InGameOverlayGuide}'.");
    }

    #endregion

    #region F10: Unified Native App Footprint & No-WebView / No-Monitoring Structural Checks

    [TestMethod]
    public void F10_01_NativeAppContract_ProhibitsWebViewElectronAndLegacyControls()
    {
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("Microsoft.Web.WebView2.Core"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("System.Drawing.Common"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("CommunityToolkit.WinUI.UI.Controls.DataGrid"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("NvAPIWrapper"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("CefSharp.Wpf"));
        Assert.IsTrue(XtrNativeAppContract.IsProhibitedDependency("Electron"));
        Assert.IsFalse(XtrNativeAppContract.IsProhibitedDependency("WinUI.TableView"));
        Assert.IsFalse(XtrNativeAppContract.IsProhibitedDependency("CommunityToolkit.Mvvm"));

        Assert.IsTrue(XtrNativeAppContract.ValidateAssemblyReferences(typeof(RuntimePack).Assembly, out var off),
            $"Test assembly must not reference prohibited dependency: {off}");

        string csproj = File.ReadAllText(TestPaths.DesktopProjectPath);
        Assert.IsFalse(csproj.Contains("WebView2", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(csproj.Contains("Electron", StringComparison.OrdinalIgnoreCase));

        var appDll = XtrNativeAppContract.FindCompiledAppAssemblyPath();
        if (appDll != null)
        {
            Assert.IsTrue(XtrNativeAppContract.ValidatePeAssemblyReferences(appDll, out var offending),
                $"Compiled XTR Unlocker.dll must not reference prohibited dependencies: {string.Join(", ", offending)}");
        }
    }

    [TestMethod]
    public void F10_02_MainWindowXaml_UsesMicaBackdropAndUnifiedNavigation()
    {
        Assert.IsTrue(File.Exists(TestPaths.MainWindowXamlPath));
        string xaml = File.ReadAllText(TestPaths.MainWindowXamlPath);

        Assert.IsTrue(xaml.Contains("MicaBackdrop", StringComparison.Ordinal),
            "MainWindow.xaml must configure Windows 11 <MicaBackdrop />.");
        Assert.IsFalse(xaml.Contains("PresentMon", StringComparison.OrdinalIgnoreCase),
            "MainWindow.xaml must not contain PresentMon monitoring labels.");
    }

    [TestMethod]
    public void F10_03_XtrStudioPageXaml_RemovesFpsTelemetrySimulatorAndPresentMon()
    {
        Assert.IsTrue(File.Exists(TestPaths.XtrStudioPageXamlPath));
        string xaml = File.ReadAllText(TestPaths.XtrStudioPageXamlPath);

        Assert.IsFalse(xaml.Contains("PresentMon", StringComparison.OrdinalIgnoreCase),
            "XtrStudioPage.xaml must not reference PresentMon.");
        Assert.IsFalse(xaml.Contains("XtrTelemetrySharedMem", StringComparison.OrdinalIgnoreCase),
            "XtrStudioPage.xaml must not reference XtrTelemetrySharedMem.");
        Assert.IsFalse(xaml.Contains("SimulatedHudFps", StringComparison.OrdinalIgnoreCase),
            "XtrStudioPage.xaml must not bind SimulatedHudFps.");
        Assert.IsFalse(xaml.Contains("LIVE HUD TELEMETRY SIMULATOR", StringComparison.OrdinalIgnoreCase),
            "XtrStudioPage.xaml must not contain LIVE HUD TELEMETRY SIMULATOR.");
        Assert.IsFalse(xaml.Contains("FPS HUD Overlay Hotkey", StringComparison.OrdinalIgnoreCase),
            "XtrStudioPage.xaml must not expose FPS HUD Overlay Hotkey controls.");
    }

    [TestMethod]
    public void F10_04_GameControlXaml_RemovesFpsOverlayToggleAndPageUpPrompt()
    {
        Assert.IsTrue(File.Exists(TestPaths.GameControlXamlPath));
        string xaml = File.ReadAllText(TestPaths.GameControlXamlPath);

        Assert.IsFalse(xaml.Contains("InGameHudOverlayEnabled", StringComparison.Ordinal),
            "GameControl.xaml must not bind InGameHudOverlayEnabled.");
        Assert.IsFalse(xaml.Contains("SelectedOverlayPosition", StringComparison.Ordinal),
            "GameControl.xaml must not bind SelectedOverlayPosition.");
        Assert.IsFalse(xaml.Contains("In-Game FPS", StringComparison.OrdinalIgnoreCase),
            "GameControl.xaml must not display In-Game FPS overlay toggle.");
        Assert.IsFalse(xaml.Contains("[Page Up]", StringComparison.OrdinalIgnoreCase),
            "GameControl.xaml must not advertise [Page Up] FPS overlay hotkey.");
    }

    [TestMethod]
    public void F10_05_DwmMicaConstants_MatchWindows11FluentSpecification()
    {
        Assert.AreEqual(38, XtrNativeAppContract.DWMWA_SYSTEMBACKDROP_TYPE);
        Assert.AreEqual(2, XtrNativeAppContract.DWMSBT_MAINWINDOW);
        Assert.AreEqual(3, XtrNativeAppContract.DWMSBT_TRANSIENTWINDOW);
        Assert.AreEqual(4, XtrNativeAppContract.DWMSBT_TABBEDWINDOW);
    }

    #endregion
}
