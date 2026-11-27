using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
[DoNotParallelize]
public sealed class RuntimePackAndReleaseFeatureTests
{
    #region F11: Runtime Pack (xtr-runtime.json) Exact SHA-256 / Byte-Size Pinning & Tamper Rejection

    [TestMethod]
    public void F11_01_RuntimePackManifest_IsSchemaV3With18PinnedFiles()
    {
        Assert.IsTrue(File.Exists(TestPaths.XtrRuntimeJsonPath), "xtr-runtime.json must exist.");
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);

        Assert.AreEqual(3, manifest.SchemaVersion);
        Assert.AreEqual("0.9.5", manifest.Version);
        Assert.AreEqual(XtrPackagingValidatorContract.ExpectedPinnedFileCount, manifest.FileHashes.Count,
            $"xtr-runtime.json must pin exactly {XtrPackagingValidatorContract.ExpectedPinnedFileCount} files (excluding removed xtr_hud.asi).");
    }

    [TestMethod]
    public async Task F11_02_RuntimePack_AllPinnedFilesMatchExactSha256AndSizeBytes()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        bool valid = XtrPackagingValidatorContract.VerifyRuntimeFilesIntegrity(
            TestPaths.BundledPackagePath, manifest, out var errors);

        Assert.IsTrue(valid, $"All pinned runtime files must match exact SHA-256 and byte size. Errors: {string.Join("; ", errors)}");

        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);
        Assert.IsTrue(pack.IsTrusted, "Bundled RuntimePack must pass cryptographic verification and be trusted.");
    }

    [TestMethod]
    public void F11_03_RuntimePack_HasZeroUntrackedRuntimeFilesOnDisk()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        var untracked = XtrPackagingValidatorContract.FindUntrackedRuntimeFiles(TestPaths.BundledPackagePath, manifest);

        Assert.AreEqual(0, untracked.Count,
            $"Untracked files found in bundled runtime directory: {string.Join(", ", untracked)}");
    }

    [TestMethod]
    public async Task F11_04_TamperedRuntimePack_RejectedByVerifyIntegrityAndPreflight()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f11_tamper");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");
        var packRoot = sandbox.CopyBundledPackToTemp();

        // 1. Load valid pack first (IsTrusted == true), then tamper on disk (TOCTOU)
        var loadedTrustedPack = await RuntimePack.LoadFromDirectoryAsync(packRoot);
        Assert.IsNotNull(loadedTrustedPack);
        Assert.IsTrue(loadedTrustedPack.IsTrusted);

        var iniPath = Path.Combine(packRoot, "OptiScaler.ini");
        await File.AppendAllTextAsync(iniPath, "\n; tampered\n");

        var toctouPreflight = await RuntimePreflightService.RunPreflightAsync("t1-tamper-toctou", sandbox.GameRoot, loadedTrustedPack);
        Assert.AreEqual(PreflightStatus.Blocked, toctouPreflight.Status);
        Assert.IsFalse(toctouPreflight.CanProceed);
        Assert.IsFalse(toctouPreflight.PackIntegrityValid);

        var blockedPlan = await DeploymentPlanner.CreatePlanAsync(
            "t1-tamper-toctou", "Test Game", sandbox.GameRoot, loadedTrustedPack,
            RuntimeIntegrationPreset.LatestDlssOnly,
            gpuInfo: IsolatedGameSandbox.CreateRtx4070());
        Assert.IsFalse(blockedPlan.CanExecute);

        // 2. Reloading already-tampered pack marks IsTrusted == false and fails VerifyIntegrityAsync
        var reloadedTampered = await RuntimePack.LoadFromDirectoryAsync(packRoot);
        Assert.IsNotNull(reloadedTampered);
        Assert.IsFalse(reloadedTampered.IsTrusted, "Tampered runtime pack must fail trust check.");
        var (isValid, errors) = await reloadedTampered.VerifyIntegrityAsync();
        Assert.IsFalse(isValid);
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public async Task F11_05_CustomLocalPackWithoutManifest_InferredAsUntrustedExperimental()
    {
        using var sandbox = new IsolatedGameSandbox("t1_f11_custom");
        sandbox.CreateFile("Game.exe", "EXE");
        sandbox.CreateFile("d3d12.dll", "DX12");

        var customPackRoot = Path.Combine(sandbox.SandboxRoot, "custom_pack");
        Directory.CreateDirectory(customPackRoot);
        File.Copy(Path.Combine(TestPaths.BundledPackagePath, "OptiScaler.dll"), Path.Combine(customPackRoot, "OptiScaler.dll"));
        File.Copy(TestPaths.OptiScalerIniPath, Path.Combine(customPackRoot, "OptiScaler.ini"));

        var pack = await RuntimePack.LoadFromDirectoryAsync(customPackRoot);
        Assert.IsNotNull(pack);
        Assert.AreEqual(RuntimeReleaseChannel.CustomLocal, pack.ReleaseChannel);
        Assert.IsFalse(pack.IsTrusted);

        var preflight = await RuntimePreflightService.RunPreflightAsync("t1-custom", sandbox.GameRoot, pack);
        Assert.AreEqual(PreflightStatus.Experimental, preflight.Status);
        Assert.IsTrue(preflight.CanProceed);
    }

    #endregion

    #region F12: DLSS Version Formatting & Release Packaging Contract

    [TestMethod]
    public void F12_01_DlssVersionHelper_CleansDlss2Dlss3AndDlss4Versions()
    {
        Assert.AreEqual("2.5.1", DlssVersionHelper.CleanVersion("2.5.1.0"));
        Assert.AreEqual("3.5.10", DlssVersionHelper.CleanVersion("V3.5.10.0"));
        Assert.AreEqual("3.7.10", DlssVersionHelper.CleanVersion("v3.7.10.0"));
        Assert.AreEqual("310.2.1", DlssVersionHelper.CleanVersion("310.2.1.0"));
        Assert.AreEqual("310.6", DlssVersionHelper.CleanVersion("310.6.0.0"));
    }

    [TestMethod]
    public void F12_02_DlssVersionHelper_FormatsGenerationLabelsAndBadges()
    {
        Assert.AreEqual("DLSS 3", DlssVersionHelper.FormatGeneration("3.7.10.0"));
        Assert.AreEqual("DLSS 310", DlssVersionHelper.FormatGeneration("310.6.0.0"));
        Assert.AreEqual("DLSS 3 - v3.7.10", DlssVersionHelper.FormatVersionLabel("3.7.10.0"));
        Assert.AreEqual("DLSS 310 - v310.6", DlssVersionHelper.FormatBadge("310.6.0.0"));
        Assert.AreEqual("⚡ BOOST ACTIVE", DlssVersionHelper.FormatFrameGenBadge(true, true, true, false));
        Assert.AreEqual("⚡ FG ACTIVE", DlssVersionHelper.FormatFrameGenBadge(true, false, true, false));
        Assert.AreEqual("DLSS 3 FG", DlssVersionHelper.FormatFrameGenBadge(false, false, false, true));
    }

    [TestMethod]
    public void F12_03_DlssVersionHelper_ComputesRuntimeBoostAndNativeOnlyFlags()
    {
        Assert.IsTrue(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(true, true, false));
        Assert.IsTrue(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(true, false, true));
        Assert.IsFalse(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(true, false, false));
        Assert.IsFalse(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(false, true, true));

        Assert.IsTrue(DlssVersionHelper.ComputeHasNativeDlssGOnly(false, true));
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(true, true));
    }

    [TestMethod]
    public void F12_04_BuildWindowsScript_DefinesInstallerZipAndRunsBothTestSuites()
    {
        Assert.IsTrue(File.Exists(TestPaths.BuildWindowsScriptPath));
        string script = File.ReadAllText(TestPaths.BuildWindowsScriptPath);

        Assert.IsTrue(script.Contains("XTR-Unlocker-Setup.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(script.Contains("XTR-Unlocker-$Configuration-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(script.Contains("7z.sfx", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(script.Contains("XTR.Tests.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(script.Contains("XTR.E2ETests.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void F12_05_ReleaseWorkflowAndCsproj_AlignOnNet10AndBundledPayload()
    {
        Assert.IsTrue(File.Exists(TestPaths.ReleaseWorkflowPath));
        string workflow = File.ReadAllText(TestPaths.ReleaseWorkflowPath);

        Assert.IsTrue(workflow.Contains("10.0.x", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(workflow.Contains("dist/XTR-Unlocker-Setup.exe", StringComparison.Ordinal));
        Assert.IsTrue(workflow.Contains("dist/XTR-Unlocker-Release-win-x64.zip", StringComparison.Ordinal));
        Assert.IsTrue(workflow.Contains("dist/XTR-Unlocker-Release_Portable-win-x64.zip", StringComparison.Ordinal));

        string csproj = File.ReadAllText(TestPaths.DesktopProjectPath);
        Assert.IsTrue(csproj.Contains("net10.0-windows10.0.26100.0", StringComparison.Ordinal));
        Assert.IsTrue(csproj.Contains(@"Runtime\OptiScaler\**\*", StringComparison.Ordinal));
    }

    #endregion
}
