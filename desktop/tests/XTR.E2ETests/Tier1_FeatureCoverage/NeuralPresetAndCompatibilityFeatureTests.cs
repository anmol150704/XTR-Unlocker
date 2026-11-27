using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Hardware;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
[DoNotParallelize]
public sealed class NeuralPresetAndCompatibilityFeatureTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F07: Neural DLSS 4/4.5 Preset Selection & 4GB VRAM Guardrails

    [TestMethod]
    public async Task F07_01_AdaAndBlackwellGpus_SelectPresetM_13()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f07_ada");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        foreach (var gpu in new[] { IsolatedGameSandbox.CreateRtx4070(), IsolatedGameSandbox.CreateRtx5090() })
        {
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "f07-ada", "Standard Game", sandbox.GameRoot, pack,
                RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: gpu);

            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetOverride").Value);
            Assert.AreEqual("13", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
        }
    }

    [TestMethod]
    public async Task F07_02_TuringAndAmpereGpus_SelectPresetK_11()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f07_ampere");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        foreach (var gpu in new[] { IsolatedGameSandbox.CreateRtx2050Mobile4Gb(), IsolatedGameSandbox.CreateRtx3080() })
        {
            var plan = await DeploymentPlanner.CreatePlanAsync(
                "f07-ampere", "Standard Game", sandbox.GameRoot, pack,
                RuntimeIntegrationPreset.TurboBoost, gpuInfo: gpu);

            Assert.AreEqual("true", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetOverride").Value);
            Assert.AreEqual("11", plan.PlannedConfigModifications.First(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll").Value);
        }
    }

    [TestMethod]
    public async Task F07_03_FourGbVramGuardrail_DisablesAlwaysTrackHeapsOnUniversalPipeline()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f07_4gb");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var plan4Gb = await DeploymentPlanner.CreatePlanAsync(
            "f07-4gb", "Universal Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx2050Mobile4Gb());

        var plan10Gb = await DeploymentPlanner.CreatePlanAsync(
            "f07-10gb", "Universal Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080());

        var heap4Gb = plan4Gb.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps");
        var heap10Gb = plan10Gb.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps");

        Assert.IsNotNull(heap4Gb, "4GB VRAM GPU must trigger AlwaysTrackHeaps=false guardrail.");
        Assert.AreEqual("false", heap4Gb.Value);
        Assert.IsNull(heap10Gb, "10GB GPU on non-Streamline title must preserve default heap tracking.");
    }

    [TestMethod]
    public async Task F07_04_NonNvidiaGpu_SelectsFsr31InTurboBoostAndOmitsDlssPreset()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f07_amd");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var amdPlan = await DeploymentPlanner.CreatePlanAsync(
            "f07-amd", "Universal Game", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.TurboBoost,
            gpuInfo: IsolatedGameSandbox.CreateAmdRx7800Xt());

        Assert.AreEqual("fsr31", amdPlan.PlannedConfigModifications.First(m => m.Section == "Upscalers" && m.Key == "Dx12Upscaler").Value);
        Assert.IsFalse(amdPlan.PlannedConfigModifications.Any(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll"),
            "Non-NVIDIA GPUs must not force NVIDIA DLSS neural preset overrides.");
    }

    [TestMethod]
    public void F07_05_GpuClassifier_IdentifiesAllMajorArchitectures()
    {
        if (!OperatingSystem.IsWindows()) return;

        var rtx5090 = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 5090", VramBytes = 32768UL * 1024 * 1024 };
        var rtx4090 = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 4090", VramBytes = 24576UL * 1024 * 1024 };
        var rtx2050 = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 2050", VramBytes = 4096UL * 1024 * 1024 };
        var rx7800 = new GpuHardwareInfo { Name = "AMD Radeon RX 7800 XT", VramBytes = 16384UL * 1024 * 1024 };
        var arcB580 = new GpuHardwareInfo { Name = "Intel(R) Arc(TM) B580 Graphics", VramBytes = 12288UL * 1024 * 1024 };

        GpuHardwareDetector.ClassifyGpu(rtx5090);
        GpuHardwareDetector.ClassifyGpu(rtx4090);
        GpuHardwareDetector.ClassifyGpu(rtx2050);
        GpuHardwareDetector.ClassifyGpu(rx7800);
        GpuHardwareDetector.ClassifyGpu(arcB580);

        Assert.IsTrue(rtx5090.IsAdaOrBlackwell && rtx5090.SupportsMultiFrameGen && rtx5090.RecommendedDlssPresetId == 13);
        Assert.IsTrue(rtx4090.IsAdaOrBlackwell && rtx4090.SupportsNativeDlssG && rtx4090.RecommendedDlssPresetId == 13);
        Assert.AreEqual(GpuVendor.Nvidia, rtx2050.Vendor);
        Assert.IsFalse(rtx2050.SupportsNativeDlssG);
        Assert.AreEqual(11, rtx2050.RecommendedDlssPresetId);
        Assert.AreEqual(GpuVendor.Amd, rx7800.Vendor);
        Assert.AreEqual(GpuVendor.Intel, arcB580.Vendor);
    }

    #endregion

    #region F08: Compatibility Database (37+ Profiles) & Anti-Cheat Guard + Offline Override

    [TestMethod]
    public void F08_01_CompatibilityDatabase_ContainsAtLeast37ValidProfiles()
    {
        Assert.IsTrue(CompatibilityDatabase.RecordCount >= 37,
            $"Expected >= 37 profiles, got {CompatibilityDatabase.RecordCount}.");

        var records = CompatibilityDatabase.GetAllRecords();
        Assert.AreEqual(CompatibilityDatabase.RecordCount, records.Count);
        foreach (var record in records)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.Title));
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.RecommendedProxy));
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.RecommendedApi));
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.Notes));
        }
    }

    [TestMethod]
    public void F08_02_ForzaHorizon5And6_RecommendVersionDllAndNativeFg()
    {
        var fh5 = CompatibilityDatabase.LookupGame("Forza Horizon 5");
        var fh6 = CompatibilityDatabase.LookupGame("Forza Horizon 6");

        Assert.IsNotNull(fh5);
        Assert.IsNotNull(fh6);
        Assert.AreEqual(CompatibilityLevel.VerifiedCompatible, fh5.Level);
        Assert.AreEqual(CompatibilityLevel.VerifiedCompatible, fh6.Level);
        Assert.AreEqual("version.dll", fh5.RecommendedProxy);
        Assert.AreEqual("version.dll", fh6.RecommendedProxy);
        Assert.IsTrue(fh5.SupportsNativeFrameGen);
        Assert.IsTrue(fh6.SupportsNativeFrameGen);
    }

    [TestMethod]
    public async Task F08_03_MultiplayerBlockedTitles_BlockedByPreflightAndPlanner()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f08_mpblock");
        sandbox.CreateFile("Game.exe", "EXE");

        var pack = await LoadPackAsync();
        foreach (var title in new[] { "VALORANT", "Apex Legends", "Fortnite", "Call of Duty: Warzone" })
        {
            Assert.IsTrue(CompatibilityDatabase.IsMultiplayerBlocked(title), $"{title} must be MultiplayerBlocked.");
            var preflight = await RuntimePreflightService.RunPreflightAsync(title, sandbox.GameRoot, pack, gameTitle: title);
            Assert.AreEqual(PreflightStatus.Blocked, preflight.Status);
            Assert.IsFalse(preflight.CanProceed);

            var plan = await DeploymentPlanner.CreatePlanAsync(
                title, title, sandbox.GameRoot, pack,
                RuntimeIntegrationPreset.LatestDlssOnly,
                gpuInfo: IsolatedGameSandbox.CreateRtx4070());
            Assert.IsFalse(plan.CanExecute);
        }
    }

    [TestMethod]
    public async Task F08_04_OnDiskAntiCheatBinary_BlocksUnknownTitleByDefault()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f08_eac");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("EasyAntiCheat/EasyAntiCheat_EOS.exe", "EAC_EOS");

        var pack = await LoadPackAsync();
        var preflight = await RuntimePreflightService.RunPreflightAsync(
            "f08-eac", sandbox.GameRoot, pack, gameTitle: "Unknown Shooter");

        Assert.IsTrue(preflight.HasAntiCheatRisk);
        Assert.IsNotNull(preflight.AntiCheatBinary);
        Assert.AreEqual(PreflightStatus.Blocked, preflight.Status);
        Assert.IsFalse(preflight.CanProceed);
    }

    [TestMethod]
    public async Task F08_05_OfflineAntiCheatOverride_PermitsCommunityTestedGameWithCaution()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f08_offline");
        sandbox.CreateFile("eldenring.exe", "ER_EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("EasyAntiCheat/EasyAntiCheat.exe", "EAC");

        var pack = await LoadPackAsync();
        var blocked = await RuntimePreflightService.RunPreflightAsync(
            "elden-ring", sandbox.GameRoot, pack, gameTitle: "Elden Ring", allowOfflineAntiCheatOverride: false);
        Assert.IsFalse(blocked.CanProceed);
        Assert.AreEqual(PreflightStatus.Blocked, blocked.Status);

        var allowed = await RuntimePreflightService.RunPreflightAsync(
            "elden-ring", sandbox.GameRoot, pack, gameTitle: "Elden Ring", allowOfflineAntiCheatOverride: true);
        Assert.IsTrue(allowed.CanProceed);
        Assert.AreEqual(PreflightStatus.NeedsAttention, allowed.Status);

        var plan = await DeploymentPlanner.CreatePlanAsync(
            "elden-ring", "Elden Ring", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx3080(),
            allowOfflineAntiCheatOverride: true);
        Assert.IsTrue(plan.CanExecute);
    }

    #endregion
}
