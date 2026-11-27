using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier3_CrossFeatureCombinations;

[TestClass]
[DoNotParallelize]
public sealed class PairwiseFeatureInteractionTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    [TestMethod]
    public async Task P01_F01xF04_Fh6SubdirStreamline_ForzaTechVersionDllAndPassthrough()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p01");
        sandbox.CreateFile("ForzaHorizon6.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("Plugins/Streamline/sl.interposer.dll", "SL");
        sandbox.CreateFile("Plugins/Streamline/sl.directsr.dll", "DIRECTSR");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p01", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsTrue(plan.PreflightResult.StreamlineDetected);
        Assert.IsTrue(plan.PreflightResult.StreamlineDlssDetected);
        Assert.AreEqual("version.dll", plan.SelectedProxy);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
    }

    [TestMethod]
    public async Task P02_F01xF05_NestedDirectSrStreamline_RoutesDlssgInputAndDisablesHudFix()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p02");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("Engine/Plugins/Streamline/sl.interposer.dll", "SL");
        sandbox.CreateFile("Engine/Plugins/Streamline/sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p02", "Open World Action", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        Assert.IsTrue(plan.PreflightResult.StreamlineDetected);
        Assert.AreEqual("dlssg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "DisableHUDFix").Value);
    }

    [TestMethod]
    public async Task P03_F01xF02_StreamlineSlDlssIsolation_PreservedDuringRuntimeInstall()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p03");
        sandbox.CreateFile("ForzaHorizon6.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var slDlss = sandbox.CreateFile("Plugins/Streamline/sl.dlss.dll", "SL_DLSS_V2");
        var ngxDlss = sandbox.CreateFile("Plugins/NVNGX/nvngx_dlss.dll", "NGX_DLSS_V310");
        string slHash = IsolatedGameSandbox.ComputeSha256Hex(slDlss);
        string ngxHash = IsolatedGameSandbox.ComputeSha256Hex(ngxDlss);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p03", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);

        Assert.IsTrue(plan.PreflightResult.NativeDlssDetected);
        Assert.AreEqual(slHash, IsolatedGameSandbox.ComputeSha256Hex(slDlss));
        Assert.AreEqual(ngxHash, IsolatedGameSandbox.ComputeSha256Hex(ngxDlss));
    }

    [TestMethod]
    public async Task P04_F02xF03_InstallWithoutPriorManifest_UpgradeThenRestoreFullCycle()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p04");
        sandbox.CreateFile("Cyberpunk2077.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var stockProxy = sandbox.CreateFile("dxgi.dll", "STOCK_DXGI");
        string stockHash = IsolatedGameSandbox.ComputeSha256Hex(stockProxy);

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx3080();

        var p1 = await DeploymentPlanner.CreatePlanAsync("p04", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.LatestDlssOnly, gpuInfo: gpu);
        await RuntimeInstallService.InstallPlanAsync(p1);

        var p2 = await DeploymentPlanner.CreatePlanAsync("p04", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);
        await RuntimeInstallService.UpgradePlanAsync(p2);

        await RuntimeInstallService.RestoreAsync("p04", sandbox.GameRoot);
        Assert.AreEqual(stockHash, IsolatedGameSandbox.ComputeSha256Hex(stockProxy));
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("p04"));
        Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync("p04"));
    }

    [TestMethod]
    public async Task P05_F03xF04_RestoreMissingIniAndModifiedFidelityFx_HealthReflectsCleanState()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p05");
        sandbox.CreateFile("ForzaHorizon6.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p05", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);
        sandbox.CreateFile("OptiScaler.log", "[OptiScaler] Hook D3D12 DLSS\n[OptiFG] fg->Present Frametime: 8.3ms");
        File.Delete(sandbox.GetFullPath("OptiScaler.ini"));

        var brokenHealth = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");
        Assert.IsFalse(brokenHealth.IniFilePresent);
        Assert.IsFalse(brokenHealth.IsRuntimeOperational, "Missing OptiScaler.ini must never report runtime operational.");

        var ffxPath = sandbox.GetFullPath("amd_fidelityfx_dx12.dll");
        await File.WriteAllTextAsync(ffxPath, "EXTERNAL_FFX_UPDATE");

        await RuntimeInstallService.RestoreAsync("p05", sandbox.GameRoot);
        Assert.IsTrue(File.Exists(ffxPath));
        Assert.IsFalse(sandbox.FileExists("version.dll"));
    }

    [TestMethod]
    public async Task P06_F03xF09_InstalledOptiScalerIni_HasZeroFpsKeysAcrossUpgrade()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p06");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        var p1 = await DeploymentPlanner.CreatePlanAsync("p06", "Universal Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
        await RuntimeInstallService.InstallPlanAsync(p1);

        var p2 = await DeploymentPlanner.CreatePlanAsync("p06", "Universal Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);
        await RuntimeInstallService.UpgradePlanAsync(p2);

        string ini = await File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));
        string? showFps = RuntimeConfigService.ReadIniValue(ini, "Menu", "ShowFps");
        Assert.IsTrue(
            showFps is null ||
            showFps.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            showFps.Equals("auto", StringComparison.OrdinalIgnoreCase),
            $"ShowFps across upgrade must be absent, false, or auto (never true), found '{showFps}'.");

        string[] fpsKeys =
        [
            "FpsScale", "FpsOverlayPos", "FpsOverlayType",
            "FpsShortcutKey", "FpsCycleShortcutKey", "FpsOverlayHorizontal", "FpsOverlayAlpha"
        ];
        foreach (var key in fpsKeys)
        {
            string? val = RuntimeConfigService.ReadIniValue(ini, "Menu", key);
            if (val is not null)
            {
                Assert.IsTrue(val.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                              val.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                              val == "-1",
                    $"Installed OptiScaler.ini key '{key}' must be absent/auto/false/-1 across upgrade, found '{val}'.");
            }
        }

        Assert.IsFalse(string.Equals(RuntimeConfigService.ReadIniValue(ini, "Plugins", "LoadAsiPlugins"), "true", StringComparison.OrdinalIgnoreCase),
            "Installed OptiScaler.ini must not force LoadAsiPlugins=true.");
    }

    [TestMethod]
    public async Task P07_F04xF09_HealthReport_ProxyMissingAndGuideFpsFree()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p07");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p07", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        await RuntimeInstallService.InstallPlanAsync(plan);
        File.Delete(sandbox.GetFullPath("dxgi.dll"));

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "dxgi.dll");
        Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
        Assert.IsFalse(report.FrameGenerationActive);
        Assert.IsFalse(report.InGameOverlayGuide.Contains("FPS", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task P08_F05xF06_UniversalDispatcher_CalibratesMvCopyAndAntiGhosting()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p08");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p08", "Universal Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "InitFlags" && m.Key == "DisableReactiveMask").Value);
        Assert.AreEqual("0.45", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "DlssReactiveMaskBias").Value);
    }

    [TestMethod]
    public async Task P09_F05xF07_NvidiaAdaWithDlssG_TurboBoostNativeBypassVsForzaException()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p09");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("sl.interposer.dll", "SL");
        sandbox.CreateFile("sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var rtx4070 = IsolatedGameSandbox.CreateRtx4070();

        var cpPlan = await DeploymentPlanner.CreatePlanAsync(
            "p09-cp", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: rtx4070);
        var fh6Plan = await DeploymentPlanner.CreatePlanAsync(
            "p09-fh6", "Forza Horizon 6", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: rtx4070);

        Assert.AreEqual("nofg", cpPlan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("13", cpPlan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);

        Assert.AreEqual("dlssg", fh6Plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("13", fh6Plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
    }

    [TestMethod]
    public async Task P10_F06xF07_RacingTitleWithRtx2050_PresetKPlusRacingAntiGhosting()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p10");
        sandbox.CreateFile("ForzaHorizon6.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p10", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx2050Mobile4Gb());

        Assert.AreEqual("11", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
        Assert.AreEqual("0.05", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);
        Assert.AreEqual("-0.5", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "MinDisOccAcc").Value);
    }

    [TestMethod]
    public async Task P11_F06xF08_CompatibilityProfileAndRacingTuning_CoexistInPlan()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p11");
        sandbox.CreateFile("ForzaHorizon5.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var compat = CompatibilityDatabase.LookupGame("Forza Horizon 5");
        Assert.IsNotNull(compat);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p11", compat.Title, sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        Assert.AreEqual(compat.RecommendedProxy, plan.SelectedProxy);
        Assert.AreEqual("0.05", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);
        Assert.AreEqual("2.0", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "ReactiveScale").Value);
    }

    [TestMethod]
    public async Task P12_F07xF05_FourGbVramNonNvidia_Fsr31PlusAlwaysTrackHeapsFalse()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p12");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p12", "Generic DX12 Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx2050Mobile4Gb());

        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
        Assert.AreEqual("upscaler", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
    }

    [TestMethod]
    public async Task P13_F08xF03_MultiplayerAntiCheatBlock_PreventsInstallAndLeavesZeroManifest()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p13");
        sandbox.CreateFile("VALORANT-Win64-Shipping.exe", "EXE");
        sandbox.CreateFile("vgk.sys", "VANGUARD");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p13-val", "VALORANT", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsFalse(plan.CanExecute);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RuntimeInstallService.InstallPlanAsync(plan));
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("p13-val"));
        Assert.IsFalse(sandbox.FileExists("dxgi.dll"));
    }

    [TestMethod]
    public async Task P14_F08xF01_OfflineOverrideWithSubdirDlss_DetectsDlssAndAllowsPlan()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p14");
        sandbox.CreateFile("eldenring.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("EasyAntiCheat/EasyAntiCheat.exe", "EAC");
        sandbox.CreateFile("Mods/DLSS/nvngx_dlss.dll", "DLSS");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p14-er", "Elden Ring", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080(),
            allowOfflineAntiCheatOverride: true);

        Assert.IsTrue(plan.CanExecute);
        Assert.IsTrue(plan.PreflightResult.HasAntiCheatRisk);
        Assert.IsTrue(plan.PreflightResult.NativeDlssDetected);
        Assert.AreEqual(PreflightStatus.NeedsAttention, plan.PreflightResult.Status);
    }

    [TestMethod]
    public async Task P15_F09xF11_ManifestAndPackOnDisk_ZeroXtrHudAsiAndExact18Hashes()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.AreEqual(18, manifest.FileHashes.Count);
        Assert.IsFalse(manifest.FileHashes.Any(f => f.Path.Contains("xtr_hud.asi", StringComparison.OrdinalIgnoreCase)));

        var pack = await LoadPackAsync();
        Assert.IsTrue(pack.IsTrusted);
        Assert.AreEqual(0, XtrPackagingValidatorContract.FindUntrackedRuntimeFiles(TestPaths.BundledPackagePath, manifest).Count);
    }

    [TestMethod]
    public void P16_F10xF09_UnifiedNativeApp_MicaAndZeroMonitoringUi()
    {
        string mainXaml = File.ReadAllText(TestPaths.MainWindowXamlPath);
        string studioXaml = File.ReadAllText(TestPaths.XtrStudioPageXamlPath);
        string gameCtrlXaml = File.ReadAllText(TestPaths.GameControlXamlPath);

        Assert.IsTrue(mainXaml.Contains("MicaBackdrop", StringComparison.Ordinal));
        Assert.IsFalse(studioXaml.Contains("PresentMon", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(studioXaml.Contains("SimulatedHudFps", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(gameCtrlXaml.Contains("InGameHudOverlayEnabled", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task P17_F11xF03_TamperedPack_BlockedBeforeAnyFileCopiesOrBackups()
    {
        using var sandbox = new IsolatedGameSandbox("t3_p17");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var packRoot = sandbox.CopyBundledPackToTemp();

        var pack = await RuntimePack.LoadFromDirectoryAsync(packRoot);
        Assert.IsNotNull(pack);
        Assert.IsTrue(pack.IsTrusted);

        await File.AppendAllTextAsync(Path.Combine(packRoot, "fakenvapi.ini"), "\n# tamper");

        var plan = await DeploymentPlanner.CreatePlanAsync(
            "p17", "Generic Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsFalse(plan.CanExecute);
        Assert.AreEqual(0, plan.PlannedCopies.Count);
        Assert.AreEqual(0, plan.PlannedBackups.Count);
    }

    [TestMethod]
    public void P18_F12xF11_ReleasePackagingAndVersionBadges_AlignedWithBundledPack()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        string script = File.ReadAllText(TestPaths.BuildWindowsScriptPath);
        string workflow = File.ReadAllText(TestPaths.ReleaseWorkflowPath);

        Assert.AreEqual("0.9.5", manifest.Version);
        Assert.AreEqual("DLSS 310 - v310.6", DlssVersionHelper.FormatVersionLabel("310.6.0.0"));
        Assert.IsTrue(script.Contains("XTR-Unlocker-Setup.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(workflow.Contains("XTR-Unlocker-Setup.exe", StringComparison.OrdinalIgnoreCase));
    }
}
