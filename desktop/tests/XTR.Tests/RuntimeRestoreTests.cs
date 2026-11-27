using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DLSS_Swapper;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
[DoNotParallelize]
public sealed class RuntimeRestoreTests
{
    private static void SafeDeleteDirectory(string path)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch
            {
                System.Threading.Thread.Sleep(40);
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

    private static string ComputeSha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));

    [TestMethod]
    public async Task Restore_MissingProxyAndIni_WithOriginalBackups_RecoversCleanly()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_restore_missing_{Guid.NewGuid():N}");
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

            var gameId = $"restore-missing-{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "OptiScaler.ini"), "ORIGINAL_INI_BEFORE_XTR");
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "libxess.dll"), "ORIGINAL_XESS_BEFORE_XTR");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                gameId, "Missing Files Recovery Game", fakeGameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, "dxgi.dll");
            Assert.IsTrue(plan.CanExecute);

            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
            Assert.IsNotNull(manifest);
            var backupRoot = Storage.GetRuntimeBackupsPath(gameId);

            // Simulate user/game deleting proxy and OptiScaler.ini before clicking Restore
            File.Delete(Path.Combine(fakeGameRoot, "dxgi.dll"));
            File.Delete(Path.Combine(fakeGameRoot, "OptiScaler.ini"));

            await RuntimeInstallService.RestoreAsync(gameId, fakeGameRoot);

            Assert.IsTrue(File.Exists(Path.Combine(fakeGameRoot, "OptiScaler.ini")));
            Assert.AreEqual("ORIGINAL_INI_BEFORE_XTR", await File.ReadAllTextAsync(Path.Combine(fakeGameRoot, "OptiScaler.ini")));
            Assert.AreEqual("ORIGINAL_XESS_BEFORE_XTR", await File.ReadAllTextAsync(Path.Combine(fakeGameRoot, "libxess.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(fakeGameRoot, "dxgi.dll")));

            Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync(gameId));
            Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync(gameId));
            Assert.IsFalse(Directory.Exists(backupRoot));
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;
            SafeDeleteDirectory(isolationRoot);
        }
    }

    [TestMethod]
    public async Task Restore_Fh6ExternalVendorUpdates_PreservesVendorDllsAndCleansXtrFiles()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_restore_fh6_{Guid.NewGuid():N}");
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

            var gameId = $"steam_2483190_{Guid.NewGuid():N}";
            var ffxPath = Path.Combine(fakeGameRoot, "amd_fidelityfx_dx12.dll");
            var d3d12OptiDir = Path.Combine(fakeGameRoot, "D3D12_Optiscaler");
            var d3d12CorePath = Path.Combine(d3d12OptiDir, "D3D12Core.dll");
            var logPath = Path.Combine(fakeGameRoot, "OptiScaler.log");

            Directory.CreateDirectory(d3d12OptiDir);
            var updatedVendorBytes = "FH6_AMD_UPDATED_FIDELITYFX_DX12_V2_3_0_4555"u8.ToArray();
            var d3d12CoreBytes = "XTR_D3D12_CORE_BYTES"u8.ToArray();
            var originalIniBytes = "FH6_ORIGINAL_INI"u8.ToArray();
            var oldXtrFfxBytes = "OLD_XTR_FIDELITYFX_DX12"u8.ToArray();
            var oldXtrIniBytes = "OLD_XTR_INI"u8.ToArray();
            var oldXtrDxgiBytes = "OLD_XTR_DXGI"u8.ToArray();

            await File.WriteAllBytesAsync(ffxPath, updatedVendorBytes);
            await File.WriteAllBytesAsync(d3d12CorePath, d3d12CoreBytes);
            await File.WriteAllTextAsync(logPath, "[info] OptiScaler log line\n");

            var backupRoot = Storage.GetRuntimeBackupsPath(gameId);
            var sessionId = "f793f322cd1044b2aec76c4288c2cc64";
            var backupSessionDir = Path.Combine(backupRoot, sessionId);
            Directory.CreateDirectory(backupSessionDir);
            await File.WriteAllBytesAsync(Path.Combine(backupSessionDir, "OptiScaler.ini"), originalIniBytes);

            var manifest = new RuntimeInstallManifest
            {
                GameId = gameId,
                RuntimeId = "optiscaler-0.9.4-xtr",
                RuntimeVersion = "0.9.4",
                ProxyFileName = "dxgi.dll",
                InstallRoot = fakeGameRoot,
                Files =
                {
                    new RuntimeOwnedFile
                    {
                        RelativePath = "amd_fidelityfx_dx12.dll",
                        InstalledSha256 = ComputeSha256Hex(oldXtrFfxBytes),
                        OriginalBackupPath = null
                    },
                    new RuntimeOwnedFile
                    {
                        RelativePath = "dxgi.dll",
                        InstalledSha256 = ComputeSha256Hex(oldXtrDxgiBytes),
                        OriginalBackupPath = null
                    },
                    new RuntimeOwnedFile
                    {
                        RelativePath = "OptiScaler.ini",
                        InstalledSha256 = ComputeSha256Hex(oldXtrIniBytes),
                        OriginalBackupPath = $"{sessionId}/OptiScaler.ini",
                        OriginalSha256 = ComputeSha256Hex(originalIniBytes)
                    },
                    new RuntimeOwnedFile
                    {
                        RelativePath = "D3D12_Optiscaler/D3D12Core.dll",
                        InstalledSha256 = ComputeSha256Hex(d3d12CoreBytes),
                        OriginalBackupPath = null
                    }
                }
            };

            await RuntimeInstallManifestStore.SaveAsync(manifest);
            await RuntimeIntegrationProfileStore.SaveAsync(new RuntimeIntegrationProfile
            {
                GameId = gameId,
                RuntimeId = manifest.RuntimeId,
                RuntimeVersion = manifest.RuntimeVersion,
                ProxyFileName = "dxgi.dll",
                SupportState = "supported"
            });

            await RuntimeInstallService.RestoreAsync(gameId, fakeGameRoot);

            // External vendor DLL must be preserved intact
            Assert.IsTrue(File.Exists(ffxPath));
            CollectionAssert.AreEqual(updatedVendorBytes, await File.ReadAllBytesAsync(ffxPath));

            // Backed-up OptiScaler.ini must be restored via File.Copy even though it was missing on disk
            Assert.IsTrue(File.Exists(Path.Combine(fakeGameRoot, "OptiScaler.ini")));
            CollectionAssert.AreEqual(originalIniBytes, await File.ReadAllBytesAsync(Path.Combine(fakeGameRoot, "OptiScaler.ini")));

            // XTR files, logs, empty directories, manifest, profile, and backups must be removed
            Assert.IsFalse(File.Exists(d3d12CorePath));
            Assert.IsFalse(Directory.Exists(d3d12OptiDir));
            Assert.IsFalse(File.Exists(logPath));
            Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync(gameId));
            Assert.IsNull(await RuntimeIntegrationProfileStore.LoadAsync(gameId));
            Assert.IsFalse(Directory.Exists(backupRoot));
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;
            SafeDeleteDirectory(isolationRoot);
        }
    }

    [TestMethod]
    public void Restore_ExternallyModifiedProxy_PreservesNonOptiScalerProxy_DeletesOptiScalerProxy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_restore_pe_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var bundledOptiScaler = Path.Combine(GetBundledPackagePath(), "OptiScaler.dll");
            var renamedOptiScalerPath = Path.Combine(tempDir, "version.dll");
            File.Copy(bundledOptiScaler, renamedOptiScalerPath, overwrite: true);

            var externalDxgiPath = Path.Combine(tempDir, "dxgi.dll");
            File.WriteAllText(externalDxgiPath, "NON_OPTISCALER_CUSTOM_PROXY");

            var optiEntry = new RuntimeOwnedFile
            {
                RelativePath = "version.dll",
                InstalledSha256 = new string('0', 64), // Hash mismatch simulating modified/upgraded OptiScaler
                OriginalBackupPath = null
            };

            var optiPlan = RuntimeRestorePlanner.PlanEntry(
                optiEntry, renamedOptiScalerPath, installedExists: true, installedSha256: new string('1', 64), backupPath: null, backupExists: false);
            Assert.AreEqual(RestoreFileActionKind.DeleteXtrFile, optiPlan.Action);

            var nonOptiEntry = new RuntimeOwnedFile
            {
                RelativePath = "dxgi.dll",
                InstalledSha256 = new string('0', 64),
                OriginalBackupPath = null
            };
            var nonOptiPlan = RuntimeRestorePlanner.PlanEntry(
                nonOptiEntry, externalDxgiPath, installedExists: true, installedSha256: new string('2', 64), backupPath: null, backupExists: false);
            Assert.AreEqual(RestoreFileActionKind.PreserveExternalFile, nonOptiPlan.Action);

            Assert.IsTrue(RuntimeRestorePlanner.IsXtrExclusiveFile("OptiScaler.ini", null));
            Assert.IsTrue(RuntimeRestorePlanner.IsXtrExclusiveFile("xtr-runtime.json", null));
            Assert.IsTrue(RuntimeRestorePlanner.IsXtrExclusiveFile(@"D3D12_Optiscaler\D3D12Core.dll", null));
            Assert.IsTrue(RuntimeRestorePlanner.IsXtrExclusiveFile("plugins/xtr_hud.asi", null));
            Assert.IsTrue(RuntimeRestorePlanner.IsXtrExclusiveFile("version.dll", renamedOptiScalerPath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public async Task Restore_ExternallyModifiedFileWithBackup_RestoresOriginalBackup()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_restore_modbak_{Guid.NewGuid():N}");
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

            var gameId = $"restore-modbak-{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "libxess.dll"), "STOCK_ORIGINAL_XESS");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                gameId, "Modified With Backup Game", fakeGameRoot, pack, RuntimeIntegrationPreset.LatestDlssOnly, "dxgi.dll");
            await RuntimeInstallService.InstallPlanAsync(plan);

            // Simulate user swapping libxess.dll externally after runtime install
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "libxess.dll"), "USER_UPDATED_XESS");

            await RuntimeInstallService.RestoreAsync(gameId, fakeGameRoot);

            Assert.AreEqual("STOCK_ORIGINAL_XESS", await File.ReadAllTextAsync(Path.Combine(fakeGameRoot, "libxess.dll")));
            Assert.IsNull(await RuntimeInstallManifestStore.LoadAsync(gameId));
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;
            SafeDeleteDirectory(isolationRoot);
        }
    }

    [TestMethod]
    public async Task Restore_CorruptedBackupFile_ThrowsInvalidDataException()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_restore_corrupt_{Guid.NewGuid():N}");
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

            var gameId = $"restore-corrupt-{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(fakeGameRoot, "dxgi.dll"), "STOCK_DXGI");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                gameId, "Corrupt Backup Game", fakeGameRoot, pack, RuntimeIntegrationPreset.LatestDlssOnly, "dxgi.dll");
            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);

            var dxgiEntry = manifest.Files.First(f => f.RelativePath.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase));
            var backupFile = Path.Combine(Storage.GetRuntimeBackupsPath(gameId), dxgiEntry.OriginalBackupPath!.Replace('/', Path.DirectorySeparatorChar));
            await File.WriteAllTextAsync(backupFile, "CORRUPTED_BACKUP_DATA");

            bool threw = false;
            try
            {
                await RuntimeInstallService.RestoreAsync(gameId, fakeGameRoot);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Expected InvalidDataException for damaged backup file.");
            Assert.IsNotNull(await RuntimeInstallManifestStore.LoadAsync(gameId));
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;
            SafeDeleteDirectory(isolationRoot);
        }
    }
}
