using System;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TransactionalInstallAndRestoreTests
{
    private static void SafeDeleteDirectory(string path)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(50);
            }
        }
    }
    private static string GetBundledPackagePath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "desktop", "src", "Runtime", "OptiScaler");
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);

            var directCandidate = Path.Combine(current, "src", "Runtime", "OptiScaler");
            if (Directory.Exists(directCandidate))
                return Path.GetFullPath(directCandidate);

            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the desktop/src/Runtime/OptiScaler folder.");
    }

    [TestMethod]
    public async Task InstallAndRestore_TransactionalLifecycle_LeavesDirectoryClean()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_isolation_{Guid.NewGuid():N}");
        var tempStorage = Path.Combine(isolationRoot, "storage");
        var tempTemp = Path.Combine(isolationRoot, "temp");
        var fakeGameRoot = Path.Combine(isolationRoot, "game");

        try
        {
            Directory.CreateDirectory(tempStorage);
            Directory.CreateDirectory(tempTemp);
            Directory.CreateDirectory(fakeGameRoot);

            // Isolate Storage paths so no real app data or user directories are affected
            Storage.StoragePathOverride = tempStorage;
            Storage.TempPathOverride = tempTemp;

            var gameId = $"transaction-test-{Guid.NewGuid():N}";
            var originalExe = Path.Combine(fakeGameRoot, "GameExecutable.exe");
            var originalDxgi = Path.Combine(fakeGameRoot, "dxgi.dll");

            await File.WriteAllTextAsync(originalExe, "ORIGINAL_GAME_BINARY");
            await File.WriteAllTextAsync(originalDxgi, "ORIGINAL_SYSTEM_DXGI_TO_BE_BACKED_UP");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            // 1. Generate Plan
            var plan = await DeploymentPlanner.CreatePlanAsync(
                gameId,
                "Transaction Test Game",
                fakeGameRoot,
                pack,
                RuntimeIntegrationPreset.LatestDlssOnly,
                "dxgi.dll");

            Assert.IsTrue(plan.CanExecute);

            // 2. Execute Plan
            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
            Assert.IsNotNull(manifest);
            Assert.AreEqual("dxgi.dll", manifest.ProxyFileName);
            Assert.IsTrue(File.Exists(Path.Combine(fakeGameRoot, "dxgi.dll")));
            Assert.IsTrue(File.Exists(Path.Combine(fakeGameRoot, "OptiScaler.ini")));

            // Ensure original dxgi was replaced with OptiScaler proxy and backup exists
            var installedDxgiContent = await File.ReadAllBytesAsync(Path.Combine(fakeGameRoot, "dxgi.dll"));
            Assert.IsTrue(installedDxgiContent.Length > 1000); // OptiScaler binary is MBs

            // 3. Restore
            await RuntimeInstallService.RestoreAsync(gameId, fakeGameRoot);

            // Verify original dxgi was restored exactly
            var restoredDxgi = await File.ReadAllTextAsync(originalDxgi);
            Assert.AreEqual("ORIGINAL_SYSTEM_DXGI_TO_BE_BACKED_UP", restoredDxgi);

            // Verify runtime files (OptiScaler.ini, etc.) were removed
            Assert.IsFalse(File.Exists(Path.Combine(fakeGameRoot, "OptiScaler.ini")));
            Assert.IsTrue(File.Exists(originalExe));
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;

            SafeDeleteDirectory(isolationRoot);
        }
    }

    [TestMethod]
    public async Task UpgradePlan_TransactionalInPlaceUpgrade_PreservesOriginalBackupAndAppliesUpdates()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_upgrade_iso_{Guid.NewGuid():N}");
        var tempStorage = Path.Combine(isolationRoot, "storage");
        var tempTemp = Path.Combine(isolationRoot, "temp");
        var fakeGameRoot = Path.Combine(isolationRoot, "game");

        try
        {
            Directory.CreateDirectory(tempStorage);
            Directory.CreateDirectory(tempTemp);
            Directory.CreateDirectory(fakeGameRoot);

            Storage.StoragePathOverride = tempStorage;
            Storage.TempPathOverride = tempTemp;

            var gameId = $"upgrade-test-{Guid.NewGuid():N}";
            var originalExe = Path.Combine(fakeGameRoot, "Game.exe");
            var originalDxgi = Path.Combine(fakeGameRoot, "dxgi.dll");

            const string originalStockDxgiContent = "ORIGINAL_STOCK_DXGI_CONTENT_DAY_ONE";
            await File.WriteAllTextAsync(originalExe, "ORIGINAL_GAME_BINARY");
            await File.WriteAllTextAsync(originalDxgi, originalStockDxgiContent);

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            // 1. Initial baseline install
            var initialPlan = await DeploymentPlanner.CreatePlanAsync(
                gameId,
                "Upgrade Test Game",
                fakeGameRoot,
                pack,
                RuntimeIntegrationPreset.LatestDlssOnly,
                "dxgi.dll");

            var initialManifest = await RuntimeInstallService.InstallPlanAsync(initialPlan);
            Assert.IsNotNull(initialManifest);
            var initialDxgiEntry = initialManifest.Files.First(f => f.RelativePath.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(initialDxgiEntry.OriginalBackupPath, "Original backup path must be recorded.");

            var backupRoot = Storage.GetRuntimeBackupsPath(gameId);
            var backupFullPath = Path.Combine(backupRoot, initialDxgiEntry.OriginalBackupPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(backupFullPath), "Original backup file must exist in storage.");
            Assert.AreEqual(originalStockDxgiContent, await File.ReadAllTextAsync(backupFullPath));

            // 2. Perform in-place upgrade to TurboBoost
            var upgradePlan = await DeploymentPlanner.CreatePlanAsync(
                gameId,
                "Upgrade Test Game",
                fakeGameRoot,
                pack,
                RuntimeIntegrationPreset.TurboBoost,
                "dxgi.dll");

            var upgradedManifest = await RuntimeInstallService.UpgradePlanAsync(upgradePlan);
            Assert.IsNotNull(upgradedManifest);
            Assert.AreEqual("0.9.5", upgradedManifest.RuntimeVersion);

            // Verify original backup is preserved and untouched
            var upgradedDxgiEntry = upgradedManifest.Files.First(f => f.RelativePath.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(initialDxgiEntry.OriginalBackupPath, upgradedDxgiEntry.OriginalBackupPath, "Original backup path must NEVER change during upgrades.");
            Assert.AreEqual(initialDxgiEntry.OriginalSha256, upgradedDxgiEntry.OriginalSha256, "Original SHA256 must NEVER change during upgrades.");
            Assert.AreEqual(originalStockDxgiContent, await File.ReadAllTextAsync(backupFullPath), "Original backup content must remain 100% untouched.");

            // 3. Restore after upgrade
            await RuntimeInstallService.RestoreAsync(gameId, fakeGameRoot);

            // Original stock file must be restored back into game folder
            var restoredDxgi = await File.ReadAllTextAsync(originalDxgi);
            Assert.AreEqual(originalStockDxgiContent, restoredDxgi, "Restoring after an upgrade must restore the original stock file.");
            Assert.IsFalse(File.Exists(Path.Combine(fakeGameRoot, "OptiScaler.ini")), "OptiScaler.ini must be removed upon restore.");
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;

            SafeDeleteDirectory(isolationRoot);
        }
    }

    [TestMethod]
    public async Task UpgradePlan_WithoutExistingInstall_ThrowsInvalidOperationException()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_upgrade_no_inst_{Guid.NewGuid():N}");
        var tempStorage = Path.Combine(isolationRoot, "storage");
        var tempTemp = Path.Combine(isolationRoot, "temp");
        var fakeGameRoot = Path.Combine(isolationRoot, "game");

        try
        {
            Directory.CreateDirectory(tempStorage);
            Directory.CreateDirectory(tempTemp);
            Directory.CreateDirectory(fakeGameRoot);

            Storage.StoragePathOverride = tempStorage;
            Storage.TempPathOverride = tempTemp;

            var gameId = $"upgrade-no-inst-{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "Game.exe"), "BINARY");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                gameId,
                "No Install Game",
                fakeGameRoot,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            bool threw = false;
            try
            {
                await RuntimeInstallService.UpgradePlanAsync(plan);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert.IsTrue(threw, "UpgradePlanAsync without existing install must throw InvalidOperationException.");
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;

            SafeDeleteDirectory(isolationRoot);
        }
    }
}
