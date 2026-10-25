using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class RuntimePackTests
{
    private static string GetBundledPackagePath()
    {
        // Tests run in desktop/tests/XTR.Tests/bin/... or similar; locate desktop/src/Runtime/OptiScaler
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
    public async Task Load_BundledPack_SucceedsAndIsTrusted()
    {
        var packagePath = GetBundledPackagePath();
        var pack = await RuntimePack.LoadFromDirectoryAsync(packagePath);

        Assert.IsNotNull(pack);
        Assert.AreEqual("optiscaler-0.9.4-xtr", pack.Id);
        Assert.AreEqual("0.9.4", pack.Version);
        Assert.AreEqual(RuntimeReleaseChannel.Stable, pack.ReleaseChannel);
        Assert.IsTrue(pack.IsTrusted, "Bundled pack must pass SHA-256 integrity check and be trusted.");
        Assert.IsTrue(pack.FileHashes.Count >= 18, $"Expected at least 18 tracked file hashes, found {pack.FileHashes.Count}.");
        Assert.AreEqual("dxgi.dll", pack.RecommendedProxy);
        Assert.IsTrue(pack.Capabilities.DlssUpdate);
        Assert.IsTrue(pack.Capabilities.FsrXeSsFallback);
    }

    [TestMethod]
    public async Task VerifyIntegrity_WithTamperedFile_FailsValidation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_test_tamper_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var bundledPath = GetBundledPackagePath();

            // Copy manifest
            File.Copy(Path.Combine(bundledPath, "xtr-runtime.json"), Path.Combine(tempDir, "xtr-runtime.json"));

            // Create a fake OptiScaler.dll with tampered content
            await File.WriteAllTextAsync(Path.Combine(tempDir, "OptiScaler.dll"), "TAMPERED_CONTENT");

            var pack = await RuntimePack.LoadFromDirectoryAsync(tempDir);
            Assert.IsNotNull(pack);
            Assert.IsFalse(pack.IsTrusted, "Tampered package must not be marked trusted.");

            var (isValid, errors) = await pack.VerifyIntegrityAsync();
            Assert.IsFalse(isValid);
            Assert.IsTrue(errors.Any(e => e.Contains("Hash mismatch") || e.Contains("Missing required")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void InferCustomPackage_DetectsComponents()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"xtr_test_infer_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.dll"), "dummy dll");
            File.WriteAllText(Path.Combine(tempDir, "OptiScaler.ini"), "[Upscalers]\nDx12Upscaler=auto\n");
            File.WriteAllText(Path.Combine(tempDir, "dlssg_sm86.dll"), "dummy mfg");

            var pack = RuntimePack.InferCustomPackage(tempDir);
            Assert.IsNotNull(pack);
            Assert.IsFalse(pack.IsTrusted);
            Assert.AreEqual(RuntimeReleaseChannel.CustomLocal, pack.ReleaseChannel);
            Assert.IsTrue(pack.Capabilities.MultiFrameGeneration);
            Assert.IsTrue(pack.Components.Contains("RTX 20/30 frame-generation unlocker"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
