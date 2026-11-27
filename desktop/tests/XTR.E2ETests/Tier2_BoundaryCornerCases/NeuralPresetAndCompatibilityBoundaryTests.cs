using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Hardware;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
[DoNotParallelize]
public sealed class NeuralPresetAndCompatibilityBoundaryTests
{
    private static async Task<RuntimePack> LoadPackAsync()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        return pack;
    }

    #region F07 Boundaries: Neural Presets & 4GB VRAM Guardrails

    [TestMethod]
    public async Task B07_VramGuardrail_ExactBoundaryAt4608MbVs4609Mb()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b07_vrambound");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var pack = await LoadPackAsync();
        var atBoundary = new GpuHardwareInfo
        {
            Name = "GPU 4608MB",
            Vendor = GpuVendor.Nvidia,
            Architecture = "Ampere",
            VramBytes = 4608UL * 1024 * 1024,
            RecommendedDlssPresetId = 11
        };
        var aboveBoundary = new GpuHardwareInfo
        {
            Name = "GPU 4609MB",
            Vendor = GpuVendor.Nvidia,
            Architecture = "Ampere",
            VramBytes = 4609UL * 1024 * 1024,
            RecommendedDlssPresetId = 11
        };

        var planAt = await DeploymentPlanner.CreatePlanAsync(
            "b07-at", "Generic Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: atBoundary);
        var planAbove = await DeploymentPlanner.CreatePlanAsync(
            "b07-above", "Generic Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: aboveBoundary);

        Assert.AreEqual("false", planAt.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
        Assert.IsNull(planAbove.PlannedConfigModifications.FirstOrDefault(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps"));
    }

    [TestMethod]
    public async Task B07_Gtx10And16Series_ZeroPresetId_OmitsRenderPresetOverride()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var sandbox = new IsolatedGameSandbox("t2_b07_gtx");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var gtx1660 = new GpuHardwareInfo { Name = "NVIDIA GeForce GTX 1660", VramBytes = 6144UL * 1024 * 1024 };
        GpuHardwareDetector.ClassifyGpu(gtx1660);
        Assert.AreEqual(0, gtx1660.RecommendedDlssPresetId);

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b07-gtx", "Generic Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: gtx1660);

        Assert.IsFalse(plan.PlannedConfigModifications.Any(m => m.Section == "DLSS" && m.Key == "RenderPresetForAll"));
    }

    [TestMethod]
    public void B07_Rtx2050Mobile_ClassifiedAsAmpereNotTuring()
    {
        if (!OperatingSystem.IsWindows()) return;

        var rtx2050 = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 2050 Laptop GPU", VramBytes = 4096UL * 1024 * 1024 };
        GpuHardwareDetector.ClassifyGpu(rtx2050);

        Assert.IsTrue(rtx2050.Architecture.Contains("Ampere", System.StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(11, rtx2050.RecommendedDlssPresetId);
        Assert.IsFalse(rtx2050.SupportsNativeDlssG);
    }

    [TestMethod]
    public async Task B07_UnknownZeroVramGpu_Triggers4GbGuardrailSafely()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b07_zerovram");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var unknown = new GpuHardwareInfo { Name = "Basic Adapter", Vendor = GpuVendor.Unknown, VramBytes = 0 };
        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "b07-zero", "Generic Game", sandbox.GameRoot, pack, RuntimeIntegrationPreset.DlssWithFrameGeneration, gpuInfo: unknown);

        Assert.IsTrue(unknown.SupportsFxFramenGenBridge);
        Assert.AreEqual("false", plan.PlannedConfigModifications.First(m => m.Section == "OptiFG" && m.Key == "AlwaysTrackHeaps").Value);
    }

    [TestMethod]
    public async Task B07_TurboBoostAdaWithNativeDlssG_ForzaExceptionUsesBridgeWhileCyberpunkBypasses()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b07_forzaada");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        sandbox.CreateFile("sl.interposer.dll", "SL");
        sandbox.CreateFile("sl.dlss_g.dll", "SL_DLSSG");

        var pack = await LoadPackAsync();
        var rtx4070 = IsolatedGameSandbox.CreateRtx4070();

        var cpPlan = await DeploymentPlanner.CreatePlanAsync(
            "b07-cp", "Cyberpunk 2077", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: rtx4070);
        var fh6Plan = await DeploymentPlanner.CreatePlanAsync(
            "b07-fh6", "Forza Horizon 6", sandbox.GameRoot, pack, RuntimeIntegrationPreset.TurboBoost, gpuInfo: rtx4070);

        Assert.AreEqual("nofg", cpPlan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "FGOutput").Value);
        Assert.AreEqual("true", fh6Plan.PlannedConfigModifications.First(m => m.Section == "FrameGen" && m.Key == "Enabled").Value);
        Assert.AreEqual("version.dll", fh6Plan.SelectedProxy);
    }

    #endregion

    #region F08 Boundaries: Compatibility Database & Anti-Cheat

    [TestMethod]
    public void B08_CompatibilityDatabase_NullWhitespaceAndUnknown_ReturnNull()
    {
        Assert.IsNull(CompatibilityDatabase.LookupGame(null!));
        Assert.IsNull(CompatibilityDatabase.LookupGame(string.Empty));
        Assert.IsNull(CompatibilityDatabase.LookupGame("   "));
        Assert.IsNull(CompatibilityDatabase.LookupGame("NonExistentSyntheticTitle_XYZ_999"));
        Assert.IsFalse(CompatibilityDatabase.IsMultiplayerBlocked("NonExistentSyntheticTitle_XYZ_999"));
    }

    [TestMethod]
    public void B08_CompatibilityDatabase_CaseInsensitiveAndSubstringLookup_Matches()
    {
        var cp = CompatibilityDatabase.LookupGame("cyberpunk 2077: phantom liberty");
        var fh6 = CompatibilityDatabase.LookupGame("FORZA HORIZON 6 STEAM EDITION");

        Assert.IsNotNull(cp);
        Assert.IsNotNull(fh6);
        Assert.AreEqual("dxgi.dll", cp.RecommendedProxy);
        Assert.AreEqual("version.dll", fh6.RecommendedProxy);
    }

    [TestMethod]
    public void B08_CompatibilityDatabase_AllMultiplayerBlockedHaveProxyNoneAndNoNativeFg()
    {
        var blocked = CompatibilityDatabase.GetAllRecords()
            .Where(r => r.Level == CompatibilityLevel.MultiplayerBlocked)
            .ToList();

        Assert.IsTrue(blocked.Count >= 6);
        foreach (var record in blocked)
        {
            Assert.AreEqual("none", record.RecommendedProxy, $"{record.Title} must recommend proxy 'none'.");
            Assert.IsFalse(record.SupportsNativeFrameGen, $"{record.Title} must not allow native FG injection.");
        }
    }

    [TestMethod]
    public async Task B08_AntiCheatScan_Depth5DetectedWhileDepth7OutsideBoundary()
    {
        var pack = await LoadPackAsync();

        using (var depth5 = new IsolatedGameSandbox("t2_b08_d5"))
        {
            depth5.CreateFile("Game.exe", "EXE");
            depth5.CreateFile("a/b/c/d/e/BEDaisy.sys", "BATTLEYE");
            var res5 = await RuntimePreflightService.RunPreflightAsync("d5", depth5.GameRoot, pack);
            Assert.IsTrue(res5.HasAntiCheatRisk);
            Assert.AreEqual(PreflightStatus.Blocked, res5.Status);
        }

        using (var depth7 = new IsolatedGameSandbox("t2_b08_d7"))
        {
            depth7.CreateFile("Game.exe", "EXE");
            depth7.CreateFile("a/b/c/d/e/f/g/BEDaisy.sys", "BATTLEYE");
            var res7 = await RuntimePreflightService.RunPreflightAsync("d7", depth7.GameRoot, pack);
            Assert.IsFalse(res7.HasAntiCheatRisk);
        }
    }

    [TestMethod]
    public async Task B08_BlockedPlan_ProducesZeroCopiesAndInstallPlanThrowsWithoutMutatingDisk()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b08_nomutate");
        sandbox.CreateFile("r5apex.exe", "EXE");
        sandbox.CreateFile("EasyAntiCheat/EasyAntiCheat_EOS.exe", "EAC");

        var pack = await LoadPackAsync();
        var plan = await DeploymentPlanner.CreatePlanAsync(
            "apex", "Apex Legends", sandbox.GameRoot, pack,
            RuntimeIntegrationPreset.DlssWithFrameGeneration,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());

        Assert.IsFalse(plan.CanExecute);
        Assert.AreEqual(0, plan.PlannedCopies.Count);
        await Assert.ThrowsExactlyAsync<System.InvalidOperationException>(
            () => RuntimeInstallService.InstallPlanAsync(plan));
        Assert.IsFalse(sandbox.FileExists("dxgi.dll"));
        Assert.IsFalse(sandbox.FileExists("OptiScaler.ini"));
    }

    #endregion
}
