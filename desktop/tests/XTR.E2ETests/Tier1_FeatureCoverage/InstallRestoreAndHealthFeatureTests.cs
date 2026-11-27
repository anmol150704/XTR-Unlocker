using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
[DoNotParallelize]
public sealed class InstallRestoreAndHealthFeatureTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F03: Transactional Install, In-Place Upgrade & Tolerant Restore Recovery

    [TestMethod]
    public async Task F03_01_InstallAndRestore_RoundTrip_RestoresStockFilesAndDeletesProfileAndManifest()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f03_roundtrip");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var stockProxyPath = sandbox.CreateFile("version.dll", "STOCK_VERSION_DLL");
        var stockFfxPath = sandbox.CreateFile("amd_fidelityfx_dx12.dll", "STOCK_FFX_DX12");
        string stockProxyHash = IsolatedGameSandbox.ComputeSha256Hex(stockProxyPath);
        string stockFfxHash = IsolatedGameSandbox.ComputeSha256Hex(stockFfxPath);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f03-roundtrip", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);
        Assert.AreNotEqual(stockProxyHash, IsolatedGameSandbox.ComputeSha256Hex(stockProxyPath));
        Assert.IsTrue(sandbox.FileExists("OptiScaler.ini"));
        Assert.IsNotNull(await RuntimeIntegrationProfileStore.LoadAsync("f03-roundtrip"));

        await RuntimeInstallService.RestoreAsync("f03-roundtrip", sandbox.GameRoot);

        Assert.AreEqual(stockProxyHash, IsolatedGameSandbox.ComputeSha256Hex(stockProxyPath), "Stock version.dll must be restored identically.");
        Assert.AreEqual(stockFfxHash, IsolatedGameSandbox.ComputeSha256Hex(stockFfxPath), "Stock amd_fidelityfx_dx12.dll must be restored identically.");
        Assert.IsFalse(sandbox.FileExists("OptiScaler.ini"), "XTR-added OptiScaler.ini must be removed on restore.");
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("f03-roundtrip"), "Manifest must be deleted after restore.");
        Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync("f03-roundtrip"), "Integration profile must be deleted after restore.");
    }

    [TestMethod]
    public async Task F03_02_UpgradeInPlace_PreservesOriginalStockBackupAcrossMultipleUpgrades()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f03_upgradebackup");
        sandbox.CreateFile("Cyberpunk2077.exe", "CP_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var stockProxyPath = sandbox.CreateFile("dxgi.dll", "ORIGINAL_STOCK_DXGI");
        string stockHash = IsolatedGameSandbox.ComputeSha256Hex(stockProxyPath);

        var pack = await LoadPackAsync();
        var plan1 = await DeploymentPlanner.CreatePlanAsync(
            "f03-upbackup", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());
        await RuntimeInstallService.InstallPlanAsync(plan1);

        var plan2 = await DeploymentPlanner.CreatePlanAsync(
            "f03-upbackup", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());
        await RuntimeInstallService.UpgradePlanAsync(plan2);

        await RuntimeInstallService.RestoreAsync("f03-upbackup", sandbox.GameRoot);
        Assert.AreEqual(stockHash, IsolatedGameSandbox.ComputeSha256Hex(stockProxyPath),
            "In-place upgrade must preserve the original stock backup and never overwrite it with intermediate OptiScaler files.");
    }

    [TestMethod]
    public async Task F03_03_Restore_WhenXtrAddedFileMissing_SkipsMissingEntryAndCompletes()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f03_missingini");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f03-missingini", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);

        // Simulate missing OptiScaler.ini and fakenvapi.ini (both OriginalBackupPath == null)
        File.Delete(sandbox.GetFullPath("OptiScaler.ini"));
        File.Delete(sandbox.GetFullPath("fakenvapi.ini"));

        await RuntimeInstallService.RestoreAsync("f03-missingini", sandbox.GameRoot);

        Assert.IsFalse(sandbox.FileExists("version.dll"), "Remaining XTR proxy must be removed cleanly.");
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("f03-missingini"));
        Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync("f03-missingini"));
    }

    [TestMethod]
    public async Task F03_04_Restore_WhenBackedUpStockFileDeleted_RestoresFromBackup()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f03_missingstock");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var stockFfxPath = sandbox.CreateFile("amd_fidelityfx_dx12.dll", "STOCK_FH6_FFX_DX12");
        string stockFfxHash = IsolatedGameSandbox.ComputeSha256Hex(stockFfxPath);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f03-missingstock", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);

        // Delete amd_fidelityfx_dx12.dll externally while backup exists
        File.Delete(stockFfxPath);
        Assert.IsFalse(File.Exists(stockFfxPath));

        await RuntimeInstallService.RestoreAsync("f03-missingstock", sandbox.GameRoot);

        Assert.IsTrue(File.Exists(stockFfxPath), "Backed-up stock file deleted from game folder must be restored via File.Copy.");
        Assert.AreEqual(stockFfxHash, IsolatedGameSandbox.ComputeSha256Hex(stockFfxPath));
    }

    [TestMethod]
    public async Task F03_05_Restore_WhenNonBackedUpVendorFileExternallyModified_PreservesFileAndCompletes()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f03_modffx");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f03-modffx", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);

        // Simulate Steam verify / game update replacing amd_fidelityfx_dx12.dll (OriginalBackupPath == null)
        var ffxPath = sandbox.GetFullPath("amd_fidelityfx_dx12.dll");
        await File.WriteAllTextAsync(ffxPath, "STEAM_VERIFIED_NEWER_FFX_SDK2");
        string modifiedHash = IsolatedGameSandbox.ComputeSha256Hex(ffxPath);

        await RuntimeInstallService.RestoreAsync("f03-modffx", sandbox.GameRoot);

        Assert.IsTrue(File.Exists(ffxPath), "Externally updated vendor file (OriginalBackupPath == null) must be preserved, not deleted or blocked.");
        Assert.AreEqual(modifiedHash, IsolatedGameSandbox.ComputeSha256Hex(ffxPath));
        Assert.IsFalse(sandbox.FileExists("version.dll"), "XTR proxy must still be removed.");
        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("f03-modffx"));
    }

    #endregion

    #region F04: Proxy & FG Bridge Health Consistency + ForzaTech version.dll Selection

    [TestMethod]
    public async Task F04_01_Health_InstalledNotYetLaunched_ReportsProxyPresentAndFgInactive()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f04_notlaunched");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f04-notlaunched", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());
        await RuntimeInstallService.InstallPlanAsync(plan);

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");

        Assert.AreEqual(RuntimeHealthState.InstalledNotYetLaunched, report.State);
        Assert.IsTrue(report.ProxyFilePresent);
        Assert.IsFalse(report.FrameGenerationActive);
    }

    [TestMethod]
    public async Task F04_02_Health_HealthyActive_ParsesStreamlineAndGeneratedFrames()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f04_healthy");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f04-healthy", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());
        await RuntimeInstallService.InstallPlanAsync(plan);

        sandbox.CreateFile("OptiScaler.log", """
            [OptiScaler] Init D3D12 Hook active
            [Streamline] slSetTag intercepted for DLSS
            [OptiFG] HookFGSwapchain attached to currentFGSwapchain
            [OptiFG] fg->Present Frametime: 8.33 ms
            """);

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");

        Assert.AreEqual(RuntimeHealthState.HealthyActive, report.State);
        Assert.IsTrue(report.ProxyFilePresent);
        Assert.IsTrue(report.ProxyHookConfirmed);
        Assert.IsTrue(report.StreamlineHookConfirmed);
        Assert.IsTrue(report.GeneratedFramesConfirmed);
        Assert.IsTrue(report.FrameGenerationActive);
        Assert.AreEqual("DX12", report.ActiveApi);
        Assert.AreEqual("DLSS", report.ActiveUpscaler);
    }

    [TestMethod]
    public async Task F04_03_Health_WhenProxyMissing_ReportsErrorAndNeverFrameGenActive()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f04_noproxy");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f04-noproxy", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());
        await RuntimeInstallService.InstallPlanAsync(plan);

        sandbox.CreateFile("OptiScaler.log", "[OptiScaler] Init D3D12\n[OptiFG] fg->Present Frametime: 8.3ms");
        File.Delete(sandbox.GetFullPath("version.dll"));

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");

        Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
        Assert.IsFalse(report.ProxyFilePresent);
        Assert.IsFalse(report.FrameGenerationActive, "FG must NEVER report active when proxy DLL is missing.");
        Assert.IsTrue(report.Summary.Contains("missing", System.StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task F04_04_Health_WhenOptiScalerIniMissing_ReportsNotOperational()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f04_noini");
        sandbox.CreateFile("ForzaHorizon6.exe", "FH6_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "f04-noini", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());
        await RuntimeInstallService.InstallPlanAsync(plan);

        sandbox.CreateFile("OptiScaler.log", "[OptiScaler] Init D3D12\n[OptiFG] fg->Present Frametime: 8.3ms");
        File.Delete(sandbox.GetFullPath("OptiScaler.ini"));

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");

        Assert.IsFalse(report.IniFilePresent,
            "Health service must detect missing OptiScaler.ini.");
        Assert.IsFalse(report.IsRuntimeOperational,
            "Health service must not report runtime operational when OptiScaler.ini is missing.");
    }

    [TestMethod]
    public async Task F04_05_ForzaTechProxySelection_VersionDllForForzaVsDxgiForStandard()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f04_proxychoice");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        var fh6Plan = await DeploymentPlanner.CreatePlanAsync(
            "fh6", "Forza Horizon 6", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
        var fh5Plan = await DeploymentPlanner.CreatePlanAsync(
            "fh5", "Forza Horizon 5", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
        var cpPlan = await DeploymentPlanner.CreatePlanAsync(
            "cp", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);

        Assert.AreEqual("version.dll", fh6Plan.SelectedProxy);
        Assert.AreEqual("version.dll", fh5Plan.SelectedProxy);
        Assert.AreEqual("dxgi.dll", cpPlan.SelectedProxy);
    }

    #endregion
}
