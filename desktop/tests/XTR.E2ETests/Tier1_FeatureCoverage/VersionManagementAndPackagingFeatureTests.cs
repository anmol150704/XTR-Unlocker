using System;
using System.IO;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier1_FeatureCoverage;

[TestClass]
public sealed class VersionManagementAndPackagingFeatureTests
{
    #region Feature 14: Upscaler DLL Switcher & Version Management

    [TestMethod]
    public void F14_DlssVersionHelper_CleansVersionString()
    {
        Assert.AreEqual("3.7.10", DlssVersionHelper.CleanVersion("v3.7.10.0"));
        Assert.AreEqual("3.5.10", DlssVersionHelper.CleanVersion("V3.5.10.0"));
        Assert.AreEqual("2.5.1", DlssVersionHelper.CleanVersion("2.5.1.0"));
    }

    [TestMethod]
    public void F14_DlssVersionHelper_FormatsGenerationAndLabels()
    {
        Assert.AreEqual("DLSS 3", DlssVersionHelper.FormatGeneration("3.7.10.0"));
        Assert.AreEqual("DLSS 2", DlssVersionHelper.FormatGeneration("2.5.1.0"));
        Assert.AreEqual("No DLSS", DlssVersionHelper.FormatGeneration(null));

        Assert.AreEqual("DLSS 3 - v3.7.10", DlssVersionHelper.FormatVersionLabel("3.7.10.0"));
        Assert.AreEqual("DLSS not detected", DlssVersionHelper.FormatVersionLabel(string.Empty));
    }

    [TestMethod]
    public void F14_VersionManagement_FormatsActiveBadges()
    {
        string boostBadge = DlssVersionHelper.FormatFrameGenBadge(
            hasRuntimeManifest: true, isTurboBoost: true, hasActiveRuntimeFg: true, hasNativeDlssG: false);
        Assert.AreEqual("⚡ BOOST ACTIVE", boostBadge);

        string fgBadge = DlssVersionHelper.FormatFrameGenBadge(
            hasRuntimeManifest: true, isTurboBoost: false, hasActiveRuntimeFg: true, hasNativeDlssG: false);
        Assert.AreEqual("⚡ FG ACTIVE", fgBadge);

        string nativeBadge = DlssVersionHelper.FormatFrameGenBadge(
            hasRuntimeManifest: false, isTurboBoost: false, hasActiveRuntimeFg: false, hasNativeDlssG: true);
        Assert.AreEqual("DLSS 3 FG", nativeBadge);
    }

    [TestMethod]
    public void F14_VersionManagement_ComputesHasRuntimeBoostOrFg()
    {
        Assert.IsTrue(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(true, true, true));
        Assert.IsTrue(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(true, false, true));
        Assert.IsFalse(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(true, false, false));
        Assert.IsFalse(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(false, false, true));
    }

    [TestMethod]
    public void F14_VersionManagement_ComputesHasNativeDlssGOnly()
    {
        Assert.IsTrue(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasRuntimeBoostOrFg: false, hasNativeDlssG: true));
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasRuntimeBoostOrFg: true, hasNativeDlssG: true));
        Assert.IsFalse(DlssVersionHelper.ComputeHasNativeDlssGOnly(hasRuntimeBoostOrFg: false, hasNativeDlssG: false));
    }

    #endregion

    #region Feature 15: Release Packaging & Single-File Installer (dist/XTR-Unlocker-Setup.exe)

    [TestMethod]
    public void F15_Packaging_ManifestFile_ExistsAndValidJson()
    {
        Assert.IsTrue(File.Exists(TestPaths.XtrRuntimeJsonPath), "xtr-runtime.json must exist.");
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.IsNotNull(manifest);
        Assert.AreEqual(2, manifest.SchemaVersion);
        Assert.AreEqual("0.9.4", manifest.Version);
    }

    [TestMethod]
    public void F15_Packaging_ManifestFile_Pins18RuntimeFiles()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.AreEqual(XtrPackagingValidatorContract.ExpectedPinnedFileCount, manifest.FileHashes.Count,
            $"Manifest must pin exactly {XtrPackagingValidatorContract.ExpectedPinnedFileCount} runtime files.");
    }

    [TestMethod]
    public void F15_Packaging_RuntimeFiles_HaveValidSha256()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        foreach (var record in manifest.FileHashes)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.Path), "Path cannot be empty.");
            Assert.AreEqual(64, record.Sha256.Length, $"SHA256 for {record.Path} must be 64 hex characters.");
            Assert.IsTrue(record.SizeBytes > 0, $"File size for {record.Path} must be greater than 0.");
        }
    }

    [TestMethod]
    public void F15_Packaging_FilesOnDisk_MatchManifestHashes()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        bool valid = XtrPackagingValidatorContract.VerifyRuntimeFilesIntegrity(
            TestPaths.BundledPackagePath, manifest, out var errors);

        Assert.IsTrue(valid, $"All 18 runtime files on disk must match manifest SHA256. Errors: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void F15_Packaging_ReleaseScript_DefinesInstallerTarget()
    {
        Assert.IsTrue(File.Exists(TestPaths.BuildWindowsScriptPath), "scripts/build-windows.ps1 must exist.");
        string scriptText = File.ReadAllText(TestPaths.BuildWindowsScriptPath);

        Assert.IsTrue(scriptText.Contains("XTR-Unlocker-Setup.exe", StringComparison.OrdinalIgnoreCase),
            "Build script must define 7-Zip SFX setup installer target: XTR-Unlocker-Setup.exe.");
        Assert.IsTrue(scriptText.Contains("XTR-Unlocker-$Configuration-win-x64.zip", StringComparison.OrdinalIgnoreCase) ||
                      scriptText.Contains("XTR-Unlocker-Release-win-x64.zip", StringComparison.OrdinalIgnoreCase),
            "Build script must define portable release zip target.");
    }

    #endregion
}
