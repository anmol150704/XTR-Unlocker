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

            // Verify XTR Menu shortcut keys & Apple Liquid theming
            var shortcutKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "ShortcutKey");
            Assert.IsNotNull(shortcutKey);
            Assert.AreEqual("0x2D", shortcutKey.Value);

            var fpsKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "FpsShortcutKey");
            Assert.IsNotNull(fpsKey);
            Assert.AreEqual("0x79", fpsKey.Value);

            var fpsCycleKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "FpsCycleShortcutKey");
            Assert.IsNotNull(fpsCycleKey);
            Assert.AreEqual("0x78", fpsCycleKey.Value);

            var fgKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "FGShortcutKey");
            Assert.IsNotNull(fgKey);
            Assert.AreEqual("0x7B", fgKey.Value);

            var accentColorR = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "AccentColorR");
            Assert.IsNotNull(accentColorR);
            Assert.AreEqual("0.20", accentColorR.Value);
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

            var transparency = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FSR" && m.Key == "UseReactiveMaskForTransparency");
            Assert.IsNotNull(transparency);
            Assert.AreEqual("true", transparency.Value);

            var borderless = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "ForceBorderless");
            Assert.IsNotNull(borderless);
            Assert.AreEqual("true", borderless.Value);

            var initFlags = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "InitFlags" && m.Key == "DisableReactiveMask");
            Assert.IsNotNull(initFlags);
            Assert.AreEqual("false", initFlags.Value);

            var drawUi = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "DrawUIOverFG");
            Assert.IsNotNull(drawUi);
            Assert.AreEqual("true", drawUi.Value);

            var reactiveScale = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FSR" && m.Key == "ReactiveScale");
            Assert.IsNotNull(reactiveScale);
            Assert.AreEqual("2.0", reactiveScale.Value);
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

            // Native DLSS-G cleanly disables proxy frame interception and enables hardware spoofing
            var fgOutput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGOutput");
            Assert.IsNotNull(fgOutput);
            Assert.AreEqual("nofg", fgOutput.Value);

            var upscaler = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Upscalers" && m.Key == "Dx12Upscaler");
            Assert.IsNotNull(upscaler);
            Assert.AreEqual("dlss", upscaler.Value);

            var fgInput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.IsNotNull(fgInput);
            Assert.AreEqual("nofg", fgInput.Value);

            var fgEnabled = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "Enabled");
            Assert.IsNotNull(fgEnabled);
            Assert.AreEqual("false", fgEnabled.Value);

            var streamlineSpoof = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Spoofing" && m.Key == "StreamlineSpoofing");
            Assert.IsNotNull(streamlineSpoof);
            Assert.AreEqual("true", streamlineSpoof.Value);

            var spoofHags = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Spoofing" && m.Key == "SpoofHAGS");
            Assert.IsNotNull(spoofHags);
            Assert.AreEqual("true", spoofHags.Value);

            var shortcutKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "ShortcutKey");
            Assert.IsNotNull(shortcutKey);
            Assert.AreEqual("0x2D", shortcutKey.Value);

            var fpsKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "FpsShortcutKey");
            Assert.IsNotNull(fpsKey);
            Assert.AreEqual("0x79", fpsKey.Value);

            var fgKey = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Menu" && m.Key == "FGShortcutKey");
            Assert.IsNotNull(fgKey);
            Assert.AreEqual("0x7B", fgKey.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task TurboBoost_WithNativeDlssG_ConfiguresHardwareBypassAndPresetEAndAnisotropy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_boost_native_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "nvngx_dlssg.dll"), "NVNGX_DLSSG");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "boost-native-test",
                "Game Native Boost",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("⚡ Activate XTR Turbo Boost", plan.Headline);

            // Hardware optical flow bypass
            Assert.AreEqual("nofg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
            Assert.AreEqual("nofg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "Enabled").Value);

            // DLSS Preset E
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetOverride").Value);
            Assert.AreEqual("5", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);

            // 16x Anisotropic filtering
            Assert.AreEqual("16", plan.PlannedConfigModifications.First(m => m.Section == "Anisotropy" && m.Key == "AnisotropyOverride").Value);

            // Mipmap LOD bias
            Assert.AreEqual("-0.5", plan.PlannedConfigModifications.First(m => m.Section == "Mipmap" && m.Key == "MipmapBiasOverride").Value);

            // RCAS Sharpness
            Assert.AreEqual("0.35", plan.PlannedConfigModifications.First(m => m.Section == "Sharpness" && m.Key == "Sharpness").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task TurboBoost_WithUniversalHardware_ConfiguresFsrBridgeAndPresetEAndFramePacing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_boost_univ_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "boost-univ-test",
                "Game Universal Boost",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("⚡ Activate XTR Turbo Boost", plan.Headline);

            // FrameGen enabled via FSR 3.1 bridge
            Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "Enabled").Value);

            // Frame Pacing tuning
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "FSRFG" && m.Key == "FramePacingTuning").Value);
            Assert.AreEqual("0.01", plan.PlannedConfigModifications.First(m => m.Section == "FSRFG" && m.Key == "FPTSafetyMarginInMs").Value);
            Assert.AreEqual("0.3", plan.PlannedConfigModifications.First(m => m.Section == "FSRFG" && m.Key == "FPTVarianceFactor").Value);

            // Anti-Ghosting shield
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix").Value);
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy").Value);
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy").Value);

            // DLSS Preset E
            Assert.AreEqual("5", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);

            // 16x Anisotropic filtering & Mipmap LOD bias
            Assert.AreEqual("16", plan.PlannedConfigModifications.First(m => m.Section == "Anisotropy" && m.Key == "AnisotropyOverride").Value);
            Assert.AreEqual("-0.5", plan.PlannedConfigModifications.First(m => m.Section == "Mipmap" && m.Key == "MipmapBiasOverride").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Game_BadgeLogic_EvaluatesTurboBoostAndNativeCorrectly()
    {
        // 1. Initial empty state: no runtime, no DLSS-G
        Assert.IsFalse(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: false, isTurboBoost: false, hasActiveRuntimeFg: false));
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasRuntimeBoostOrFg: false, hasNativeDlssG: false));
        Assert.AreEqual(string.Empty, DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: false, isTurboBoost: false, hasActiveRuntimeFg: false, hasNativeDlssG: false));

        // 2. Native DLSS 3 FG present in game, no runtime installed
        var hasBoostOrFg2 = DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: false, isTurboBoost: false, hasActiveRuntimeFg: false);
        Assert.IsFalse(hasBoostOrFg2);
        Assert.IsTrue(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasBoostOrFg2, hasNativeDlssG: true));
        Assert.AreEqual("DLSS 3 FG", DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: false, isTurboBoost: false, hasActiveRuntimeFg: false, hasNativeDlssG: true));

        // 3. Runtime installed with standard FG
        var hasBoostOrFg3 = DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: true);
        Assert.IsTrue(hasBoostOrFg3);
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasBoostOrFg3, hasNativeDlssG: false));
        Assert.AreEqual("⚡ FG ACTIVE", DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: true, hasNativeDlssG: false));

        // 4. Runtime installed with Turbo Boost
        var hasBoostOrFg4 = DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: true, isTurboBoost: true, hasActiveRuntimeFg: true);
        Assert.IsTrue(hasBoostOrFg4);
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasBoostOrFg4, hasNativeDlssG: false));
        Assert.AreEqual("⚡ BOOST ACTIVE", DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: true, isTurboBoost: true, hasActiveRuntimeFg: true, hasNativeDlssG: false));

        // 5. Runtime installed with LatestDlssOnly (FG disabled) and native DLSS 3 in game
        var hasBoostOrFg5 = DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: false);
        Assert.IsFalse(hasBoostOrFg5);
        Assert.IsTrue(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasBoostOrFg5, hasNativeDlssG: true));
        Assert.AreEqual("DLSS 3 FG", DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: false, hasNativeDlssG: true));

        // 6. Runtime installed with LatestDlssOnly and NO native DLSS 3 in game
        var hasBoostOrFg6 = DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: false);
        Assert.IsFalse(hasBoostOrFg6);
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasBoostOrFg6, hasNativeDlssG: false));
        Assert.AreEqual(string.Empty, DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: false, hasNativeDlssG: false));
    }

    [TestMethod]
    public async Task TurboBoost_InstallPlan_SetsProfilePresetAndIniCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_boost_install_test_{Guid.NewGuid():N}");
        var gameId = $"boost-install-test-{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                gameId,
                "Game Install Boost Test",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            Assert.IsTrue(plan.CanExecute);
            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
            Assert.IsNotNull(manifest);

            var profile = await RuntimeIntegrationProfileStore.LoadAsync(gameId);
            Assert.IsNotNull(profile);
            Assert.AreEqual("TurboBoost", profile.Options["Preset"]?.ToString());

            var iniPath = Path.Combine(tempDir, "OptiScaler.ini");
            Assert.IsTrue(File.Exists(iniPath));
            var iniContent = await File.ReadAllTextAsync(iniPath);
            Assert.AreEqual("5", RuntimeConfigService.ReadIniValue(iniContent, "DLSS", "RenderPresetForAll"));
            Assert.AreEqual("16", RuntimeConfigService.ReadIniValue(iniContent, "Anisotropy", "AnisotropyOverride"));
            Assert.AreEqual("-0.5", RuntimeConfigService.ReadIniValue(iniContent, "Mipmap", "MipmapBiasOverride"));
            Assert.AreEqual("0.35", RuntimeConfigService.ReadIniValue(iniContent, "Sharpness", "Sharpness"));
            Assert.AreEqual("0x2D", RuntimeConfigService.ReadIniValue(iniContent, "Menu", "ShortcutKey"));
            Assert.AreEqual("0x79", RuntimeConfigService.ReadIniValue(iniContent, "Menu", "FpsShortcutKey"));
            Assert.AreEqual("0x78", RuntimeConfigService.ReadIniValue(iniContent, "Menu", "FpsCycleShortcutKey"));
            Assert.AreEqual("0x7B", RuntimeConfigService.ReadIniValue(iniContent, "Menu", "FGShortcutKey"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(iniContent, "Menu", "ShowFps"));
            Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(iniContent, "Menu", "DisableSplash"));

            await RuntimeInstallService.RestoreAsync(gameId, tempDir);
            Assert.IsFalse(File.Exists(iniPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}