using System;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class TransactionalInstallAndRestoreTests
{
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

            if (Directory.Exists(isolationRoot))
                Directory.Delete(isolationRoot, recursive: true);
        }
    }
}
