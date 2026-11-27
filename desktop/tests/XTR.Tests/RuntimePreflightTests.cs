using System;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class RuntimePreflightTests
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
    public async Task Preflight_NonExistentFolder_ReturnsBlocked()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
        Assert.IsNotNull(pack);

        var result = await RuntimePreflightService.RunPreflightAsync("test-game", @"C:\non_existent_folder_xyz_123", pack);

        Assert.IsFalse(result.CanProceed);
        Assert.AreEqual(PreflightStatus.Blocked, result.Status);
        Assert.AreEqual("Game folder not found", result.Headline);
    }

    [TestMethod]
    public async Task Preflight_CleanGameDirectory_ReturnsReady()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_game_clean_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "fake executable binary");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var result = await RuntimePreflightService.RunPreflightAsync("test-clean-game", tempDir, pack);

            Assert.IsTrue(result.CanProceed);
            Assert.AreEqual(PreflightStatus.Ready, result.Status);
            Assert.AreEqual("Ready", result.Headline);
            Assert.AreEqual(Path.Combine(tempDir, "game.exe"), result.ExecutablePath);
            Assert.IsFalse(result.HasAntiCheatRisk);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Preflight_AntiCheatBinary_ReturnsNeedsAttention()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_game_anticheat_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "fake executable binary");
            File.WriteAllText(Path.Combine(tempDir, "EasyAntiCheat.exe"), "fake EAC binary");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var result = await RuntimePreflightService.RunPreflightAsync("test-ac-game", tempDir, pack);

            Assert.IsTrue(result.HasAntiCheatRisk);
            Assert.AreEqual(PreflightStatus.Blocked, result.Status);
            Assert.IsFalse(result.CanProceed);
            Assert.AreEqual("Anti-cheat detected", result.Headline);
            Assert.IsTrue(result.Explanation.Contains("anti-cheat", StringComparison.OrdinalIgnoreCase));

            // With offline override active, user can proceed under caution (SEC-06)
            var overrideResult = await RuntimePreflightService.RunPreflightAsync("test-ac-game", tempDir, pack, allowOfflineAntiCheatOverride: true);
            Assert.IsTrue(overrideResult.HasAntiCheatRisk);
            Assert.AreEqual(PreflightStatus.NeedsAttention, overrideResult.Status);
            Assert.IsTrue(overrideResult.CanProceed);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Preflight_UnownedProxyCollision_ReportsConflict()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_game_proxy_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "game.exe"), "fake executable binary");
            File.WriteAllText(Path.Combine(tempDir, "dxgi.dll"), "existing unowned proxy");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var result = await RuntimePreflightService.RunPreflightAsync("test-proxy-game", tempDir, pack, proxyName: "dxgi.dll");

            Assert.AreEqual(1, result.ProxyConflicts.Count);
            Assert.IsTrue(result.ProxyConflicts[0].Contains("dxgi.dll"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task RuntimePreflight_Fh6StreamlineLayout_ReportsStreamlineDlssAndNoMissingWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_pf_fh6_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_pf_ngx_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.directsr.dll"), "DIRECTSR");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.common.dll"), "COMMON");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "amd_fidelityfx_loader_dx12.dll"), "FFX_LOADER");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "libxess.dll"), "XESS");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var result = await RuntimePreflightService.RunPreflightAsync(
                "fh6-pf-test",
                tempDir,
                pack,
                "version.dll",
                "Forza Horizon 6",
                allowOfflineAntiCheatOverride: false,
                ngxModelsRootOverride: missingNgxRoot);

            Assert.IsTrue(result.StreamlineDetected);
            Assert.IsTrue(result.StreamlineDlssDetected);
            Assert.IsFalse(result.NativeDlssDetected);
            Assert.IsTrue(result.FidelityFxSdk2Detected);
            Assert.IsTrue(result.XeSsDetected);
            Assert.AreEqual("DX12", result.DetectedApi);
            Assert.IsFalse(System.Linq.Enumerable.Any(
                result.Checks,
                c => c.Severity == CheckItemSeverity.Warning &&
                     c.Message.Contains("No nvngx_dlss.dll found", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task RuntimePreflight_SubdirectoryNgxDlss_DetectsNativeDlss()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_pf_subdir_{Guid.NewGuid():N}");
        var missingNgxRoot = Path.Combine(Path.GetTempPath(), $"xtr_pf_ngx_{Guid.NewGuid():N}");
        try
        {
            var subDir = Path.Combine(tempDir, "Binaries", "Win64");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            var dlssPath = Path.Combine(subDir, "NVNGX_DLSS.DLL");
            await File.WriteAllTextAsync(dlssPath, "DLSS");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var result = await RuntimePreflightService.RunPreflightAsync(
                "subdir-pf-test",
                tempDir,
                pack,
                "dxgi.dll",
                "Subdir Game",
                allowOfflineAntiCheatOverride: false,
                ngxModelsRootOverride: missingNgxRoot);

            Assert.IsTrue(result.NativeDlssDetected);
            Assert.AreEqual(dlssPath, result.PrimaryDlssPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
