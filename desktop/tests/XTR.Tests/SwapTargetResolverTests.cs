using System;
using System.IO;
using DLSS_Swapper;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SwapTargetResolverTests
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

    [TestMethod]
    public void SwapTargetResolver_Fh6StreamlineLayout_ResolvesInterposerDirWithNvngxFilename()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_swap_fh6_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            File.WriteAllText(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            File.WriteAllText(Path.Combine(tempDir, "sl.directsr.dll"), "DIRECTSR");
            File.WriteAllText(Path.Combine(tempDir, "sl.common.dll"), "COMMON");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_loader_dx12.dll"), "FFX_LOADER");
            File.WriteAllText(Path.Combine(tempDir, "libxess.dll"), "XESS");

            var dlssRes = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(dlssRes.CanSwap);
            Assert.AreEqual(tempDir, dlssRes.TargetDirectory);
            Assert.AreEqual("nvngx_dlss.dll", dlssRes.TargetFileName);
            Assert.AreEqual(Path.Combine(tempDir, "nvngx_dlss.dll"), dlssRes.TargetPath);
            Assert.IsFalse(dlssRes.TargetAlreadyExistsOnDisk);

            var dlssGRes = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.DlssG, tempDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(dlssGRes.CanSwap);
            Assert.AreEqual("nvngx_dlssg.dll", dlssGRes.TargetFileName);

            var dlssDRes = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.DlssD, tempDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(dlssDRes.CanSwap);
            Assert.AreEqual("nvngx_dlssd.dll", dlssDRes.TargetFileName);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void SwapTargetResolver_StreamlinePluginPresent_NeverOverwritesSlDlssDll()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_swap_sldlss_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            var binDir = Path.Combine(tempDir, "Binaries", "Win64");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "EXE");
            File.WriteAllText(Path.Combine(binDir, "sl.interposer.dll"), "SL");
            var slDlssPath = Path.Combine(binDir, "sl.dlss.dll");
            File.WriteAllText(slDlssPath, "SL_DLSS");

            var res = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, ngxModelsRootOverride: missingNgxRoot);

            Assert.IsTrue(res.CanSwap);
            Assert.AreEqual(binDir, res.TargetDirectory);
            Assert.AreEqual("nvngx_dlss.dll", res.TargetFileName);
            Assert.AreNotEqual(slDlssPath, res.TargetPath);
            Assert.IsFalse(SwapTargetResolver.IsProtectedStreamlineOrLoaderFileName(res.TargetFileName));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void SwapTargetResolver_SubdirExeAndInterposer_FollowsPriorityOrder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_swap_prio_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            var siblingDir = Path.Combine(tempDir, "Engine", "Plugins", "DLSS", "Win64");
            var slDir = Path.Combine(tempDir, "Binaries", "ThirdParty");
            var exeDir = Path.Combine(tempDir, "Binaries", "Win64");
            Directory.CreateDirectory(siblingDir);
            Directory.CreateDirectory(slDir);
            Directory.CreateDirectory(exeDir);

            // Case A: Existing sibling same-family asset dir beats sl.interposer and exe dir
            File.WriteAllText(Path.Combine(siblingDir, "nvngx_dlssg.dll"), "DLSSG");
            File.WriteAllText(Path.Combine(slDir, "sl.interposer.dll"), "SL");
            File.WriteAllText(Path.Combine(exeDir, "game.exe"), "EXE");

            var resA = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(resA.CanSwap);
            Assert.AreEqual(siblingDir, resA.TargetDirectory);

            // Case B: Remove sibling -> sl.interposer dir beats exe dir
            File.Delete(Path.Combine(siblingDir, "nvngx_dlssg.dll"));
            var resB = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(resB.CanSwap);
            Assert.AreEqual(slDir, resB.TargetDirectory);

            // Case C: Remove sl.interposer, add OptiScaler.ini in exeDir -> exeDir wins
            File.Delete(Path.Combine(slDir, "sl.interposer.dll"));
            File.WriteAllText(Path.Combine(exeDir, "OptiScaler.ini"), "[Menu]\nOverlayMenu=true\n");
            var resC = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, hasRuntimeManifest: true, runtimeInstallRoot: exeDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(resC.CanSwap);
            Assert.AreEqual(exeDir, resC.TargetDirectory);

            // Case D: No exe anywhere, runtime manifest points to root -> falls back to InstallPath
            File.Delete(Path.Combine(exeDir, "game.exe"));
            File.Delete(Path.Combine(exeDir, "OptiScaler.ini"));
            var resD = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, hasRuntimeManifest: true, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsTrue(resD.CanSwap);
            Assert.AreEqual(tempDir, resD.TargetDirectory);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void SwapTargetResolver_ExistingFileOnDiskWithZeroDbRecords_FlagsBackupRequired()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_swap_ondisk_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            File.WriteAllText(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            File.WriteAllText(Path.Combine(tempDir, "nvngx_dlss.dll"), "EXISTING_ON_DISK");

            var res = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Dlss, tempDir, knownAssetPaths: Array.Empty<string>(), ngxModelsRootOverride: missingNgxRoot);

            Assert.IsTrue(res.CanSwap);
            Assert.IsTrue(res.TargetAlreadyExistsOnDisk);
            Assert.IsNull(res.ExistingBackupPath);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void SwapTargetResolver_IncapableGameWithNoUpscalerOrRuntime_BlocksSwapCleanly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_swap_incapable_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "indiegame.exe"), "EXE");
            File.WriteAllText(Path.Combine(tempDir, "UnityPlayer.dll"), "UNITY");

            foreach (var slot in new[] { UpscalerSwapSlot.Dlss, UpscalerSwapSlot.Fsr31Dx12, UpscalerSwapSlot.XeSs })
            {
                var res = SwapTargetResolver.ResolveTargetForMissingAsset(slot, tempDir, ngxModelsRootOverride: missingNgxRoot);
                Assert.IsFalse(res.CanSwap, $"Expected slot {slot} to be blocked for incapable game.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(res.Reason));
            }

            // Cross-family guardrail: game has only libxess.dll (no Streamline/DLSS/OptiScaler)
            File.WriteAllText(Path.Combine(tempDir, "libxess.dll"), "XESS");
            var dlssRes = SwapTargetResolver.ResolveTargetForMissingAsset(UpscalerSwapSlot.Dlss, tempDir, ngxModelsRootOverride: missingNgxRoot);
            var xessRes = SwapTargetResolver.ResolveTargetForMissingAsset(UpscalerSwapSlot.XeSs, tempDir, ngxModelsRootOverride: missingNgxRoot);
            Assert.IsFalse(dlssRes.CanSwap);
            Assert.IsTrue(xessRes.CanSwap);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void SwapTargetResolver_FidelityFxSdk2Game_ResolvesFsr31Dx12WithoutOverwritingLoader()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_swap_ffx2_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_loader_dx12.dll"), "LOADER");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_upscaler_dx12.dll"), "UPSCALER");

            var res = SwapTargetResolver.ResolveTargetForMissingAsset(
                UpscalerSwapSlot.Fsr31Dx12, tempDir, ngxModelsRootOverride: missingNgxRoot);

            Assert.IsTrue(res.CanSwap);
            Assert.AreEqual("amd_fidelityfx_dx12.dll", res.TargetFileName);
            Assert.AreNotEqual("amd_fidelityfx_loader_dx12.dll", res.TargetFileName);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void DeployedDllMarkerStore_TracksOriginallyAbsentAndClearsOnReset()
    {
        var isolationRoot = Path.Combine(Path.GetTempPath(), $"xtr_marker_iso_{Guid.NewGuid():N}");
        var tempStorage = Path.Combine(isolationRoot, "storage");
        var tempTemp = Path.Combine(isolationRoot, "temp");
        var fakeGameDir = Path.Combine(isolationRoot, "game");
        try
        {
            Directory.CreateDirectory(tempStorage);
            Directory.CreateDirectory(tempTemp);
            Directory.CreateDirectory(fakeGameDir);
            Storage.StoragePathOverride = tempStorage;
            Storage.TempPathOverride = tempTemp;

            const string gameId = "steam_2483190";
            var dlssPath = Path.Combine(fakeGameDir, "nvngx_dlss.dll");

            Assert.IsFalse(DeployedDllMarkerStore.IsOriginallyAbsent(gameId, dlssPath));
            DeployedDllMarkerStore.MarkOriginallyAbsent(gameId, dlssPath);
            Assert.IsTrue(DeployedDllMarkerStore.IsOriginallyAbsent(gameId, dlssPath));
            Assert.IsTrue(DeployedDllMarkerStore.IsOriginallyAbsent(gameId, dlssPath.ToUpperInvariant()));

            DeployedDllMarkerStore.RemoveMarker(gameId, dlssPath);
            Assert.IsFalse(DeployedDllMarkerStore.IsOriginallyAbsent(gameId, dlssPath));
        }
        finally
        {
            Storage.StoragePathOverride = null;
            Storage.TempPathOverride = null;
            SafeDeleteDirectory(isolationRoot);
        }
    }
}
