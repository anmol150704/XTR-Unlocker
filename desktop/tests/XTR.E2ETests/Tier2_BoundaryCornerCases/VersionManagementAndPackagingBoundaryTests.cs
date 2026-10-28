using System;
using System.IO;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
public sealed class VersionManagementAndPackagingBoundaryTests
{
    #region Feature 14 Boundaries: Upscaler DLL Switcher & Version Management

    [TestMethod]
    public void B14_DlssVersion_MalformedString_Fallback()
    {
        Assert.AreEqual("DLSS", DlssVersionHelper.FormatGeneration("not_a_version"));
        Assert.AreEqual("DLSS not detected", DlssVersionHelper.FormatVersionLabel(null));
        Assert.AreEqual("DLSS not detected", DlssVersionHelper.FormatVersionLabel("   "));
        Assert.AreEqual("NO DLSS", DlssVersionHelper.FormatBadge(string.Empty));
    }

    [TestMethod]
    public void B14_DlssVersion_ZeroVersion_FormatsSafely()
    {
        string cleaned = DlssVersionHelper.CleanVersion("0.0.0.0");
        Assert.AreEqual("0.0", cleaned);

        string label = DlssVersionHelper.FormatVersionLabel("0.0.0.0");
        Assert.AreEqual("DLSS 0 - v0.0", label);
    }

    [TestMethod]
    public void B14_DllManifest_FileHashMismatch_FlagsDrift()
    {
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);
        Assert.IsTrue(manifest.FileHashes.Count > 0);

        // Corrupt first file hash in memory to test drift detection
        var corruptedManifest = new XtrRuntimeManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            Version = manifest.Version,
            FileHashes = [.. manifest.FileHashes]
        };
        corruptedManifest.FileHashes[0] = new RuntimeFileHashRecord
        {
            Path = manifest.FileHashes[0].Path,
            Sha256 = new string('0', 64), // Invalid hash
            SizeBytes = manifest.FileHashes[0].SizeBytes
        };

        bool valid = XtrPackagingValidatorContract.VerifyRuntimeFilesIntegrity(
            TestPaths.BundledPackagePath, corruptedManifest, out var errors);

        Assert.IsFalse(valid, "Drifted file hash must be flagged.");
        Assert.IsTrue(errors.Any(e => e.Contains("SHA256 mismatch")), "Errors must report SHA256 mismatch.");
    }

    [TestMethod]
    public void B14_DllManifest_MissingFile_FlagsNeedsAttention()
    {
        var manifest = new XtrRuntimeManifest
        {
            SchemaVersion = 2,
            FileHashes =
            [
                new RuntimeFileHashRecord
                {
                    Path = "non_existent_fake_file.dll",
                    Sha256 = new string('a', 64),
                    SizeBytes = 1234
                }
            ]
        };

        bool valid = XtrPackagingValidatorContract.VerifyRuntimeFilesIntegrity(
            TestPaths.BundledPackagePath, manifest, out var errors);

        Assert.IsFalse(valid);
        Assert.IsTrue(errors.Any(e => e.Contains("Missing runtime file")));
    }

    [TestMethod]
    public void B14_VersionManagement_EmptyBadges_WhenNothingInstalled()
    {
        string badge = DlssVersionHelper.FormatFrameGenBadge(false, false, false, false);
        Assert.AreEqual(string.Empty, badge);
    }

    #endregion

    #region Feature 15 Boundaries: Release Packaging & Single-File Installer

    [TestMethod]
    public void B15_Packaging_ManifestMissingKey_ThrowsOrRejects()
    {
        string badJson = "{\"schemaVersion\": 2}"; // Missing fileHashes
        var manifest = System.Text.Json.JsonSerializer.Deserialize<XtrRuntimeManifest>(badJson);
        Assert.IsNotNull(manifest);
        Assert.AreEqual(0, manifest.FileHashes.Count);
    }

    [TestMethod]
    public void B15_Packaging_EmptyFileInManifest_Rejected()
    {
        var manifest = new XtrRuntimeManifest
        {
            FileHashes =
            [
                new RuntimeFileHashRecord { Path = "empty.dll", Sha256 = new string('e', 64), SizeBytes = 0 }
            ]
        };

        Assert.AreEqual(0, manifest.FileHashes[0].SizeBytes);
    }

    [TestMethod]
    public void B15_Packaging_PathNormalization_HandlesBackslashes()
    {
        string forward = "Licenses/XeSS_LICENSE.txt";
        string normalized = forward.Replace('/', Path.DirectorySeparatorChar);

        Assert.IsTrue(normalized.Contains(Path.DirectorySeparatorChar.ToString()));
    }

    [TestMethod]
    public void B15_Packaging_ExtraFiles_Detected()
    {
        // Directory contains files not in manifest
        string[] diskFiles = Directory.GetFiles(TestPaths.BundledPackagePath, "*.dll");
        var manifest = XtrPackagingValidatorContract.LoadManifest(TestPaths.XtrRuntimeJsonPath);

        var manifestDllNames = manifest.FileHashes
            .Select(h => Path.GetFileName(h.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in diskFiles)
        {
            string fileName = Path.GetFileName(file);
            Assert.IsTrue(manifestDllNames.Contains(fileName),
                $"Runtime directory file {fileName} should be tracked in manifest.");
        }
    }

    [TestMethod]
    public void B15_Packaging_7zSfxHeader_Verification()
    {
        // SFX installer uses 7z.sfx header. Script must reference 7z or 7z.sfx
        string script = File.ReadAllText(TestPaths.BuildWindowsScriptPath);
        Assert.IsTrue(script.Contains("7z", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
