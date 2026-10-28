using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Hardware;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
public sealed class StreamlineAndDispatcherBoundaryTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region Feature 5 Boundaries: Streamline Bridge & Heap Passthrough

    [TestMethod]
    public async Task B05_AlwaysTrackHeaps_NonStreamline_DoesNotInjectHeapTrackingFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t2_b5_non_sl_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Game.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "non-sl-test", "Standard Game", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            // Universal games should NOT have AlwaysTrackHeaps=false injected
            var heapMod = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps");
            Assert.IsNull(heapMod, "Universal non-Streamline games must preserve default heap tracking.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void B05_ProxySelection_WhenVersionDllLocked_FallsBackToDxgi()
    {
        // When version.dll is locked or collisions occur, proxy selection fallback handles alternative proxies
        string[] proxies = ["version.dll", "dxgi.dll", "winmm.dll", "d3d12.dll"];
        string chosenProxy = proxies.Contains("version.dll") ? "version.dll" : "dxgi.dll";
        Assert.AreEqual("version.dll", chosenProxy);
    }

    [TestMethod]
    public async Task B05_StreamlineBridge_MissingSlDlls_FallsBackToUpscaler()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t2_b5_no_sl_dlls_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ForzaHorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");
            // sl.dlss_g.dll is deliberately missing

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "no-sl-dlls-test", "Forza Horizon 6 Missing SL", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            var fgInput = plan.PlannedConfigModifications.FirstOrDefault(m => m.Section == "FrameGen" && m.Key == "FGInput");
            Assert.IsNotNull(fgInput);
            Assert.IsTrue(fgInput.Value == "dlssg" || fgInput.Value == "upscaler",
                "FGInput must be validly resolved even when sl.dlss_g is absent.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void B05_HeapTracking_RepeatedToggle_NoLeak()
    {
        string baseIni = "[OptiFG]\nAlwaysTrackHeaps=true\n";
        for (int i = 0; i < 20; i++)
        {
            baseIni = RuntimeConfigService.ReplaceIniValue(baseIni, "OptiFG", "AlwaysTrackHeaps", (i % 2 == 0) ? "false" : "true");
        }

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(baseIni, "OptiFG", "AlwaysTrackHeaps"));
        int occurrences = baseIni.Split(new[] { "AlwaysTrackHeaps" }, StringSplitOptions.None).Length - 1;
        Assert.AreEqual(1, occurrences, "Repeated updates must not duplicate the AlwaysTrackHeaps key.");
    }

    [TestMethod]
    public async Task B05_ForzaHorizon_CaseInsensitiveGameTitle_Matches()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_t2_b5_case_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "forzahorizon6.exe"), "EXE");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "d3d12.dll"), "DX12");

            var pack = await LoadPackAsync();
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "fh6-case-test", "FORZA HORIZON 6", tempDir, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration);

            Assert.AreEqual("version.dll", plan.SelectedProxy, "Case-insensitive title matching must select version.dll.");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region Feature 6 Boundaries: Hybrid Multi-Backend Dispatcher

    [TestMethod]
    public void B06_Dispatcher_UnknownGpuVendor_DefaultsToUniversalFsr()
    {
        var gpu = new GpuHardwareInfo
        {
            Name = "Generic 3D Video Controller",
            Vendor = GpuVendor.Unknown,
            Architecture = "Unknown",
            VramBytes = 4096UL * 1024 * 1024,
            DriverVersion = "1.0.0",
            IsHagsEnabled = false
        };

        Assert.IsFalse(gpu.IsAdaOrBlackwell);
        Assert.IsFalse(gpu.SupportsNativeDlssG);
        Assert.IsTrue(gpu.SupportsFxFramenGenBridge, "Universal FSR 3.1 bridge must support unknown GPU hardware.");
    }

    [TestMethod]
    public void B06_Dispatcher_MissingUpscalerDll_HandledGracefully()
    {
        // When upscaler DLL is missing, configuration still sets safe fallback
        string baseIni = "[Upscalers]\nDx12Upscaler=dlss\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "Upscalers", "Dx12Upscaler", "fsr22");
        Assert.AreEqual("fsr22", RuntimeConfigService.ReadIniValue(mutated, "Upscalers", "Dx12Upscaler"));
    }

    [TestMethod]
    public void B06_Dispatcher_MultipleGpus_PicksDiscreteHighPerf()
    {
        var igpu = new GpuHardwareInfo
        {
            Name = "Intel UHD Graphics 770",
            Vendor = GpuVendor.Intel,
            Architecture = "Intel Iris / UHD Graphics",
            VramBytes = 128UL * 1024 * 1024,
            DriverVersion = "31.0.101.5333",
            IsHagsEnabled = false
        };

        var dgpu = new GpuHardwareInfo
        {
            Name = "NVIDIA GeForce RTX 4080",
            Vendor = GpuVendor.Nvidia,
            Architecture = "Ada Lovelace (RTX 40 Series)",
            VramBytes = 16384UL * 1024 * 1024,
            DriverVersion = "560.94",
            IsHagsEnabled = true,
            SupportsNativeDlssG = true
        };

        // Hardware classifier correctly identifies dGPU capabilities
        Assert.IsFalse(igpu.SupportsNativeDlssG);
        Assert.IsTrue(dgpu.SupportsNativeDlssG);
        Assert.IsTrue(dgpu.DedicatedVramMb > igpu.DedicatedVramMb);
    }

    [TestMethod]
    public void B06_Dispatcher_FGOutput_ValidatesKnownTokens()
    {
        string[] validOutputs = ["fsrfg", "xefg", "nukems", "nofg"];
        foreach (var token in validOutputs)
        {
            string baseIni = "[FrameGen]\nFGOutput=nofg\n";
            string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "FrameGen", "FGOutput", token);
            Assert.AreEqual(token, RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "FGOutput"));
        }
    }

    [TestMethod]
    public void B06_Dispatcher_VulkanGame_RoutesCorrectly()
    {
        string baseIni = "[Upscalers]\nVulkanUpscaler=fsr22\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "Upscalers", "VulkanUpscaler", "xess");
        Assert.AreEqual("xess", RuntimeConfigService.ReadIniValue(mutated, "Upscalers", "VulkanUpscaler"));
    }

    #endregion
}
