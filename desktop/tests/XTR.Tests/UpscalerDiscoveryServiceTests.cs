using System;
using System.IO;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class UpscalerDiscoveryServiceTests
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
    public void Discover_Fh6SteamLayout_WithNgxOtaStore_DetectsStreamlineDirectSrAndOtaVersions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_fh6_{Guid.NewGuid():N}");
        var ngxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_fh6_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            File.WriteAllText(Path.Combine(tempDir, "sl.interposer.dll"), "SL_INTERPOSER");
            File.WriteAllText(Path.Combine(tempDir, "sl.directsr.dll"), "SL_DIRECTSR");
            File.WriteAllText(Path.Combine(tempDir, "sl.common.dll"), "SL_COMMON");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_loader_dx12.dll"), "FFX_LOADER");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_upscaler_dx12.dll"), "FFX_UPSCALER");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_framegeneration_dx12.dll"), "FFX_FG");
            File.WriteAllText(Path.Combine(tempDir, "amd_fidelityfx_dx12.dll"), "FFX_31");
            File.WriteAllText(Path.Combine(tempDir, "libxess.dll"), "XESS");
            File.WriteAllText(Path.Combine(tempDir, "libxell.dll"), "XELL");
            File.WriteAllText(Path.Combine(tempDir, "libxess_fg.dll"), "XESS_FG");

            var dlssModelDir = Path.Combine(ngxRoot, "dlss", "versions", "20318480", "files");
            var dlssgModelDir = Path.Combine(ngxRoot, "dlssg", "versions", "20318480", "files");
            Directory.CreateDirectory(dlssModelDir);
            Directory.CreateDirectory(dlssgModelDir);
            File.WriteAllText(Path.Combine(dlssModelDir, "160_E658700.bin"), "NGX_DLSS");
            File.WriteAllText(Path.Combine(dlssgModelDir, "160_E658700.bin"), "NGX_DLSSG");

            var result = UpscalerDiscoveryService.Discover(tempDir, ngxRoot);

            Assert.IsTrue(result.StreamlineStackDetected);
            Assert.IsTrue(result.HasStreamlineNgxOtaDlss);
            Assert.IsTrue(result.HasStreamlineNgxOtaDlssG);
            Assert.IsTrue(result.HasAnyDlssCapability);
            Assert.IsFalse(result.HasLocalNgxDlss);
            Assert.IsTrue(result.NgxOta.StoreExists);
            Assert.IsTrue(result.NgxOta.HasDlssModel);
            Assert.IsTrue(result.NgxOta.HasDlssGModel);
            Assert.AreEqual(3, result.FidelityFxSdk2Files.Count);
            Assert.AreEqual(1, result.Fsr31Dx12.Count);
            Assert.AreEqual(3, result.XeSSFiles.Count);
            Assert.AreEqual("DX12", result.DetectedGraphicsApi);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
            SafeDeleteDirectory(ngxRoot);
        }
    }

    [TestMethod]
    public void Discover_Fh6SteamLayout_WithoutNgxStore_StillDetectsStreamlineDirectSrCapability()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_fh6_nongx_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            File.WriteAllText(Path.Combine(tempDir, "sl.interposer.dll"), "SL_INTERPOSER");
            File.WriteAllText(Path.Combine(tempDir, "sl.directsr.dll"), "SL_DIRECTSR");
            File.WriteAllText(Path.Combine(tempDir, "sl.common.dll"), "SL_COMMON");

            var result = UpscalerDiscoveryService.Discover(tempDir, missingNgxRoot);

            Assert.IsTrue(result.HasStreamlineNgxOtaDlss);
            Assert.IsTrue(result.HasAnyDlssCapability);
            Assert.IsFalse(result.NgxOta.StoreExists);
            Assert.IsNull(result.NgxOta.DlssVersion);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void Discover_SubdirectoryAndMixedCaseDlls_DetectsAllRecursively()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_subdir_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            var binDir = Path.Combine(tempDir, "Binaries", "Win64");
            var pluginDir = Path.Combine(tempDir, "Engine", "Plugins");
            var subDir = Path.Combine(tempDir, "Sub");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(pluginDir);
            Directory.CreateDirectory(subDir);

            File.WriteAllText(Path.Combine(binDir, "NVNGX_DLSS.DLL"), "DLSS");
            File.WriteAllText(Path.Combine(pluginDir, "SL.INTERPOSER.DLL"), "SL");
            File.WriteAllText(Path.Combine(pluginDir, "AMD_FIDELITYFX_LOADER_DX12.DLL"), "FFX");
            File.WriteAllText(Path.Combine(subDir, "LIBXESS_DX11.DLL"), "XESS11");

            var result = UpscalerDiscoveryService.Discover(tempDir, missingNgxRoot);

            Assert.AreEqual(4, result.Files.Count);
            Assert.IsTrue(result.HasLocalNgxDlss);
            Assert.IsFalse(result.LocalNgxDlss[0].IsRootLevel);
            Assert.IsTrue(result.LocalNgxDlss[0].RelativePath.EndsWith("NVNGX_DLSS.DLL", StringComparison.Ordinal));
            Assert.AreEqual(1, result.StreamlineInterposer.Count);
            Assert.AreEqual(1, result.FidelityFxSdk2Files.Count);
            Assert.AreEqual(1, result.XeSSFiles.Count);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void Discover_StreamlineDlssPlugin_NeverConflatedWithLocalNgxDlss()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_sldlss_{Guid.NewGuid():N}");
        var emptyNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_empty_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(emptyNgxRoot);
            File.WriteAllText(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            File.WriteAllText(Path.Combine(tempDir, "sl.dlss.dll"), "SL_DLSS_2_14");

            var result = UpscalerDiscoveryService.Discover(tempDir, emptyNgxRoot);

            Assert.AreEqual(1, result.StreamlineDlssPlugin.Count);
            Assert.AreEqual(0, result.LocalNgxDlss.Count);
            Assert.IsFalse(result.HasLocalNgxDlss);
            Assert.IsTrue(result.HasStreamlineNgxOtaDlss);
            Assert.IsNull(result.EffectiveNgxDlssVersion);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
            SafeDeleteDirectory(emptyNgxRoot);
        }
    }

    [TestMethod]
    public void Discover_ReflexOnlyStreamline_DoesNotFalselyReportDlss()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_reflex_{Guid.NewGuid():N}");
        var ngxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_reflex_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            File.WriteAllText(Path.Combine(tempDir, "sl.reflex.dll"), "SL_REFLEX");

            var dlssModelDir = Path.Combine(ngxRoot, "dlss", "versions", "20318480", "files");
            Directory.CreateDirectory(dlssModelDir);
            File.WriteAllText(Path.Combine(dlssModelDir, "160_E658700.bin"), "NGX_DLSS");

            var result = UpscalerDiscoveryService.Discover(tempDir, ngxRoot);

            Assert.IsTrue(result.StreamlineStackDetected);
            Assert.IsFalse(result.HasStreamlineNgxOtaDlss);
            Assert.IsFalse(result.HasAnyDlssCapability);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
            SafeDeleteDirectory(ngxRoot);
        }
    }

    [TestMethod]
    public void Discover_IgnoresDlsssBackupsAndReparsePoints()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_dlsss_{Guid.NewGuid():N}");
        var externalTarget = Path.Combine(Path.GetTempPath(), $"xtr_disc_symlink_target_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(externalTarget);
            File.WriteAllText(Path.Combine(tempDir, "nvngx_dlss.dll.dlsss"), "BACKUP_FILE");
            File.WriteAllText(Path.Combine(externalTarget, "nvngx_dlss.dll"), "SYMLINKED_DLSS");

            var linkPath = Path.Combine(tempDir, "LinkedMod");
            try
            {
                Directory.CreateSymbolicLink(linkPath, externalTarget);
            }
            catch
            {
                // Symlink creation may require Developer Mode/elevation on some Windows hosts.
            }

            var result = UpscalerDiscoveryService.Discover(tempDir, missingNgxRoot);

            Assert.AreEqual(0, result.Files.Count);
            Assert.IsFalse(result.HasLocalNgxDlss);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
            SafeDeleteDirectory(externalTarget);
        }
    }

    [TestMethod]
    public void HasUncachedUpscalerFiles_DetectsStaleSixRowCacheAndCleanCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_cache_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ffxDx12 = Path.Combine(tempDir, "amd_fidelityfx_dx12.dll");
            var ffxVk = Path.Combine(tempDir, "amd_fidelityfx_vk.dll");
            var xell = Path.Combine(tempDir, "libxell.dll");
            var xess = Path.Combine(tempDir, "libxess.dll");
            var xess11 = Path.Combine(tempDir, "libxess_dx11.dll");
            var xessFg = Path.Combine(tempDir, "libxess_fg.dll");
            var slInterposer = Path.Combine(tempDir, "sl.interposer.dll");
            var slDirectSr = Path.Combine(tempDir, "sl.directsr.dll");
            var ffxLoader = Path.Combine(tempDir, "amd_fidelityfx_loader_dx12.dll");

            foreach (var p in new[] { ffxDx12, ffxVk, xell, xess, xess11, xessFg, slInterposer, slDirectSr, ffxLoader })
            {
                File.WriteAllText(p, "DLL");
            }

            var staleSixRows = new[] { ffxDx12, ffxVk, xell, xess, xess11, xessFg };
            Assert.IsTrue(UpscalerDiscoveryService.HasUncachedUpscalerFiles(tempDir, staleSixRows));

            var fullRows = new[] { ffxDx12, ffxVk, xell, xess, xess11, xessFg, slInterposer, slDirectSr, ffxLoader };
            Assert.IsFalse(UpscalerDiscoveryService.HasUncachedUpscalerFiles(tempDir, fullRows));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void ResolveDlssDeployTargetDirectory_FollowsPriorityOrder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_disc_target_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_ngx_missing_{Guid.NewGuid():N}");
        try
        {
            var exeDir = Path.Combine(tempDir, "Binaries");
            var slDir = Path.Combine(tempDir, "Streamline");
            var dlssDir = Path.Combine(tempDir, "Plugins");
            Directory.CreateDirectory(exeDir);
            Directory.CreateDirectory(slDir);
            Directory.CreateDirectory(dlssDir);

            var exePath = Path.Combine(exeDir, "game.exe");
            File.WriteAllText(exePath, "EXE");

            // Case 1: primary exe dir when no Streamline or DLSS
            var d1 = UpscalerDiscoveryService.Discover(tempDir, missingNgxRoot);
            Assert.AreEqual(exeDir, UpscalerDiscoveryService.ResolveDlssDeployTargetDirectory(tempDir, d1, exePath));

            // Case 2: sl.interposer.dll dir overrides exe dir
            File.WriteAllText(Path.Combine(slDir, "sl.interposer.dll"), "SL");
            var d2 = UpscalerDiscoveryService.Discover(tempDir, missingNgxRoot);
            Assert.AreEqual(slDir, UpscalerDiscoveryService.ResolveDlssDeployTargetDirectory(tempDir, d2, exePath));

            // Case 3: existing nvngx_dlss.dll dir overrides Streamline dir
            File.WriteAllText(Path.Combine(dlssDir, "nvngx_dlss.dll"), "DLSS");
            var d3 = UpscalerDiscoveryService.Discover(tempDir, missingNgxRoot);
            Assert.AreEqual(dlssDir, UpscalerDiscoveryService.ResolveDlssDeployTargetDirectory(tempDir, d3, exePath));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [TestMethod]
    public void Discover_LiveFh6Directory_ReadOnlySmokeCheck()
    {
        const string fh6Path = @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon6";
        if (!Directory.Exists(fh6Path))
        {
            return;
        }

        var result = UpscalerDiscoveryService.Discover(fh6Path);

        Assert.IsTrue(result.StreamlineInterposer.Count > 0, "Expected sl.interposer.dll in live FH6 folder.");
        Assert.IsTrue(result.StreamlineDirectSr.Count > 0, "Expected sl.directsr.dll in live FH6 folder.");
        Assert.IsTrue(result.FidelityFxSdk2Files.Count > 0, "Expected FidelityFX SDK 2 DLLs in live FH6 folder.");
        Assert.IsTrue(result.XeSSFiles.Count > 0, "Expected XeSS DLLs in live FH6 folder.");
        Assert.IsTrue(result.HasAnyDlssCapability, "Expected DLSS capability via Streamline/DirectSR in live FH6 folder.");
        Assert.IsFalse(result.HasLocalNgxDlss, "FH6 ships without local nvngx_dlss.dll.");
    }
}
