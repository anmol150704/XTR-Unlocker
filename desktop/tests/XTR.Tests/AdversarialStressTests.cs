using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class AdversarialStressTests
{
    private static string GetOptiScalerIniPath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "desktop", "src", "Runtime", "OptiScaler", "OptiScaler.ini");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            var directCandidate = Path.Combine(current, "src", "Runtime", "OptiScaler", "OptiScaler.ini");
            if (File.Exists(directCandidate))
                return Path.GetFullPath(directCandidate);

            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        throw new FileNotFoundException("Could not locate desktop/src/Runtime/OptiScaler/OptiScaler.ini");
    }

    private static string GetOptiScalerPackagePath()
    {
        return Path.GetDirectoryName(GetOptiScalerIniPath())!;
    }

    [TestMethod]
    public void StressTest_RealOptiScalerIni_HotfixIsLastSection()
    {
        var iniPath = GetOptiScalerIniPath();
        var lines = File.ReadAllLines(iniPath);
        var sectionHeaders = lines
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('[') && l.EndsWith(']'))
            .ToList();

        Assert.IsTrue(sectionHeaders.Count > 0, "OptiScaler.ini should contain sections.");
        Assert.AreEqual("[Hotfix]", sectionHeaders.Last(), "Adversarial Check: [Hotfix] MUST be the final section in baseline OptiScaler.ini.");
    }

    [TestMethod]
    public void StressTest_RealOptiScalerIni_BaselineUpscalersAreDlss()
    {
        var iniPath = GetOptiScalerIniPath();
        var content = File.ReadAllText(iniPath);

        Assert.AreEqual("dlss", RuntimeConfigService.ReadIniValue(content, "Upscalers", "Dx11Upscaler"));
        Assert.AreEqual("dlss", RuntimeConfigService.ReadIniValue(content, "Upscalers", "Dx12Upscaler"));
        Assert.AreEqual("dlss", RuntimeConfigService.ReadIniValue(content, "Upscalers", "VulkanUpscaler"));
    }

    [TestMethod]
    public void StressTest_RealOptiScalerIni_FsrAntiGhostingKeysBelongStrictlyToFsr()
    {
        var iniPath = GetOptiScalerIniPath();
        var content = File.ReadAllText(iniPath);

        // Verify keys exist in [FSR]
        Assert.IsNotNull(RuntimeConfigService.ReadIniValue(content, "FSR", "UseReactiveMaskForTransparency"), "UseReactiveMaskForTransparency must exist in [FSR]");
        Assert.IsNotNull(RuntimeConfigService.ReadIniValue(content, "FSR", "DlssReactiveMaskBias"), "DlssReactiveMaskBias must exist in [FSR]");
        Assert.IsNotNull(RuntimeConfigService.ReadIniValue(content, "FSR", "ReactiveScale"), "ReactiveScale must exist in [FSR]");

        // Verify keys do NOT exist in [FSRFGInputs]
        Assert.IsNull(RuntimeConfigService.ReadIniValue(content, "FSRFGInputs", "UseReactiveMaskForTransparency"), "UseReactiveMaskForTransparency must NOT be in [FSRFGInputs]");
        Assert.IsNull(RuntimeConfigService.ReadIniValue(content, "FSRFGInputs", "DlssReactiveMaskBias"), "DlssReactiveMaskBias must NOT be in [FSRFGInputs]");
        Assert.IsNull(RuntimeConfigService.ReadIniValue(content, "FSRFGInputs", "ReactiveScale"), "ReactiveScale must NOT be in [FSRFGInputs]");
    }

    [TestMethod]
    public void StressTest_ReplaceIniValue_NeverAppendsKeysAfterHotfix_OnRealOptiScalerIni()
    {
        var iniPath = GetOptiScalerIniPath();
        var originalContent = File.ReadAllText(iniPath);

        var keysToTest = new (string Section, string Key, string Value)[]
        {
            ("InitFlags", "DisableReactiveMask", "false"),
            ("FrameGen", "DrawUIOverFG", "true"),
            ("FrameGen", "DisableUI", "false"),
            ("FrameGen", "DisableHudless", "false"),
            ("FSR", "UseReactiveMaskForTransparency", "true"),
            ("FSR", "DlssReactiveMaskBias", "0.45"),
            ("FSR", "ReactiveScale", "2.0"),
            ("FSR", "AccAddPerFrame", "0.05"),
            ("FSR", "MinDisOccAcc", "-0.5"),
            ("OptiFG", "HUDFix", "true"),
            ("OptiFG", "HUDFixExtended", "true"),
            ("OptiFG", "DisableHUDFix", "false"),
            ("OptiFG", "MakeDepthCopy", "true"),
            ("OptiFG", "MakeMVCopy", "true"),
            ("OptiFG", "HUDFixImmediate", "true"),
            ("OptiFG", "AlwaysTrackHeaps", "true"),
            ("OptiFG", "HUDFixRelaxedResolutionCheck", "true"),
            ("Spoofing", "StreamlineSpoofing", "true"),
            ("Spoofing", "SpoofHAGS", "true")
        };

        var mutatedContent = originalContent;
        foreach (var (section, key, value) in keysToTest)
        {
            mutatedContent = RuntimeConfigService.ReplaceIniValue(mutatedContent, section, key, value);
        }

        // Verify every key was correctly written and is readable in its target section
        foreach (var (section, key, value) in keysToTest)
        {
            var actual = RuntimeConfigService.ReadIniValue(mutatedContent, section, key);
            Assert.AreEqual(value, actual, $"Key '{key}' in section '[{section}]' should match updated value '{value}'");
        }

        // Adversarial check: Inspect everything after [Hotfix]
        var lines = mutatedContent.Replace("\r\n", "\n").Split('\n');
        var hotfixIndex = Array.FindIndex(lines, l => l.Trim().Equals("[Hotfix]", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(hotfixIndex >= 0, "[Hotfix] section must exist.");

        // Read all keys present after [Hotfix]
        var hotfixKeysFound = new List<string>();
        for (var i = hotfixIndex + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator > 0)
            {
                hotfixKeysFound.Add(trimmed[..separator].Trim());
            }
        }

        // None of our test keys should ever appear under [Hotfix]
        foreach (var (_, key, _) in keysToTest)
        {
            Assert.IsFalse(hotfixKeysFound.Contains(key, StringComparer.OrdinalIgnoreCase),
                $"CRITICAL VIOLATION: Key '{key}' was leaked into or appended after [Hotfix]!");
        }
    }

    [TestMethod]
    public void StressTest_SectionBoundary_NonExistentSectionAppendedCleanlyWithHeader()
    {
        var ini = @"[FirstSection]
Key1=Val1

[Hotfix]
HotfixKey=HotfixVal
";
        var mutated = RuntimeConfigService.ReplaceIniValue(ini, "BrandNewSection", "NewKey", "NewVal");

        Assert.AreEqual("Val1", RuntimeConfigService.ReadIniValue(mutated, "FirstSection", "Key1"));
        Assert.AreEqual("HotfixVal", RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "HotfixKey"));
        Assert.AreEqual("NewVal", RuntimeConfigService.ReadIniValue(mutated, "BrandNewSection", "NewKey"));

        // When a new section is added, it must have its own [BrandNewSection] header, NOT pollute [Hotfix]
        Assert.IsNull(RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "NewKey"));
    }

    [TestMethod]
    [DataRow("DX11")]
    [DataRow("DX12")]
    [DataRow("Vulkan")]
    public async Task StressTest_NativeDlssG_PresetValidation_AllApis(string api)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_native_stress_{api}_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            if (api == "DX12")
                await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DLL");
            else if (api == "DX11")
                await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d11.dll"), "DLL");
            else
                await File.WriteAllTextAsync(Path.Combine(tempDir, "vulkan-1.dll"), "DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetOptiScalerPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                $"stress-game-{api}",
                $"Stress Game {api}",
                tempDir,
                pack,
                RuntimeIntegrationPreset.NativeDlssG);

            Assert.IsTrue(plan.CanExecute);

            // Mandatory requirement: NativeDlssG disables proxy frame interception:
            // [FrameGen] Enabled=false, FGInput=nofg, FGOutput=nofg
            var fgEnabled = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "Enabled");
            Assert.IsNotNull(fgEnabled, "NativeDlssG must specify [FrameGen] Enabled");
            Assert.AreEqual("false", fgEnabled.Value, "[FrameGen] Enabled must be 'false'");

            var fgInput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.IsNotNull(fgInput, "NativeDlssG must specify [FrameGen] FGInput");
            Assert.AreEqual("nofg", fgInput.Value, "[FrameGen] FGInput must be 'nofg'");

            var fgOutput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.IsNotNull(fgOutput, "NativeDlssG must specify [FrameGen] FGOutput");
            Assert.AreEqual("nofg", fgOutput.Value, "[FrameGen] FGOutput must be 'nofg'");

            // Mandatory requirement: NativeDlssG configures Streamline & HAGS spoofing
            var slSpoof = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Spoofing" && m.Key == "StreamlineSpoofing");
            Assert.IsNotNull(slSpoof);
            Assert.AreEqual("true", slSpoof.Value);

            var hagsSpoof = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Spoofing" && m.Key == "SpoofHAGS");
            Assert.IsNotNull(hagsSpoof);
            Assert.AreEqual("true", hagsSpoof.Value);

            // Mandatory requirement: NativeDlssG routes upscalers to dlss
            var dx12Upscaler = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Upscalers" && m.Key == "Dx12Upscaler");
            Assert.IsNotNull(dx12Upscaler);
            Assert.AreEqual("dlss", dx12Upscaler.Value);

            var dx11Upscaler = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Upscalers" && m.Key == "Dx11Upscaler");
            Assert.IsNotNull(dx11Upscaler);
            Assert.AreEqual("dlss", dx11Upscaler.Value);

            var vkUpscaler = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Upscalers" && m.Key == "VulkanUpscaler");
            Assert.IsNotNull(vkUpscaler);
            Assert.AreEqual("dlss", vkUpscaler.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("DX11", "Dx11Upscaler")]
    [DataRow("DX12", "Dx12Upscaler")]
    [DataRow("Vulkan", "VulkanUpscaler")]
    public async Task StressTest_DlssWithFrameGeneration_PresetValidation_AllApis(string api, string expectedKey)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_fg_stress_{api}_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            if (api == "DX12")
                await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DLL");
            else if (api == "DX11")
                await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d11.dll"), "DLL");
            else
                await File.WriteAllTextAsync(Path.Combine(tempDir, "vulkan-1.dll"), "DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetOptiScalerPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                $"stress-fg-game-{api}",
                $"Stress FG Game {api}",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute);

            // Mandatory: routes upscaler to dlss
            var upscaler = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Upscalers" && m.Key == expectedKey);
            Assert.IsNotNull(upscaler, $"Expected upscaler key '{expectedKey}' in plan");
            Assert.AreEqual("dlss", upscaler.Value, $"Upscaler '{expectedKey}' must be routed to 'dlss'");

            // Mandatory: FG output is fsrfg
            var fgOutput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.IsNotNull(fgOutput);
            Assert.AreEqual("fsrfg", fgOutput.Value);

            // Mandatory: FG Enabled is true
            var fgEnabled = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "Enabled");
            Assert.IsNotNull(fgEnabled);
            Assert.AreEqual("true", fgEnabled.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
