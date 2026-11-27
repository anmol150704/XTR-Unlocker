using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
[DoNotParallelize]
public sealed class Fh6DiscoveryAndSwapFeatureTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack, "Bundled OptiScaler runtime pack must load.");
        return pack;
    }

    #region F01: FH6 Steam / Streamline / DirectSR / FidelityFX-SDK2 Recursive Discovery & NGX OTA

    [TestMethod]
    public async Task F01_01_Fh6SteamLayout_SubdirectoryStreamline_DetectedByPreflight()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f01_fh6steam");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("amd_fidelityfx_dx12.dll", "FFX_DX12");
        sandbox.CreateFile("amd_fidelityfx_upscaler_dx12.dll", "FFX_UP");
        sandbox.CreateFile("amd_fidelityfx_framegeneration_dx12.dll", "FFX_FG");
        sandbox.CreateFile("Plugins/Streamline/sl.interposer.dll", "SL_INTERPOSER");
        sandbox.CreateFile("Plugins/Streamline/sl.directsr.dll", "SL_DIRECTSR");
        sandbox.CreateFile("Plugins/Streamline/sl.dlss.dll", "SL_DLSS");
        sandbox.CreateFile("Plugins/Streamline/sl.dlss_g.dll", "SL_DLSSG");
        sandbox.CreateFile("Plugins/Streamline/sl.common.dll", "SL_COMMON");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync(
            "fh6-steam", sandbox.GameRoot, pack, proxyName: "version.dll", gameTitle: "Forza Horizon 6");

        Assert.IsTrue(preflight.CanProceed, $"FH6 Steam layout must pass preflight: {preflight.Explanation}");
        Assert.AreEqual("DX12", preflight.DetectedApi);
        Assert.IsTrue(preflight.StreamlineDetected, "Subdirectory sl.interposer.dll must be detected recursively.");
        Assert.IsTrue(preflight.NativeDlssGDetected, "Subdirectory sl.dlss_g.dll must be detected as native FG capability.");
        Assert.IsTrue(preflight.StreamlineDlssDetected, "Streamline + DirectSR + NGX OTA layout must report StreamlineDlssDetected.");

        var discovery = UpscalerDiscoveryService.Discover(sandbox.GameRoot);
        Assert.IsTrue(discovery.HasAnyDlssCapability);
        Assert.IsTrue(discovery.FidelityFxSdk2Files.Count >= 2);
    }

    [TestMethod]
    public async Task F01_02_MixedCaseSubdirectoryDlss_DetectedCaseInsensitively()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f01_mixedcase");
        sandbox.CreateFile("Game-Win64-Shipping.exe", "UE5_EXE");
        sandbox.CreateFile("Engine/Binaries/ThirdParty/NVIDIA/NVNGX/Win64/NVNGX_DLSS.DLL", "DLSS_BIN");
        sandbox.CreateFile("Engine/Binaries/ThirdParty/NVIDIA/NVNGX/Win64/NVNGX_DLSSG.DLL", "DLSSG_BIN");
        sandbox.CreateFile("Engine/Binaries/Win64/D3D12Core.dll", "DX12_CORE");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync(
            "ue5-mixed", sandbox.GameRoot, pack, gameTitle: "UE5 Mixed Case");

        Assert.IsTrue(preflight.CanProceed);
        Assert.AreEqual("DX12", preflight.DetectedApi);
        Assert.IsTrue(preflight.NativeDlssDetected, "Mixed-case subdirectory NVNGX_DLSS.DLL must be discovered.");
        Assert.IsTrue(preflight.NativeDlssGDetected, "Mixed-case subdirectory NVNGX_DLSSG.DLL must be discovered.");
    }

    [TestMethod]
    public async Task F01_03_FidelityFxSdk2_CoexistsWithStreamlineInDeploymentPlan()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f01_ffxsdk2");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("amd_fidelityfx_dx12.dll", "FFX_DX12");
        sandbox.CreateFile("amd_fidelityfx_upscaler_dx12.dll", "FFX_UP");
        sandbox.CreateFile("amd_fidelityfx_framegeneration_dx12.dll", "FFX_FG");
        sandbox.CreateFile("Media/Streamline/sl.interposer.dll", "SL_INTERPOSER");
        sandbox.CreateFile("Media/Streamline/sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "fh6-ffx", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsTrue(plan.CanExecute);
        Assert.AreEqual("version.dll", plan.SelectedProxy);
        Assert.AreEqual("dlssg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
    }

    [TestMethod]
    public async Task F01_04_NgxOtaStreamlineOnly_WithoutNvngxDlss_RecognizedAsDlssCapable()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f01_ngxota");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("Runtime/Streamline/sl.interposer.dll", "SL_INTERPOSER");
        sandbox.CreateFile("Runtime/Streamline/sl.directsr.dll", "SL_DIRECTSR");
        sandbox.CreateFile("Runtime/Streamline/sl.common.dll", "SL_COMMON");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync(
            "fh6-ota", sandbox.GameRoot, pack, proxyName: "version.dll", gameTitle: "Forza Horizon 6");

        Assert.IsTrue(preflight.StreamlineDetected);
        Assert.IsTrue(preflight.StreamlineDlssDetected, "NGX OTA + Streamline/DirectSR must report StreamlineDlssDetected.");
        Assert.IsTrue(UpscalerDiscoveryService.Discover(sandbox.GameRoot).HasAnyDlssCapability);
    }

    [TestMethod]
    public async Task F01_05_Fh6NoSpacesTitle_SelectsForzaTechVersionDllAndPassthrough()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f01_nospace", customGameSubfolderName: "ForzaHorizon6");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("Streamline/sl.interposer.dll", "SL");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "ForzaHorizon6", "ForzaHorizon6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsTrue(plan.CanExecute);
        Assert.AreEqual("version.dll", plan.SelectedProxy);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
    }

    #endregion

    #region F02: Swap & Target Resolution Without Prior Records (sl.dlss.dll vs nvngx_dlss.dll Isolation)

    [TestMethod]
    public async Task F02_01_DeployWithoutPriorManifest_CreatesManifestAndProfileCleanly()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f02_noprior");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f02-noprior", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
        Assert.IsNotNull(manifest);
        Assert.AreEqual("version.dll", manifest.ProxyFileName);
        Assert.IsTrue(sandbox.FileExists("version.dll"));
        Assert.IsNotNull(await RuntimeInstallManifestStore.LoadAsync("f02-noprior"));
        Assert.IsNotNull(await RuntimeIntegrationProfileStore.LoadAsync("f02-noprior"));
    }

    [TestMethod]
    public async Task F02_02_StreamlineSlDlss_NeverOverwrittenByRuntimeDeployment()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f02_sldlss");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var slDlssPath = sandbox.CreateFile("Plugins/Streamline/sl.dlss.dll", "STREAMLINE_PLUGIN_SENTINEL");
        var slDlssGPath = sandbox.CreateFile("Plugins/Streamline/sl.dlss_g.dll", "STREAMLINE_FG_SENTINEL");
        string beforeDlssHash = IsolatedGameSandbox.ComputeSha256Hex(slDlssPath);
        string beforeDlssGHash = IsolatedGameSandbox.ComputeSha256Hex(slDlssGPath);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f02-sldlss", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);

        Assert.AreEqual(beforeDlssHash, IsolatedGameSandbox.ComputeSha256Hex(slDlssPath), "sl.dlss.dll must never be overwritten by runtime or nvngx_dlss.dll.");
        Assert.AreEqual(beforeDlssGHash, IsolatedGameSandbox.ComputeSha256Hex(slDlssGPath), "sl.dlss_g.dll must remain intact.");
    }

    [TestMethod]
    public async Task F02_03_NgxDlssAndStreamlinePlugin_RemainIsolatedAcrossInstallAndRestore()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f02_isolation");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var ngxPath = sandbox.CreateFile("Binaries/Win64/nvngx_dlss.dll", "NGX_DLSS_V310");
        var slPath = sandbox.CreateFile("Binaries/Win64/sl.dlss.dll", "SL_DLSS_INTERPOSER");
        string ngxHash = IsolatedGameSandbox.ComputeSha256Hex(ngxPath);
        string slHash = IsolatedGameSandbox.ComputeSha256Hex(slPath);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f02-isolation", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);
        Assert.AreEqual(ngxHash, IsolatedGameSandbox.ComputeSha256Hex(ngxPath));
        Assert.AreEqual(slHash, IsolatedGameSandbox.ComputeSha256Hex(slPath));

        await RuntimeInstallService.RestoreAsync("f02-isolation", sandbox.GameRoot);
        Assert.AreEqual(ngxHash, IsolatedGameSandbox.ComputeSha256Hex(ngxPath));
        Assert.AreEqual(slHash, IsolatedGameSandbox.ComputeSha256Hex(slPath));
    }

    [TestMethod]
    public async Task F02_04_StreamlineSlDlssOnly_DoesNotConflateSlPluginWithNgxDlss()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f02_slonly");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("Streamline/sl.interposer.dll", "SL_INTERPOSER");
        sandbox.CreateFile("Streamline/sl.dlss.dll", "SL_DLSS");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync(
            "f02-slonly", sandbox.GameRoot, pack, gameTitle: "Streamline Title");

        Assert.IsTrue(preflight.StreamlineDetected);
        Assert.IsTrue(preflight.StreamlineDlssDetected);
        Assert.IsFalse(preflight.NativeDlssDetected, "sl.dlss.dll must not be misclassified as local nvngx_dlss.dll.");

        // Verify SwapTargetResolver resolves nvngx_dlss.dll beside Streamline without touching sl.dlss.dll
        var resolution = SwapTargetResolver.ResolveTargetForMissingAsset(
            UpscalerSwapSlot.Dlss, sandbox.GameRoot, knownAssetPaths: null);
        Assert.IsTrue(resolution.CanSwap, $"SwapTargetResolver must allow DLSS deployment without prior records: {resolution.Reason}");
        Assert.AreEqual("nvngx_dlss.dll", resolution.TargetFileName);
        Assert.IsFalse(SwapTargetResolver.IsProtectedStreamlineOrLoaderFileName(resolution.TargetFileName));
        Assert.IsTrue(SwapTargetResolver.IsProtectedStreamlineOrLoaderFileName("sl.dlss.dll"));
    }

    [TestMethod]
    public async Task F02_05_UpgradeWithoutPriorBackup_ReplacesRuntimeCleanlyAndTracksNewEntries()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f02_upgrade");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var initialPlan = await DeploymentPlanner.CreatePlanAsync(
            "f02-upgrade", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        await RuntimeInstallService.InstallPlanAsync(initialPlan);

        var upgradePlan = await DeploymentPlanner.CreatePlanAsync(
            "f02-upgrade", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        var upgradedManifest = await RuntimeInstallService.UpgradePlanAsync(upgradePlan);
        Assert.IsNotNull(upgradedManifest);
        var profile = await RuntimeIntegrationProfileStore.LoadAsync("f02-upgrade");
        Assert.IsNotNull(profile);
        Assert.AreEqual("TurboBoost", profile.Options["Preset"]?.ToString());
    }

    #endregion
}
