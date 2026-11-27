using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public class DeploymentPlannerCustomizationTests
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
    public async Task CreatePlan_AppliesCustomDevAndFgHotkeyFromOverrides()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            DeploymentPlanner.OverrideFrameGenHotkey = "0x7B";
            DeploymentPlanner.OverrideDevHotkey = "0x7A";

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "custom-hotkey-test",
                "Custom Hotkey Game",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            var fgKeyEntry = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "FGShortcutKey");
            Assert.IsNotNull(fgKeyEntry);
            Assert.AreEqual("0x7B", fgKeyEntry.Value);

            var devKeyEntry = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "ShortcutKey");
            Assert.IsNotNull(devKeyEntry);
            Assert.AreEqual("0x7A", devKeyEntry.Value);
        }
        finally
        {
            DeploymentPlanner.OverrideFrameGenHotkey = null;
            DeploymentPlanner.OverrideDevHotkey = null;
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task CreatePlan_AppliesCyberpunkThemeColors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            DeploymentPlanner.OverrideTheme = "Cyberpunk Neon";

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "cyberpunk-theme-test",
                "Cyberpunk Theme Game",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            var accentR = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "AccentColorR");
            Assert.IsNotNull(accentR);
            Assert.AreEqual("0.00", accentR.Value);

            var accentB = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "AccentColorB");
            Assert.IsNotNull(accentB);
            Assert.AreEqual("1.00", accentB.Value);
        }
        finally
        {
            DeploymentPlanner.OverrideTheme = null;
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task CreatePlan_AlwaysWritesAppleDarkGlassBackgroundParameters()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "apple-glass-bg-test",
                "Apple Glass Game",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            var bgR = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "BGColorR");
            var bgG = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "BGColorG");
            var bgB = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "BGColorB");
            var bgA = plan.PlannedConfigModifications.FirstOrDefault(m => m.Key == "BGColorA");

            Assert.IsNotNull(bgR);
            Assert.IsNotNull(bgG);
            Assert.IsNotNull(bgB);
            Assert.IsNotNull(bgA);
            Assert.AreEqual("0.92", bgA.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
