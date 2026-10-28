using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Hardware;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ChallengerMilestone1StressTests
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

    #region 1. Hardware Profile Classification Stress Tests

    [TestMethod]
    public void StressTest_HardwareClassifier_AdaLovelace_Series()
    {
        string[] adaModels =
        [
            "NVIDIA GeForce RTX 4090",
            "NVIDIA GeForce RTX 4080 Super",
            "NVIDIA GeForce RTX 4070 Ti SUPER",
            "NVIDIA GeForce RTX 4070",
            "NVIDIA GeForce RTX 4060 Ti 16GB",
            "NVIDIA GeForce RTX 4060 Laptop GPU",
            "NVIDIA GeForce RTX 4050 Laptop GPU",
            "NVIDIA RTX 6000 Ada Generation"
        ];

        foreach (var model in adaModels)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Nvidia, info.Vendor, $"Expected NVIDIA for {model}");
            Assert.IsTrue(info.Architecture.Contains("Ada Lovelace", StringComparison.OrdinalIgnoreCase), $"Expected Ada Lovelace architecture for {model}, was {info.Architecture}");
            Assert.IsTrue(info.SupportsNativeDlssG, $"Expected native DLSS-G support for {model}");
            Assert.IsTrue(info.IsAdaOrBlackwell, $"Expected IsAdaOrBlackwell == true for {model}");
            Assert.AreEqual("Native DLSS 3 Hardware FG", info.RecommendedFgMode, $"Expected Native DLSS 3 recommended for {model}");
            Assert.AreEqual("Preset E (Anti-Ghosting Champion)", info.RecommendedNeuralPreset, $"Expected Preset E for {model}");
        }
    }

    [TestMethod]
    public void StressTest_HardwareClassifier_Ampere_Series()
    {
        string[] ampereModels =
        [
            "NVIDIA GeForce RTX 3090 Ti",
            "NVIDIA GeForce RTX 3090",
            "NVIDIA GeForce RTX 3080 Ti Laptop GPU",
            "NVIDIA GeForce RTX 3080",
            "NVIDIA GeForce RTX 3070 Ti",
            "NVIDIA GeForce RTX 3070",
            "NVIDIA GeForce RTX 3060 Ti",
            "NVIDIA GeForce RTX 3060",
            "NVIDIA GeForce RTX 3050 6GB",
            "NVIDIA RTX A6000",
            "NVIDIA A100-PCIE-40GB"
        ];

        foreach (var model in ampereModels)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Nvidia, info.Vendor, $"Expected NVIDIA for {model}");
            Assert.IsTrue(info.Architecture.Contains("Ampere", StringComparison.OrdinalIgnoreCase), $"Expected Ampere architecture for {model}, was {info.Architecture}");
            Assert.IsFalse(info.SupportsNativeDlssG, $"Ampere does not support native DLSS 3 FG: {model}");
            Assert.IsFalse(info.IsAdaOrBlackwell, $"Ampere is not Ada/Blackwell: {model}");
            Assert.IsTrue(info.RecommendedFgMode.Contains("Universal FG Bridge", StringComparison.OrdinalIgnoreCase), $"Expected Universal FG Bridge for {model}");
            Assert.IsTrue(info.RecommendedFgMode.Contains("Streamline Spoofing", StringComparison.OrdinalIgnoreCase), $"Expected Streamline Spoofing for {model}");
            Assert.AreEqual("Preset E (Anti-Ghosting Champion)", info.RecommendedNeuralPreset);
        }
    }

    [TestMethod]
    public void StressTest_HardwareClassifier_Turing_Series()
    {
        string[] turingRtx =
        [
            "NVIDIA GeForce RTX 2080 Ti",
            "NVIDIA GeForce RTX 2080 SUPER",
            "NVIDIA GeForce RTX 2070",
            "NVIDIA GeForce RTX 2060 SUPER",
            "NVIDIA GeForce RTX 2060 12GB",
            "NVIDIA GeForce RTX 2050"
        ];

        foreach (var model in turingRtx)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Nvidia, info.Vendor);
            Assert.IsTrue(info.Architecture.Contains("Turing", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(info.SupportsNativeDlssG);
            Assert.IsTrue(info.RecommendedFgMode.Contains("Universal FG Bridge"));
            Assert.IsTrue(info.RecommendedFgMode.Contains("Streamline Spoofing"));
        }

        string[] turingGtx =
        [
            "NVIDIA GeForce GTX 1660 Ti",
            "NVIDIA GeForce GTX 1660 SUPER",
            "NVIDIA GeForce GTX 1660",
            "NVIDIA GeForce GTX 1650 SUPER",
            "NVIDIA GeForce GTX 1650"
        ];

        foreach (var model in turingGtx)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Nvidia, info.Vendor);
            Assert.IsTrue(info.Architecture.Contains("GTX 16 Series") || info.Architecture.Contains("Turing"));
            Assert.IsFalse(info.SupportsNativeDlssG);
            Assert.AreEqual("Universal FG Bridge (FSR 3.1)", info.RecommendedFgMode);
        }
    }

    [TestMethod]
    public void StressTest_HardwareClassifier_AmdRdna_Series()
    {
        string[] rdna3Models =
        [
            "AMD Radeon RX 7900 XTX",
            "AMD Radeon RX 7900 XT",
            "AMD Radeon RX 7900 GRE",
            "AMD Radeon RX 7800 XT",
            "AMD Radeon RX 7700 XT",
            "AMD Radeon RX 7600 XT"
        ];

        foreach (var model in rdna3Models)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Amd, info.Vendor);
            Assert.IsTrue(info.Architecture.Contains("RDNA 3"), $"Expected RDNA 3 for {model}, was {info.Architecture}");
            Assert.IsFalse(info.SupportsNativeDlssG);
            Assert.AreEqual("Universal FG Bridge (FSR 3.1)", info.RecommendedFgMode);
            Assert.AreEqual("Preset E (Anti-Ghosting Champion)", info.RecommendedNeuralPreset);
        }

        string[] rdna2Models =
        [
            "AMD Radeon RX 6950 XT",
            "AMD Radeon RX 6900 XT",
            "AMD Radeon RX 6800 XT",
            "AMD Radeon RX 6700 XT",
            "AMD Radeon RX 6600 XT"
        ];

        foreach (var model in rdna2Models)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Amd, info.Vendor);
            Assert.IsTrue(info.Architecture.Contains("RDNA 2"), $"Expected RDNA 2 for {model}, was {info.Architecture}");
            Assert.IsFalse(info.SupportsNativeDlssG);
            Assert.AreEqual("Universal FG Bridge (FSR 3.1)", info.RecommendedFgMode);
        }

        string[] legacyAmd =
        [
            "AMD Radeon RX 5700 XT",
            "AMD Radeon VII",
            "Radeon RX 580 Series"
        ];

        foreach (var model in legacyAmd)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Amd, info.Vendor);
            Assert.IsFalse(info.SupportsNativeDlssG);
            Assert.AreEqual("Universal FG Bridge (FSR 3.1)", info.RecommendedFgMode);
        }
    }

    [TestMethod]
    public void StressTest_HardwareClassifier_IntelArc_Series()
    {
        string[] arcModels =
        [
            "Intel(R) Arc(TM) A770 Graphics",
            "Intel(R) Arc(TM) A750 Graphics",
            "Intel(R) Arc(TM) A580 Graphics",
            "Intel(R) Arc(TM) A380 Graphics",
            "Intel(R) Arc(TM) Pro A60M",
            "Intel Arc Battlemage B580"
        ];

        foreach (var model in arcModels)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Intel, info.Vendor);
            Assert.IsTrue(info.Architecture.Contains("Arc"), $"Expected Arc architecture for {model}, was {info.Architecture}");
            Assert.IsFalse(info.SupportsNativeDlssG);
            Assert.AreEqual("Universal FG Bridge (FSR 3.1)", info.RecommendedFgMode);
            Assert.AreEqual("Preset E (Anti-Ghosting Champion)", info.RecommendedNeuralPreset);
        }

        string[] igpuModels =
        [
            "Intel(R) UHD Graphics 770",
            "Intel(R) Iris(R) Xe Graphics"
        ];

        foreach (var model in igpuModels)
        {
            var info = new GpuHardwareInfo { Name = model };
            GpuHardwareDetector.ClassifyGpu(info);

            Assert.AreEqual(GpuVendor.Intel, info.Vendor);
            Assert.IsTrue(info.Architecture.Contains("Iris / UHD"), $"Expected Iris/UHD for {model}, was {info.Architecture}");
            Assert.IsFalse(info.SupportsNativeDlssG);
        }
    }

    [TestMethod]
    public void StressTest_HardwareClassifier_EdgeCasesAndCasing()
    {
        // Weird casing
        var info1 = new GpuHardwareInfo { Name = "nvIdiA gEforCe rTx 4090 ti" };
        GpuHardwareDetector.ClassifyGpu(info1);
        Assert.AreEqual(GpuVendor.Nvidia, info1.Vendor);
        Assert.IsTrue(info1.Architecture.Contains("Ada Lovelace"));
        Assert.IsTrue(info1.SupportsNativeDlssG);

        var info2 = new GpuHardwareInfo { Name = "aMd rAdEon rX 7900 xTx" };
        GpuHardwareDetector.ClassifyGpu(info2);
        Assert.AreEqual(GpuVendor.Amd, info2.Vendor);
        Assert.IsTrue(info2.Architecture.Contains("RDNA 3"));

        var info3 = new GpuHardwareInfo { Name = "iNtEl aRc a770" };
        GpuHardwareDetector.ClassifyGpu(info3);
        Assert.AreEqual(GpuVendor.Intel, info3.Vendor);
        Assert.IsTrue(info3.Architecture.Contains("Arc"));

        // Unknown / Obscure vendor
        var infoUnknown = new GpuHardwareInfo { Name = "Qualcomm Adreno 680" };
        GpuHardwareDetector.ClassifyGpu(infoUnknown);
        Assert.AreEqual(GpuVendor.Other, infoUnknown.Vendor);
        Assert.IsFalse(infoUnknown.SupportsNativeDlssG);
        Assert.AreEqual("Standard Graphics Architecture", infoUnknown.Architecture);
        Assert.AreEqual("Universal FG Bridge (FSR 3.1)", infoUnknown.RecommendedFgMode);

        // VRAM calculation boundaries
        var vramInfo = new GpuHardwareInfo { VramBytes = 24UL * 1024 * 1024 * 1024 };
        Assert.AreEqual(24.0, vramInfo.VramGigabytes);
        Assert.AreEqual(24576u, vramInfo.DedicatedVramMb);

        var vramZero = new GpuHardwareInfo { VramBytes = 0 };
        Assert.AreEqual(0.0, vramZero.VramGigabytes);
        Assert.AreEqual(0u, vramZero.DedicatedVramMb);
    }

    #endregion

    #region 2. Anti-Ghosting Keys Empirical Stress Tests

    [TestMethod]
    public async Task StressTest_AntiGhostingShield_All11Keys_UniversalBridge()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_challenger_antighost_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "GenericDx12Game.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "challenger-antighost-id",
                "Universal Anti-Ghosting Shield Game",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute, "Plan must be executable.");

            // Helper to lookup config entry
            string GetVal(string section, string key)
            {
                var match = plan.PlannedConfigModifications.FirstOrDefault(m =>
                    string.Equals(m.Section, section, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(match, $"Required anti-ghosting config [{section}] {key} was not found!");
                return match.Value;
            }

            // Key 1: AccAddPerFrame = 0.05 (Disocclusion accumulation flush)
            var accAdd = GetVal("FSR", "AccAddPerFrame");
            Assert.AreEqual("0.05", accAdd);

            // Key 2: MinDisOccAcc = -0.50 (Disocclusion delay preventing premature history blending)
            var minDisOcc = GetVal("FSR", "MinDisOccAcc");
            Assert.IsTrue(float.TryParse(minDisOcc, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fMinDisOcc), "MinDisOccAcc must be float");
            Assert.AreEqual(-0.50f, fMinDisOcc, 0.001f);
            Assert.IsTrue(minDisOcc == "-0.5" || minDisOcc == "-0.50");

            // Key 3: DisableReactiveMask = false (Preserves native responsive pixel masks)
            var disableReactive = GetVal("InitFlags", "DisableReactiveMask");
            Assert.AreEqual("false", disableReactive);

            // Key 4: MakeMVCopy = true (Eliminates DX12 async compute race conditions on motion vectors)
            var mvCopy = GetVal("OptiFG", "MakeMVCopy");
            Assert.AreEqual("true", mvCopy);

            // Key 5: MakeDepthCopy = true (Eliminates DX12 async compute race conditions on depth)
            var depthCopy = GetVal("OptiFG", "MakeDepthCopy");
            Assert.AreEqual("true", depthCopy);

            // Key 6: DlssReactiveMaskBias = 0.45 (Optimal reactive mask bias)
            var maskBias = GetVal("FSR", "DlssReactiveMaskBias");
            Assert.AreEqual("0.45", maskBias);

            // Key 7: ReactiveScale = 2.0 (Calibrated reactive scale for high-motion silhouettes)
            var reactiveScale = GetVal("FSR", "ReactiveScale");
            Assert.AreEqual("2.0", reactiveScale);

            // Key 8: RenderPresetForAll = 5 (Forces DLSS Preset E - anti-ghosting champion)
            var presetForAll = GetVal("DLSS", "RenderPresetForAll");
            Assert.AreEqual("5", presetForAll);

            // Key 9: HUDFix = true (Prevents HUD smearing and crosshair trails)
            var hudFix = GetVal("OptiFG", "HUDFix");
            Assert.AreEqual("true", hudFix);

            // Key 10: HUDFixExtended = true (Extended coverage for crosshairs and subtitles)
            var hudFixExt = GetVal("OptiFG", "HUDFixExtended");
            Assert.AreEqual("true", hudFixExt);

            // Key 11: DisableHUDFix = false (Ensures HUD tracking is fully enabled)
            var disableHudFix = GetVal("OptiFG", "DisableHUDFix");
            Assert.AreEqual("false", disableHudFix);

            // Ancillary presentation invariants
            Assert.AreEqual("true", GetVal("FrameGen", "DrawUIOverFG"));
            Assert.AreEqual("false", GetVal("FrameGen", "DisableUI"));
            Assert.AreEqual("false", GetVal("FrameGen", "DisableHudless"));
            Assert.AreEqual("true", GetVal("FrameGen", "ForceBorderless"));
            Assert.AreEqual("true", GetVal("FSR", "UseReactiveMaskForTransparency"));
            Assert.AreEqual("true", GetVal("DLSS", "RenderPresetOverride"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region 3. Forza Horizon 5 Deployment Stress Tests

    [TestMethod]
    public async Task StressTest_ForzaHorizon5_DlssWithFrameGen_ProxyAndHeapTrackingAndNoMipmapBias()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_challenger_fh5_fg_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon5.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "INTERPOSER_DLL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "DLSSG_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh5-stress-test",
                "Forza Horizon 5",
                tempDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute, "Forza Horizon 5 plan must be executable.");

            // 1. Proxy must default to version.dll for Streamline
            Assert.AreEqual("version.dll", plan.SelectedProxy, "Forza Horizon 5 MUST default to version.dll proxy.");

            // 2. AlwaysTrackHeaps must be FALSE to avoid virtual texture streaming pool starvation
            var heapTracking = plan.PlannedConfigModifications.FirstOrDefault(m =>
                string.Equals(m.Section, "OptiFG", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Key, "AlwaysTrackHeaps", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(heapTracking, "AlwaysTrackHeaps must be explicitly configured.");
            Assert.AreEqual("false", heapTracking.Value, "AlwaysTrackHeaps MUST be false for Forza Horizon 5.");

            // 3. Native Streamline Passthrough flags
            var hudFix = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix");
            Assert.AreEqual("false", hudFix.Value);

            var disableHudFix = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "DisableHUDFix");
            Assert.AreEqual("true", disableHudFix.Value);

            var depthCopy = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeDepthCopy");
            Assert.AreEqual("false", depthCopy.Value);

            var mvCopy = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "MakeMVCopy");
            Assert.AreEqual("false", mvCopy.Value);

            // 4. Mipmap and Anisotropy overrides MUST NOT be present
            var mipmapEntry = plan.PlannedConfigModifications.FirstOrDefault(m =>
                string.Equals(m.Section, "Mipmap", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Key, "MipmapBiasOverride", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(mipmapEntry, "Forza Horizon 5 must NOT have MipmapBiasOverride injected.");

            var anisoEntry = plan.PlannedConfigModifications.FirstOrDefault(m =>
                string.Equals(m.Section, "Anisotropy", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(m.Key, "AnisotropyOverride", StringComparison.OrdinalIgnoreCase));
            Assert.IsNull(anisoEntry, "Forza Horizon 5 must NOT have AnisotropyOverride injected.");

            // 5. Anti-ghosting parameters MUST be present in FSR/DLSS sections
            Assert.AreEqual("0.05", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "AccAddPerFrame").Value);
            Assert.AreEqual("-0.5", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "MinDisOccAcc").Value);
            Assert.AreEqual("2.0", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "ReactiveScale").Value);
            Assert.AreEqual("0.45", plan.PlannedConfigModifications.First(m => m.Section == "FSR" && m.Key == "DlssReactiveMaskBias").Value);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "InitFlags" && m.Key == "DisableReactiveMask").Value);
            Assert.AreEqual("5", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task StressTest_ForzaHorizon5_TurboBoost_AvoidsMipmapAnisotropyAndDisablesHeapTracking()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_challenger_fh5_boost_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon5.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "nvngx_dlssg.dll"), "DLSSG_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh5-boost-stress-test",
                "Forza Horizon 5",
                tempDir,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("version.dll", plan.SelectedProxy, "TurboBoost on Forza Horizon 5 must select version.dll proxy.");

            // Verify heap tracking disabled
            var heapTracking = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps");
            Assert.AreEqual("false", heapTracking.Value);

            // Verify HUD fix disabled
            var hudFix = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix");
            Assert.AreEqual("false", hudFix.Value);

            // Verify FrameGen bridge enabled
            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "Enabled").Value);
            Assert.AreEqual("dlssg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
            Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);

            // MipmapBias and Anisotropy MUST be omitted for Forza
            Assert.IsFalse(plan.PlannedConfigModifications.Any(m => m.Section == "Mipmap" && m.Key == "MipmapBiasOverride"), "TurboBoost must not set MipmapBiasOverride on Forza");
            Assert.IsFalse(plan.PlannedConfigModifications.Any(m => m.Section == "Anisotropy" && m.Key == "AnisotropyOverride"), "TurboBoost must not set AnisotropyOverride on Forza");

            // Preset E and RCAS Sharpness remain active
            Assert.AreEqual("5", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
            Assert.AreEqual("0.35", plan.PlannedConfigModifications.First(m => m.Section == "Sharpness" && m.Key == "Sharpness").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void StressTest_ForzaTitleVariations_AlwaysTriggerVirtualTextureSafeguards()
    {
        string[] variations =
        [
            "Forza Horizon 5",
            "FORZA HORIZON 5",
            "forza horizon 5: premium edition",
            "Forza Horizon 5 - Rally Adventure",
            "Forza Horizon 4",
            "Forza Motorsport",
            "FORZA MOTORSPORT 8",
            "ForzaTech Demo Title"
        ];

        foreach (var title in variations)
        {
            bool isForza = DeploymentPlanner.IsForzaOrVirtualTextureTitle(title);
            Assert.IsTrue(isForza, $"Expected title '{title}' to be recognized as Forza / virtual texture title.");
        }

        // Titles that should NOT trigger Forza virtual texture safeguards
        string[] nonForza =
        [
            "Generic DX12 Shooter",
            "Apex Legends",
            "Doom Eternal",
            "Red Dead Redemption 2"
        ];

        foreach (var title in nonForza)
        {
            bool isForza = DeploymentPlanner.IsForzaOrVirtualTextureTitle(title);
            Assert.IsFalse(isForza, $"Title '{title}' should not be recognized as Forza.");
        }
    }

    #endregion

    #region 4. Multi-Backend Routing Stress Tests

    [TestMethod]
    public void StressTest_MultiBackendRouting_AllAliasesAndPrecedence()
    {
        // Default
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput());

        // Nukem aliases
        Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("nukem"));
        Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("nukems"));
        Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("nukem9"));
        Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("NUKEM9"));

        // Intel XeFG aliases
        Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput("xefg"));
        Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput("xess"));
        Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput("intel"));
        Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput("XEFG"));

        // AMD FSR aliases
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("fsr"));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("fsrfg"));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("fsr3"));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("fsr31"));
        Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("amd"));

        // Precedence: explicit requested argument over static OverrideFgOutput
        try
        {
            DeploymentPlanner.OverrideFgOutput = "xefg";
            Assert.AreEqual("xefg", DeploymentPlanner.ResolveFgOutput());
            Assert.AreEqual("nukems", DeploymentPlanner.ResolveFgOutput("nukems"), "Explicit parameter must override static OverrideFgOutput.");
            Assert.AreEqual("fsrfg", DeploymentPlanner.ResolveFgOutput("fsrfg"), "Explicit parameter must override static OverrideFgOutput.");
        }
        finally
        {
            DeploymentPlanner.OverrideFgOutput = null;
        }
    }

    [TestMethod]
    public async Task StressTest_ConcurrentPlanGeneration_AsyncLocalIsolation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_challenger_concurrency_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ConcurrentGame.exe"), "EXE_CONTENT");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12_DLL");

            var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
            Assert.IsNotNull(pack);

            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    string targetBackend = (index % 3) switch
                    {
                        0 => "fsrfg",
                        1 => "nukems",
                        _ => "xefg"
                    };

                    DeploymentPlanner.OverrideFgOutput = targetBackend;

                    var plan = await DeploymentPlanner.CreatePlanAsync(
                        $"concurrency-game-{index}",
                        $"Concurrency Game {index}",
                        tempDir,
                        pack,
                        RuntimeIntegrationPreset.DlssWithFrameGeneration);

                    Assert.IsTrue(plan.CanExecute);
                    var fgOutput = plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value;
                    Assert.AreEqual(targetBackend, fgOutput, $"Task {index} suffered cross-thread contamination! Expected {targetBackend}, got {fgOutput}");
                }));
            }

            await Task.WhenAll(tasks);
        }
        finally
        {
            DeploymentPlanner.OverrideFgOutput = null;
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region 5. Negative and Boundary Failure Modes

    [TestMethod]
    public async Task StressTest_DeploymentPlanner_ThrowsOnInvalidArguments()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
        Assert.IsNotNull(pack);

        bool threw1 = false;
        try
        {
            await DeploymentPlanner.CreatePlanAsync("", "Title", "C:\\valid", pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);
        }
        catch (ArgumentException)
        {
            threw1 = true;
        }
        Assert.IsTrue(threw1, "Expected ArgumentException for empty gameId");

        bool threw2 = false;
        try
        {
            await DeploymentPlanner.CreatePlanAsync("game-id", "Title", "  ", pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);
        }
        catch (ArgumentException)
        {
            threw2 = true;
        }
        Assert.IsTrue(threw2, "Expected ArgumentException for whitespace gameRoot");

        bool threw3 = false;
        try
        {
            await DeploymentPlanner.CreatePlanAsync("game-id", "Title", "C:\\valid", null!, RuntimeIntegrationPreset.DlssWithFrameGeneration);
        }
        catch (ArgumentNullException)
        {
            threw3 = true;
        }
        Assert.IsTrue(threw3, "Expected ArgumentNullException for null pack");
    }

    [TestMethod]
    public async Task StressTest_DeploymentPlanner_BlocksWhenDirectoryOrExeMissing()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(GetBundledPackagePath());
        Assert.IsNotNull(pack);

        // Nonexistent directory
        var fakePath = Path.Combine(Path.GetTempPath(), $"xtr_nonexistent_{Guid.NewGuid():N}");
        var planNoDir = await DeploymentPlanner.CreatePlanAsync(
            "fake-id",
            "Fake Game",
            fakePath,
            pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration);

        Assert.IsFalse(planNoDir.CanExecute, "Plan must not execute when directory does not exist.");
        Assert.AreEqual(PreflightStatus.Blocked, planNoDir.PreflightResult.Status);

        // Directory exists but has no exe
        var emptyDir = Path.Combine(Path.GetTempPath(), $"xtr_empty_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(emptyDir);
            var planNoExe = await DeploymentPlanner.CreatePlanAsync(
                "empty-id",
                "Empty Game",
                emptyDir,
                pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsFalse(planNoExe.CanExecute, "Plan must not execute when no executable is found.");
            Assert.AreEqual(PreflightStatus.Blocked, planNoExe.PreflightResult.Status);
        }
        finally
        {
            if (Directory.Exists(emptyDir))
                Directory.Delete(emptyDir, recursive: true);
        }
    }

    #endregion
}
