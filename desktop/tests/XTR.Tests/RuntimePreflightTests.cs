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
            Assert.AreEqual(PreflightStatus.NeedsAttention, result.Status);
            Assert.AreEqual("Needs attention", result.Headline);
            Assert.IsTrue(result.Explanation.Contains("anti-cheat", StringComparison.OrdinalIgnoreCase));
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
}
