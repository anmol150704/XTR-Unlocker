using System;
using System.IO;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class RuntimeConfigTests
{
    [TestMethod]
    public async Task ApplyProfile_ModifiesOwnedKeys_PreservesUnknownKeysAndComments()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_ini_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var iniPath = Path.Combine(tempDir, "OptiScaler.ini");

            var originalIni = @"# OptiScaler Configuration File
; User custom comments should stay preserved

[Upscalers]
Dx11Upscaler=fsr22
Dx12Upscaler=xess
VulkanUpscaler=fsr21

[FrameGen]
FGInput=auto
FGOutput=auto

[CustomUserSection]
MyCustomKey=KeepThisValue
AnotherTuningValue=123
";
            await File.WriteAllTextAsync(iniPath, originalIni);

            var profile = new RuntimeIntegrationProfile
            {
                GameId = "ini-test-game",
                Api = "DX12",
                SelectedUpscaler = "dlss",
                FrameGenerationInput = "dlssg",
                FrameGenerationOutput = "fsr3"
            };

            await RuntimeConfigService.ApplyProfileAsync(tempDir, profile);

            var resultIni = await File.ReadAllTextAsync(iniPath);

            // Verify owned changes were applied
            Assert.IsTrue(resultIni.Contains("Dx12Upscaler=dlss"));
            Assert.IsTrue(resultIni.Contains("FGInput=dlssg"));
            Assert.IsTrue(resultIni.Contains("FGOutput=fsr3"));

            // Verify comments and user sections were preserved verbatim
            Assert.IsTrue(resultIni.Contains("# OptiScaler Configuration File"));
            Assert.IsTrue(resultIni.Contains("; User custom comments should stay preserved"));
            Assert.IsTrue(resultIni.Contains("[CustomUserSection]"));
            Assert.IsTrue(resultIni.Contains("MyCustomKey=KeepThisValue"));
            Assert.IsTrue(resultIni.Contains("AnotherTuningValue=123"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void ReadIniValue_RetrievesCorrectKeyUnderCorrectSection()
    {
        var ini = @"
[OptiFG]
HUDFix=true
MakeDepthCopy=true

[FSR]
UseReactiveMaskForTransparency=true
DlssReactiveMaskBias=0.45

[Hotfix]
UseReactiveMaskForTransparency=false
";

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(ini, "OptiFG", "HUDFix"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(ini, "OptiFG", "MakeDepthCopy"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(ini, "FSR", "UseReactiveMaskForTransparency"));
        Assert.AreEqual("0.45", RuntimeConfigService.ReadIniValue(ini, "FSR", "DlssReactiveMaskBias"));

        // Case insensitivity of section and key
        Assert.AreEqual("0.45", RuntimeConfigService.ReadIniValue(ini, "fsr", "dlssreactivemaskbias"));

        // Section isolation: Hotfix has false, FSR has true
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(ini, "Hotfix", "UseReactiveMaskForTransparency"));

        // Non-existent key or section returns null
        Assert.IsNull(RuntimeConfigService.ReadIniValue(ini, "OptiFG", "NonExistentKey"));
        Assert.IsNull(RuntimeConfigService.ReadIniValue(ini, "NonExistentSection", "HUDFix"));
        Assert.IsNull(RuntimeConfigService.ReadIniValue("", "OptiFG", "HUDFix"));
    }

    [TestMethod]
    public void ReplaceIniValue_UpdatesExistingAndInsertsMissingInCorrectSection()
    {
        var ini = @"[FrameGen]
FGInput=dlssg
FGOutput=fsrfg

[Menu]
ShortcutKey=0x2D
";

        // Update existing key
        var updated = RuntimeConfigService.ReplaceIniValue(ini, "FrameGen", "FGOutput", "nofg");
        Assert.AreEqual("nofg", RuntimeConfigService.ReadIniValue(updated, "FrameGen", "FGOutput"));
        Assert.AreEqual("dlssg", RuntimeConfigService.ReadIniValue(updated, "FrameGen", "FGInput"));

        // Insert new key into existing section without creating duplicate sections
        var withNewKey = RuntimeConfigService.ReplaceIniValue(updated, "FrameGen", "ForcedFlag", "true");
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(withNewKey, "FrameGen", "ForcedFlag"));
        Assert.AreEqual("0x2D", RuntimeConfigService.ReadIniValue(withNewKey, "Menu", "ShortcutKey"));

        // Insert key into a completely new section
        var withNewSection = RuntimeConfigService.ReplaceIniValue(withNewKey, "AntiGhosting", "ShieldEnabled", "1");
        Assert.AreEqual("1", RuntimeConfigService.ReadIniValue(withNewSection, "AntiGhosting", "ShieldEnabled"));
    }

    [TestMethod]
    public async Task UpdateSettingsAsync_AtomicallyAppliesMultipleSettingsToFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_batch_ini_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var iniPath = Path.Combine(tempDir, "OptiScaler.ini");
            var original = @"[FrameGen]
FGOutput=fsrfg
FGInput=dlssg

[OptiFG]
HUDFix=false
";
            await File.WriteAllTextAsync(iniPath, original);

            var updates = new[]
            {
                ("FrameGen", "FGOutput", "nofg"),
                ("OptiFG", "HUDFix", "true"),
                ("FSR", "DlssReactiveMaskBias", "0.45")
            };

            await RuntimeConfigService.UpdateSettingsAsync(tempDir, updates);

            var content = await File.ReadAllTextAsync(iniPath);
            Assert.AreEqual("nofg", RuntimeConfigService.ReadIniValue(content, "FrameGen", "FGOutput"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(content, "OptiFG", "HUDFix"));
            Assert.AreEqual("0.45", RuntimeConfigService.ReadIniValue(content, "FSR", "DlssReactiveMaskBias"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task AntiGhostingShield_AllKeysStrictlySectionBounded_ZeroOrphanedKeysAtEof()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_antighost_bounds_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var iniPath = Path.Combine(tempDir, "OptiScaler.ini");

            // Multi-section INI matching realistic OptiScaler layout with [Hotfix] as the final section
            var initialContent = @"[InitFlags]
DisableReactiveMask=auto

[FrameGen]
DrawUIOverFG=auto
DisableUI=auto
DisableHudless=auto

[OptiFG]
HUDFix=false
HUDFixExtended=false
DisableHUDFix=true
MakeDepthCopy=false
MakeMVCopy=false

[FSR]
UseReactiveMaskForTransparency=false
DlssReactiveMaskBias=0.0
ReactiveScale=1.0
AccAddPerFrame=0.333
MinDisOccAcc=-0.333

[Menu]
ShortcutKey=-1
ShowFps=auto
FpsOverlayType=auto
OverlayMenu=auto

[Spoofing]
StreamlineSpoofing=auto

[Hotfix]
DisableReactiveMask=auto
";
            await File.WriteAllTextAsync(iniPath, initialContent);

            var antiGhostingUpdates = new[]
            {
                // [InitFlags]
                ("InitFlags", "DisableReactiveMask", "false"),

                // [FrameGen]
                ("FrameGen", "DrawUIOverFG", "true"),
                ("FrameGen", "DisableUI", "false"),
                ("FrameGen", "DisableHudless", "false"),

                // [FSR]
                ("FSR", "UseReactiveMaskForTransparency", "true"),
                ("FSR", "DlssReactiveMaskBias", "0.45"),
                ("FSR", "ReactiveScale", "2.0"),
                ("FSR", "AccAddPerFrame", "0.05"),
                ("FSR", "MinDisOccAcc", "-0.5"),

                // [OptiFG]
                ("OptiFG", "HUDFix", "true"),
                ("OptiFG", "HUDFixExtended", "true"),
                ("OptiFG", "DisableHUDFix", "false"),
                ("OptiFG", "MakeDepthCopy", "true"),
                ("OptiFG", "MakeMVCopy", "true"),

                // [Menu]
                ("Menu", "ShortcutKey", "0x2D"),
                ("Menu", "ShowFps", "true"),
                ("Menu", "FpsOverlayType", "2"),
                ("Menu", "OverlayMenu", "true"),

                // [Spoofing]
                ("Spoofing", "StreamlineSpoofing", "true")
            };

            await RuntimeConfigService.UpdateSettingsAsync(tempDir, antiGhostingUpdates);

            var finalContent = await File.ReadAllTextAsync(iniPath);

            // 1. Verify all [InitFlags] keys
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "InitFlags", "DisableReactiveMask"));

            // 2. Verify all [FrameGen] keys
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "FrameGen", "DrawUIOverFG"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "FrameGen", "DisableUI"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "FrameGen", "DisableHudless"));

            // 3. Verify all [FSR] keys
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "UseReactiveMaskForTransparency"));
            Assert.AreEqual("0.45", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "DlssReactiveMaskBias"));
            Assert.AreEqual("2.0", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "ReactiveScale"));
            Assert.AreEqual("0.05", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "AccAddPerFrame"));
            Assert.AreEqual("-0.5", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "MinDisOccAcc"));

            // 4. Verify all [OptiFG] keys
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "HUDFix"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "HUDFixExtended"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "DisableHUDFix"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "MakeDepthCopy"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "MakeMVCopy"));

            // 5. Verify [Menu] and [Spoofing]
            Assert.AreEqual("0x2D", RuntimeConfigService.ReadIniValue(finalContent, "Menu", "ShortcutKey"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "Menu", "ShowFps"));
            Assert.AreEqual("2", RuntimeConfigService.ReadIniValue(finalContent, "Menu", "FpsOverlayType"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "Menu", "OverlayMenu"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "Spoofing", "StreamlineSpoofing"));

            // 6. Verify [Hotfix] remained completely unpolluted with initial value and zero orphaned keys
            Assert.AreEqual("auto", RuntimeConfigService.ReadIniValue(finalContent, "Hotfix", "DisableReactiveMask"));
            Assert.IsNull(RuntimeConfigService.ReadIniValue(finalContent, "Hotfix", "HUDFix"));
            Assert.IsNull(RuntimeConfigService.ReadIniValue(finalContent, "Hotfix", "ReactiveScale"));
            Assert.IsNull(RuntimeConfigService.ReadIniValue(finalContent, "Hotfix", "UseReactiveMaskForTransparency"));

            // 7. Verify no orphaned keys appended at the end of the file after [Hotfix]
            var lines = finalContent.Replace("\r\n", "\n").Split('\n');
            var hotfixIndex = Array.FindIndex(lines, l => l.Trim().Equals("[Hotfix]", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(hotfixIndex >= 0);

            for (var i = hotfixIndex + 1; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                    continue;

                // The only key in [Hotfix] is DisableReactiveMask=auto
                Assert.AreEqual("DisableReactiveMask=auto", trimmed,
                    $"Found unexpected orphaned or misplaced key in [Hotfix] section: '{trimmed}'");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void SectionAwareWriting_PreventsCrossSectionCollisionsForDuplicateKeyNames()
    {
        var ini = @"[InitFlags]
DisableReactiveMask=auto

[Hotfix]
DisableReactiveMask=auto
";

        // Updating InitFlags must not modify Hotfix
        var updated = RuntimeConfigService.ReplaceIniValue(ini, "InitFlags", "DisableReactiveMask", "false");
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(updated, "InitFlags", "DisableReactiveMask"));
        Assert.AreEqual("auto", RuntimeConfigService.ReadIniValue(updated, "Hotfix", "DisableReactiveMask"));

        // Updating Hotfix must not modify InitFlags
        var updatedHotfix = RuntimeConfigService.ReplaceIniValue(updated, "Hotfix", "DisableReactiveMask", "custom");
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(updatedHotfix, "InitFlags", "DisableReactiveMask"));
        Assert.AreEqual("custom", RuntimeConfigService.ReadIniValue(updatedHotfix, "Hotfix", "DisableReactiveMask"));
    }

    [TestMethod]
    public async Task AntiGhostingShield_Disabling_WritesCleanDefaultsUnderTargetSections()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_antighost_disable_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var iniPath = Path.Combine(tempDir, "OptiScaler.ini");

            var initialContent = @"[InitFlags]
DisableReactiveMask=false

[FrameGen]
DrawUIOverFG=true
DisableUI=false
DisableHudless=false

[OptiFG]
HUDFix=true
HUDFixExtended=true
DisableHUDFix=false
MakeDepthCopy=true
MakeMVCopy=true

[FSR]
UseReactiveMaskForTransparency=true
DlssReactiveMaskBias=0.45
ReactiveScale=2.0
AccAddPerFrame=0.05
MinDisOccAcc=-0.5

[Hotfix]
DisableReactiveMask=auto
";
            await File.WriteAllTextAsync(iniPath, initialContent);

            var disableUpdates = new[]
            {
                ("InitFlags", "DisableReactiveMask", "true"),
                ("FrameGen", "DrawUIOverFG", "false"),
                ("FrameGen", "DisableUI", "true"),
                ("FrameGen", "DisableHudless", "false"),
                ("FSR", "UseReactiveMaskForTransparency", "false"),
                ("FSR", "DlssReactiveMaskBias", "0.45"),
                ("FSR", "ReactiveScale", "1.0"),
                ("FSR", "AccAddPerFrame", "0.333"),
                ("FSR", "MinDisOccAcc", "-0.333"),
                ("OptiFG", "HUDFix", "false"),
                ("OptiFG", "HUDFixExtended", "false"),
                ("OptiFG", "DisableHUDFix", "true"),
                ("OptiFG", "MakeDepthCopy", "false"),
                ("OptiFG", "MakeMVCopy", "false")
            };

            await RuntimeConfigService.UpdateSettingsAsync(tempDir, disableUpdates);

            var finalContent = await File.ReadAllTextAsync(iniPath);
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "InitFlags", "DisableReactiveMask"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "FrameGen", "DrawUIOverFG"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "FrameGen", "DisableUI"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "HUDFix"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "DisableHUDFix"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "OptiFG", "MakeDepthCopy"));
            Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "UseReactiveMaskForTransparency"));
            Assert.AreEqual("1.0", RuntimeConfigService.ReadIniValue(finalContent, "FSR", "ReactiveScale"));
            Assert.AreEqual("auto", RuntimeConfigService.ReadIniValue(finalContent, "Hotfix", "DisableReactiveMask"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

