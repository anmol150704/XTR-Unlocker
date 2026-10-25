using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class FrameGenerationPlannerTests
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
    public async Task DlssWithFrameGen_WithNativeStreamlineAndDlssG_ConfiguresDlssgInputAndFsrfgOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_fg_plan_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Cyberpunk2077.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL_INTERPOSER");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "SL_DLSSG");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "cp2077-fg-test",
                "Cyberpunk 2077",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("Enable DLSS + Frame Generation", plan.Headline);

            // Verify FrameGen section config entries
            var fgEnabled = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "Enabled");
            Assert.IsNotNull(fgEnabled);
            Assert.AreEqual("true", fgEnabled.Value);

            var fgInput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.IsNotNull(fgInput);
            Assert.AreEqual("dlssg", fgInput.Value);

            // As proven via REA audit of OptiScaler 0.9.4 FG_Hooks.cpp:88, FGOutput must be 'fsrfg'
            var fgOutput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.IsNotNull(fgOutput);
            Assert.AreEqual("fsrfg", fgOutput.Value);

            // Verify Streamline GPU spoofing is activated to unlock DLSS-G menu switch on RTX 20/30 cards
            var spoofing = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Spoofing" && m.Key == "StreamlineSpoofing");
            Assert.IsNotNull(spoofing);
            Assert.AreEqual("true", spoofing.Value);

            // Verify in-game detection overlay is enabled
            var showFps = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "ShowFps");
            Assert.IsNotNull(showFps);
            Assert.AreEqual("true", showFps.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task DlssWithFrameGen_WithoutStreamline_FallsBackToUpscalerMotionVectors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_upscaler_fg_plan_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "GameWithoutStreamline.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "no-sl-fg-test",
                "Non Streamline Game",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute);

            var fgInput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.IsNotNull(fgInput);
            Assert.AreEqual("upscaler", fgInput.Value, "When Streamline DLSSG is not detected, OptiScaler must derive vectors from the upscaler.");

            var fgOutput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.IsNotNull(fgOutput);
            Assert.AreEqual("fsrfg", fgOutput.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task DlssWithFrameGen_AppliesAntiGhostingShieldSettings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_antighost_plan_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "antighost-test",
                "Ghosting Test Game",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute);

            // Verify Anti-Ghosting HUD fix and buffer duplication entries
            var hudFix = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "HUDFix");
            Assert.IsNotNull(hudFix);
            Assert.AreEqual("true", hudFix.Value);

            var depthCopy = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy");
            Assert.IsNotNull(depthCopy);
            Assert.AreEqual("true", depthCopy.Value);

            var mvCopy = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy");
            Assert.IsNotNull(mvCopy);
            Assert.AreEqual("true", mvCopy.Value);

            var transparency = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FSRFGInputs" && m.Key == "UseReactiveMaskForTransparency");
            Assert.IsNotNull(transparency);
            Assert.AreEqual("true", transparency.Value);

            var borderless = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "ForceBorderless");
            Assert.IsNotNull(borderless);
            Assert.AreEqual("true", borderless.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task NativeDlssG_ConfiguresDirectDlssgOutputAndHardwarePipeline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_native_dlssg_plan_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Cyberpunk2077.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "SL_DLSSG");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "cp2077-native-fg-test",
                "Cyberpunk 2077 Native FG",
                tempDir,
                pack,
                RuntimeIntegrationPreset.NativeDlssG);

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("Enable Native DLSS 3 Hardware Frame Generation", plan.Headline);

            // Native DLSS-G directly outputs dlssg to optical flow hardware
            var fgOutput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.IsNotNull(fgOutput);
            Assert.AreEqual("dlssg", fgOutput.Value);

            var upscaler = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Upscalers" && m.Key == "Dx12Upscaler");
            Assert.IsNotNull(upscaler);
            Assert.AreEqual("dlss", upscaler.Value);

            var fgInput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.IsNotNull(fgInput);
            Assert.AreEqual("dlssg", fgInput.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}