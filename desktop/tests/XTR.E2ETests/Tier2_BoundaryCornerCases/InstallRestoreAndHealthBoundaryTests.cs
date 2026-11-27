using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
[DoNotParallelize]
public sealed class InstallRestoreAndHealthBoundaryTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F03 Boundaries: Transactional Install & Tolerant Restore

    [TestMethod]
    public async Task B03_Restore_WithoutExistingManifest_ThrowsInvalidOperationException()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b03_nomani");
        sandbox.CreateFile("Game.exe", "EXE");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RuntimeInstallService.RestoreAsync("never-installed", sandbox.GameRoot));
    }

    [TestMethod]
    public async Task B03_Restore_WhenGameRootMoved_ThrowsAndPreservesBackups()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b03_moved");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("dxgi.dll", "STOCK_DXGI");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b03-moved", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        await RuntimeInstallService.InstallPlanAsync(plan);

        string movedRoot = Path.Combine(sandbox.SandboxRoot, "moved_game");
        Directory.CreateDirectory(movedRoot);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RuntimeInstallService.RestoreAsync("b03-moved", movedRoot));

        Assert.IsNotNull(await RuntimeInstallManifestStore.LoadAsync("b03-moved"), "Manifest must remain intact when wrong root is passed.");
    }

    [TestMethod]
    public async Task B03_Restore_WhenAllXtrAddedFilesManuallyDeleted_SucceedsAndCleansMetadata()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b03_allgone");
        sandbox.CreateFile("ForzaHorizon6.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b03-allgone", "Forza Horizon 6", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        var manifest = await RuntimeInstallService.InstallPlanAsync(plan);

        foreach (var entry in manifest.Files.Where(f => f.OriginalBackupPath is null))
        {
            var full = sandbox.GetFullPath(entry.RelativePath);
            if (File.Exists(full)) File.Delete(full);
        }

        await RuntimeInstallService.RestoreAsync("b03-allgone", sandbox.GameRoot);

        Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync("b03-allgone"));
        Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync("b03-allgone"));
    }

    [TestMethod]
    public async Task B03_Restore_CalledTwice_SecondCallRejectsCleanlyBecauseStateDeleted()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b03_twice");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b03-twice", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        await RuntimeInstallService.InstallPlanAsync(plan);
        await RuntimeInstallService.RestoreAsync("b03-twice", sandbox.GameRoot);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RuntimeInstallService.RestoreAsync("b03-twice", sandbox.GameRoot));
    }

    [TestMethod]
    public async Task B03_Restore_CorruptedStockBackup_ThrowsInvalidDataExceptionWithoutMutatingGame()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b03_badbackup");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("dxgi.dll", "STOCK_DXGI");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b03-badbackup", "Cyberpunk 2077", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
        var proxyEntry = manifest.Files.First(f => f.RelativePath == "dxgi.dll");
        string backupPath = Path.Combine(Storage.GetRuntimeBackupsPath("b03-badbackup"), proxyEntry.OriginalBackupPath!);
        await File.WriteAllTextAsync(backupPath, "CORRUPTED_BACKUP");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => RuntimeInstallService.RestoreAsync("b03-badbackup", sandbox.GameRoot));
        Assert.IsTrue(sandbox.FileExists("dxgi.dll"), "Corrupted backup abort must leave game directory unmutated.");
    }

    #endregion

    #region F04 Boundaries: Proxy & FG Bridge Health Consistency

    [TestMethod]
    public void B04_Health_NonExistentGameRoot_ReturnsErrorDetected()
    {
        var report = RuntimeHealthService.CheckHealth(
            Path.Combine(Path.GetTempPath(), $"missing_root_{Guid.NewGuid():N}"), "dxgi.dll");

        Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
        Assert.IsFalse(report.ProxyFilePresent);
        Assert.IsFalse(report.FrameGenerationActive);
    }

    [TestMethod]
    public void B04_Health_LogWithoutHookLines_ReturnsProxyBypassed()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b04_bypassed");
        sandbox.CreateFile("dxgi.dll", "PROXY");
        sandbox.CreateFile("OptiScaler.ini", "[Menu]\nOverlayMenu=true\n");
        sandbox.CreateFile("OptiScaler.log", "Starting application...\nWindow created.\nShutdown complete.\n");

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "dxgi.dll");

        Assert.AreEqual(RuntimeHealthState.ProxyBypassed, report.State);
        Assert.IsTrue(report.ProxyFilePresent);
        Assert.IsFalse(report.ProxyHookConfirmed);
    }

    [TestMethod]
    public void B04_Health_LogWithErrorLine_ReturnsErrorDetectedWithDetails()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b04_err");
        sandbox.CreateFile("version.dll", "PROXY");
        sandbox.CreateFile("OptiScaler.ini", "[FrameGen]\nEnabled=true\n");
        sandbox.CreateFile("OptiScaler.log", "[OptiScaler] Init D3D12\nError: Can't init FG Feature on swapchain\n");

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");

        Assert.AreEqual(RuntimeHealthState.ErrorDetected, report.State);
        Assert.IsTrue(report.Details.Count > 0);
    }

    [TestMethod]
    public void B04_Health_LargeLogOver200Lines_OnlyInspectsRecentTail()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b04_taillog");
        sandbox.CreateFile("version.dll", "PROXY");
        sandbox.CreateFile("OptiScaler.ini", "[FrameGen]\nEnabled=true\n");

        var lines = Enumerable.Repeat("Error: Old stale boot message", 5)
            .Concat(Enumerable.Repeat("[Trace] Normal frame tick", 220))
            .Concat(new[]
            {
                "[OptiScaler] Hook active D3D12 DLSS",
                "[OptiFG] fg->Present Frametime: 8.1 ms"
            });
        sandbox.CreateFile("OptiScaler.log", string.Join(Environment.NewLine, lines));

        var report = RuntimeHealthService.CheckHealth(sandbox.GameRoot, "version.dll");

        Assert.AreEqual(RuntimeHealthState.HealthyActive, report.State);
        Assert.IsTrue(report.GeneratedFramesConfirmed);
    }

    [TestMethod]
    public async Task B04_ForzaTechTitleVariants_CaseInsensitiveAndNoSpaces_SelectVersionDll()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b04_forzavar");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var gpu = IsolatedGameSandbox.CreateRtx4070();

        foreach (var title in new[] { "FORZA HORIZON 6", "forzahorizon6", "Forza Motorsport", "Horizon 6" })
        {
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "forza-var", title, sandbox.GameRoot, pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);
            Assert.AreEqual("version.dll", plan.SelectedProxy, $"Title '{title}' must select version.dll.");
        }
    }

    #endregion
}
