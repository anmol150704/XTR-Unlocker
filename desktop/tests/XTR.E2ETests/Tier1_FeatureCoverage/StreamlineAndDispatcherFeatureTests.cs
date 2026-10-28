using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
public sealed class StreamlineAndDispatcherFeatureTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region Feature 5: Streamline Bridge & Heap Passthrough (AlwaysTrackHeaps=false, version.dll)

    [TestMethod]
    public async Task F05_ForzaHorizon6_SelectsVersionDllProxy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f5_fh6_proxy_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "DLSSG");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh6-proxy-test", "Forza Horizon 6", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.IsTrue(plan.CanExecute);
            Assert.AreEqual("version.dll", plan.SelectedProxy, "Forza Horizon 6 must default to version.dll proxy.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task F05_ForzaHorizon6_DisablesHeapTracking()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f5_fh6_heaps_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh6-heaps-test", "Forza Horizon 6", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            var trackHeaps = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps");
            Assert.IsNotNull(trackHeaps, "AlwaysTrackHeaps must be in planned modifications.");
            Assert.AreEqual("false", trackHeaps.Value, "AlwaysTrackHeaps must be false to prevent virtual texture pool exhaustion.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task F05_ForzaHorizon6_DisablesHUDFix_ForStreamlinePassthrough()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f5_fh6_hudfix_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh6-hudfix-test", "Forza Horizon 6", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            var hudFix = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "HUDFix");
            var disableHudFix = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "DisableHUDFix");

            Assert.AreEqual("false", hudFix.Value);
            Assert.AreEqual("true", disableHudFix.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task F05_ForzaHorizon5_Detection_ConfiguresStreamlinePassthrough()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f5_fh5_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon5.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh5-test", "Forza Horizon 5", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("version.dll", plan.SelectedProxy, "Forza Horizon 5 must select version.dll proxy.");
            var trackHeaps = plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps");
            Assert.AreEqual("false", trackHeaps.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task F05_Cyberpunk_StreamlineInterposer_Detected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f5_cp_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Cyberpunk2077.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "DLSSG");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "cp-test", "Cyberpunk 2077", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            var fgInput = plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.AreEqual("dlssg", fgInput.Value, "Streamline DLSSG in Cyberpunk 2077 must configure FGInput=dlssg.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Feature 6: Hybrid Multi-Backend Dispatcher (Nukem9, FSR 3.1, XeFG)

    [TestMethod]
    public async Task F06_Dispatcher_StreamlineDetected_SelectsDlssgInputAndFsrfgOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f6_sl_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.interposer.dll"), "SL_INTERPOSER");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "DLSSG");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "disp-sl-test", "Streamline Game", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("dlssg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
            Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task F06_Dispatcher_NoStreamline_SelectsUpscalerInput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f6_nosl_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "disp-nosl-test", "Generic DX12 Game", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("upscaler", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
            Assert.AreEqual("fsrfg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public async Task F06_Dispatcher_NativeHardware_SelectsNoFgBypass()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f6_native_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "DLSSG");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "disp-native-test", "Native DLSS 3 Game", tempDir, pack, RuntimeIntegrationPreset.NativeDlssG);

            Assert.AreEqual("nofg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGInput").Value);
            Assert.AreEqual("nofg", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
            Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "Enabled").Value);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void F06_Dispatcher_XeFgFallback_ConfiguresXeFgOutput()
    {
        string baseIni = "[FrameGen]\nEnabled=true\nFGInput=upscaler\nFGOutput=fsrfg\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "FrameGen", "FGOutput", "xefg");

        Assert.AreEqual("xefg", RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "FGOutput"),
            "Dispatcher must support routing output to Intel XeFG provider.");
    }

    [TestMethod]
    public async Task F06_Dispatcher_Spoofing_ConfiguresStreamlineSpoofing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t1_f6_spoof_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "sl.dlss_g.dll"), "DLSSG");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "disp-spoof-test", "Cyberpunk 2077", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            var spoofMod = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "Spoofing" && m.Key == "StreamlineSpoofing");
            Assert.IsNotNull(spoofMod, "StreamlineSpoofing must be configured.");
            Assert.AreEqual("true", spoofMod.Value, "StreamlineSpoofing must be true to unlock DLSS-G on RTX 20/30 cards.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    #endregion
}
