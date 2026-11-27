using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
[DoNotParallelize]
public sealed class DispatcherAndAntiGhostingFeatureTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F05: Streamline & Universal Dispatcher Output Selection

    [TestMethod]
    public async Task F05_01_StreamlineTitle_SelectsDlssgInputAndDisablesHudFix()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f05_sl");
        sandbox.CreateFile("Cyberpunk2077.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("sl.interposer.dll", "SL");
        sandbox.CreateFile("sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f05-sl", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        Assert.AreEqual("dlssg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "DisableHUDFix").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy").Value);
    }

    [TestMethod]
    public async Task F05_02_UniversalTitleWithoutStreamline_SelectsUpscalerInputAndEnablesHudFix()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f05_univ");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f05-univ", "Generic DX12 Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        Assert.AreEqual("upscaler", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFixExtended").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy").Value);
    }

    [TestMethod]
    public async Task F05_03_NativeDlssGPreset_ConfiguresNofgBypassAndStreamlineSpoofing()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f05_native");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f05-native", "Native DLSS 3 Title", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.NativeDlssG,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "Enabled").Value);
        Assert.AreEqual("nofg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
        Assert.AreEqual("nofg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "Spoofing" && m.Key == "StreamlineSpoofing").Value);
    }

    [TestMethod]
    public async Task F05_04_ExplicitBackendOverrides_RouteToNukemsAndXefg()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f05_backends");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx3080();

        var nukemsPlan = await DeploymentPlanner.CreatePlanAsync(
            "f05-nukem", "Generic Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            fgBackendOverride: "nukems", gpuInfo: gpu);

        var xefgPlan = await DeploymentPlanner.CreatePlanAsync(
            "f05-xefg", "Generic Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            fgBackendOverride: "xefg", gpuInfo: gpu);

        Assert.AreEqual("nukems", nukemsPlan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        Assert.AreEqual("xefg", xefgPlan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
    }

    [TestMethod]
    public async Task F05_05_InstalledOptiScalerIni_PersistsDispatcherAndSpoofingSettingsOnDisk()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f05_on_disk");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f05-disk", "Universal Test Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            fgBackendOverride: "xefg",
            gpuInfo: IsolatedGameSandbox.CreateIntelArcB580());

        await RuntimeInstallService.InstallPlanAsync(plan);
        string iniContent = await File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(iniContent, "FrameGen", "Enabled"));
        Assert.AreEqual("upscaler", RuntimeConfigService.ReadIniValue(iniContent, "FrameGen", "FGInput"));
        Assert.AreEqual("xefg", RuntimeConfigService.ReadIniValue(iniContent, "FrameGen", "FGOutput"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(iniContent, "OptiFG", "HUDFix"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(iniContent, "Spoofing", "StreamlineSpoofing"));
    }

    #endregion

    #region F06: Racing Anti-Ghosting & High-Velocity Optical Flow Tuning

    [TestMethod]
    public async Task F06_01_RacingTitle_CalibratesAggressiveAntiGhostingValues()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f06_racing");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f06-racing", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.AreEqual("0.05", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);
        Assert.AreEqual("-0.5", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "MinDisOccAcc").Value);
        Assert.AreEqual("2.0", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "ReactiveScale").Value);
        Assert.AreEqual("0.45", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "DlssReactiveMaskBias").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "InitFlags" && m.Key == "DisableReactiveMask").Value);
        Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "UseReactiveMaskForTransparency").Value);
    }

    [TestMethod]
    public async Task F06_02_StandardNonRacingTitle_CalibratesBalancedAntiGhostingValues()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f06_standard");
        sandbox.CreateFile("Cyberpunk2077.exe", "CP_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f06-std", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        Assert.AreEqual("0.15", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);
        Assert.AreEqual("-0.35", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "MinDisOccAcc").Value);
        Assert.AreEqual("1.25", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "ReactiveScale").Value);
    }

    [TestMethod]
    public async Task F06_03_ForzaVirtualTexture_OmitsAnisotropyAndMipmapWhileStandardTurboBoostAppliesThem()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f06_vt");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx3080();

        var fh6Turbo = await DeploymentPlanner.CreatePlanAsync(
            "f06-fh6tb", "Forza Horizon 6", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);
        var cpTurbo = await DeploymentPlanner.CreatePlanAsync(
            "f06-cptb", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);

        Assert.IsFalse(fh6Turbo.PlannedConfigModifications.Any(m => m.Section == "Anisotropy" || m.Section == "Mipmap"),
            "ForzaTech virtual texture streaming must never receive Anisotropy/Mipmap overrides.");
        Assert.AreEqual("16", cpTurbo.PlannedConfigModifications.First(m => m.Section == "Anisotropy" && m.Key == "AnisotropyOverride").Value);
        Assert.AreEqual("-0.5", cpTurbo.PlannedConfigModifications.First(m => m.Section == "Mipmap" && m.Key == "MipmapBiasOverride").Value);
        Assert.AreEqual("0.35", cpTurbo.PlannedConfigModifications.First(m => m.Section == "Sharpness" && m.Key == "Sharpness").Value);
    }

    [TestMethod]
    public void F06_04_BaselineOptiScalerIni_DefinesFsrAndInitFlagsKeys()
    {
        Assert.IsTrue(File.Exists(TestPaths.OptiScalerIniPath));
        string ini = File.ReadAllText(TestPaths.OptiScalerIniPath);

        Assert.IsNotNull(RuntimeConfigService.ReadIniValue(ini, "FSR", "AccAddPerFrame"));
        Assert.IsNotNull(RuntimeConfigService.ReadIniValue(ini, "FSR", "MinDisOccAcc"));
        Assert.IsNotNull(RuntimeConfigService.ReadIniValue(ini, "InitFlags", "DisableReactiveMask"));
    }

    [TestMethod]
    public async Task F06_05_TurboBoostUniversal_AppliesFptLatencyTuningAndPersistsToIni()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f06_fpt");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f06-fpt", "Universal Action Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        await RuntimeInstallService.InstallPlanAsync(plan);
        string ini = await File.ReadAllTextAsync(sandbox.GetFullPath("OptiScaler.ini"));

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(ini, "FSRFG", "FramePacingTuning"));
        Assert.AreEqual("0.01", RuntimeConfigService.ReadIniValue(ini, "FSRFG", "FPTSafetyMarginInMs"));
        Assert.AreEqual("0.3", RuntimeConfigService.ReadIniValue(ini, "FSRFG", "FPTVarianceFactor"));
        Assert.AreEqual("0.15", RuntimeConfigService.ReadIniValue(ini, "FSR", "AccAddPerFrame"));
    }

    #endregion
}
