using System;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class CompatibilityDatabaseTests
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
    public void LookupGame_ApexLegends_ReturnsMultiplayerBlocked()
    {
        var record = CompatibilityDatabase.LookupGame("Apex Legends");
        Assert.IsNotNull(record);
        Assert.AreEqual(CompatibilityLevel.MultiplayerBlocked, record.Level);
        Assert.IsTrue(record.Notes.Contains("Easy Anti-Cheat", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LookupGame_Valorant_ReturnsMultiplayerBlocked()
    {
        var record = CompatibilityDatabase.LookupGame("VALORANT");
        Assert.IsNotNull(record);
        Assert.AreEqual(CompatibilityLevel.MultiplayerBlocked, record.Level);
        Assert.IsTrue(record.Notes.Contains("Vanguard", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LookupGame_Cyberpunk2077_ReturnsVerifiedCompatibleWithFrameGen()
    {
        var record = CompatibilityDatabase.LookupGame("Cyberpunk 2077");
        Assert.IsNotNull(record);
        Assert.AreEqual(CompatibilityLevel.VerifiedCompatible, record.Level);
        Assert.IsTrue(record.SupportsNativeFrameGen);
        Assert.AreEqual("dxgi.dll", record.RecommendedProxy);
        Assert.AreEqual("DX12", record.RecommendedApi);
    }

    [TestMethod]
    public async Task Preflight_MultiplayerGame_BlocksInstallationAndSetsSafetyWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_mp_preflight_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "r5apex.exe"), "EXE_CONTENT");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var preflight = await RuntimePreflightService.RunPreflightAsync(
                "apex-test-id",
                tempDir,
                pack,
                "dxgi.dll",
                gameTitle: "Apex Legends");

            Assert.IsFalse(preflight.CanProceed, "Installation must be blocked for multiplayer games with anti-cheat.");
            Assert.AreEqual(PreflightStatus.Blocked, preflight.Status);
            Assert.IsTrue(preflight.HasAntiCheatRisk);
            Assert.IsTrue(preflight.Headline.Contains("Safety risk", StringComparison.OrdinalIgnoreCase) ||
                          preflight.Headline.Contains("Multiplayer", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void LookupGame_ForzaHorizon6_ReturnsVerifiedCompatibleWithVersionDll()
    {
        var record = CompatibilityDatabase.LookupGame("Forza Horizon 6");
        Assert.IsNotNull(record);
        Assert.AreEqual(CompatibilityLevel.VerifiedCompatible, record.Level);
        Assert.IsTrue(record.SupportsNativeFrameGen);
        Assert.AreEqual("version.dll", record.RecommendedProxy, "Forza Horizon 6 requires version.dll proxy to prevent crashes.");
        Assert.AreEqual("DX12", record.RecommendedApi);
    }

    [TestMethod]
    public void LookupGame_ForzaHorizon5_ReturnsVerifiedCompatibleWithVersionDll()
    {
        var record = CompatibilityDatabase.LookupGame("Forza Horizon 5");
        Assert.IsNotNull(record);
        Assert.AreEqual(CompatibilityLevel.VerifiedCompatible, record.Level);
        Assert.IsTrue(record.SupportsNativeFrameGen);
        Assert.AreEqual("version.dll", record.RecommendedProxy, "Forza Horizon 5 requires version.dll proxy.");
        Assert.AreEqual("DX12", record.RecommendedApi);
    }
}