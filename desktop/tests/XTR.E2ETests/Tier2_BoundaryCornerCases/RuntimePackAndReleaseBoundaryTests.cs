using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
[DoNotParallelize]
public sealed class RuntimePackAndReleaseBoundaryTests
{
    #region F11 Boundaries: Runtime Pack Exact Pinning & Tamper Rejection

    [TestMethod]
    public async Task B11_ExactByteSizeDrift_OneByteAddedToIni_DetectedImmediately()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b11_onebyte");
        var tempPack = sandbox.CopyBundledPackToTemp();
        var iniPath = Path.Combine(tempPack, "OptiScaler.ini");
        await File.AppendAllTextAsync(iniPath, " ");

        var manifest = XtrPackagingValidatorContract.LoadManifest(Path.Combine(tempPack, "xtr-runtime.json"));
        bool valid = XtrPackagingValidatorContract.VerifyRuntimeFilesIntegrity(tempPack, manifest, out var errors);

        Assert.IsFalse(valid, "A single-byte modification to OptiScaler.ini must fail size and hash validation (no +/-4 byte loophole).");
        Assert.IsTrue(errors.Any(e => e.Contains("Size mismatch", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("SHA256 mismatch", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task B11_MissingSubdirectoryFile_D3D12CoreDeleted_BlockedByPreflight()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b11_missingcore");
        sandbox.CreateFile("Game.exe", "EXE");
        var tempPack = sandbox.CopyBundledPackToTemp();

        var trustedPack = await RuntimePack.LoadFromDirectoryAsync(tempPack);
        Assert.IsNotNull(trustedPack);
        Assert.IsTrue(trustedPack.IsTrusted);

        File.Delete(Path.Combine(tempPack, "D3D12_Optiscaler", "D3D12Core.dll"));

        var toctouPreflight = await RuntimePreflightService.RunPreflightAsync("b11-missing", sandbox.GameRoot, trustedPack);
        Assert.AreEqual(PreflightStatus.Blocked, toctouPreflight.Status);
        Assert.IsFalse(toctouPreflight.PackIntegrityValid);

        var reloadedPack = await RuntimePack.LoadFromDirectoryAsync(tempPack);
        Assert.IsNotNull(reloadedPack);
        Assert.IsFalse(reloadedPack.IsTrusted);
    }

    [TestMethod]
    public void B11_UntrackedSubdirectoryPlugin_FlaggedByFindUntrackedRuntimeFiles()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b11_extra");
        var tempPack = sandbox.CopyBundledPackToTemp();
        var roguePath = Path.Combine(tempPack, "plugins", "rogue_overlay.asi");
        Directory.CreateDirectory(Path.GetDirectoryName(roguePath)!);
        File.WriteAllText(roguePath, "ROGUE");
        var rogueBypassPath = Path.Combine(tempPack, "!!rogue_bypass.dll");
        File.WriteAllText(rogueBypassPath, "ROGUE_BYPASS");

        var manifest = XtrPackagingValidatorContract.LoadManifest(Path.Combine(tempPack, "xtr-runtime.json"));
        var untracked = XtrPackagingValidatorContract.FindUntrackedRuntimeFiles(tempPack, manifest);

        Assert.IsTrue(untracked.Contains("plugins/rogue_overlay.asi", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(untracked.Contains("!!rogue_bypass.dll", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task B11_EmptyOrMalformedRuntimeJson_RejectedCleanly()
    {
        using var sandbox = new IsolatedGameSandbox("t2_b11_badjson");
        var badPack = Path.Combine(sandbox.SandboxRoot, "bad_pack");
        Directory.CreateDirectory(badPack);
        await File.WriteAllTextAsync(Path.Combine(badPack, "xtr-runtime.json"), "{ \"schemaVersion\": 3 }");

        var pack = await RuntimePack.LoadFromDirectoryAsync(badPack);
        Assert.IsNotNull(pack);
        Assert.IsFalse(pack.IsTrusted, "Manifest without fileHashes must never be trusted.");

        await File.WriteAllTextAsync(Path.Combine(badPack, "xtr-runtime.json"), "null");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => RuntimePack.LoadFromDirectoryAsync(badPack));

        await File.WriteAllTextAsync(Path.Combine(badPack, "xtr-runtime.json"), "{ invalid json");
        await Assert.ThrowsExactlyAsync<JsonException>(
            () => RuntimePack.LoadFromDirectoryAsync(badPack));
    }

    [TestMethod]
    public async Task B11_GetExpectedHash_NormalizesForwardAndBackSlashes()
    {
        var pack = await RuntimePack.LoadFromDirectoryAsync(TestPaths.BundledPackagePath);
        Assert.IsNotNull(pack);

        string? forward = pack.GetExpectedHash("Licenses/XeSS_LICENSE.txt");
        string? back = pack.GetExpectedHash("Licenses\\XeSS_LICENSE.txt");

        Assert.IsNotNull(forward);
        Assert.AreEqual(forward, back);
        Assert.AreEqual(64, forward.Length);
    }

    #endregion

    #region F12 Boundaries: DLSS Version Formatting & Packaging Contracts

    [TestMethod]
    public void B12_DlssVersion_NullWhitespaceAndMalformedInputs_ReturnSafeFallbacks()
    {
        Assert.AreEqual("No DLSS", DlssVersionHelper.FormatGeneration(null));
        Assert.AreEqual("No DLSS", DlssVersionHelper.FormatGeneration("   "));
        Assert.AreEqual("DLSS", DlssVersionHelper.FormatGeneration("not_a_version"));
        Assert.AreEqual("DLSS not detected", DlssVersionHelper.FormatVersionLabel(null));
        Assert.AreEqual("DLSS not detected", DlssVersionHelper.FormatVersionLabel("   "));
        Assert.AreEqual("NO DLSS", DlssVersionHelper.FormatBadge(string.Empty));
    }

    [TestMethod]
    public void B12_DlssVersion_Dlss4And45_310Series_FormatsWithoutTrailingZero()
    {
        Assert.AreEqual("310.1", DlssVersionHelper.CleanVersion("310.1.0.0"));
        Assert.AreEqual("310.6", DlssVersionHelper.CleanVersion("v310.6.0.0"));
        Assert.AreEqual("DLSS 310 - v310.1", DlssVersionHelper.FormatVersionLabel("310.1.0.0"));
        Assert.AreEqual("DLSS 310 - v310.6", DlssVersionHelper.FormatBadge("310.6.0.0"));
        Assert.AreEqual("0.0", DlssVersionHelper.CleanVersion("0.0.0.0"));
    }

    [TestMethod]
    public void B12_FrameGenBadgePrecedence_BoostOverridesFgOverridesNative()
    {
        Assert.AreEqual("⚡ BOOST ACTIVE", DlssVersionHelper.FormatFrameGenBadge(true, true, true, true));
        Assert.AreEqual("⚡ FG ACTIVE", DlssVersionHelper.FormatFrameGenBadge(true, false, true, true));
        Assert.AreEqual("DLSS 3 FG", DlssVersionHelper.FormatFrameGenBadge(true, false, false, true));
        Assert.AreEqual(string.Empty, DlssVersionHelper.FormatFrameGenBadge(false, true, true, false));
        Assert.AreEqual(string.Empty, DlssVersionHelper.FormatFrameGenBadge(false, false, false, false));
    }

    [TestMethod]
    public void B12_BuildWindowsScript_SupportsReleaseAndPortableWithSfxPackaging()
    {
        string script = File.ReadAllText(TestPaths.BuildWindowsScriptPath);

        Assert.IsTrue(script.Contains("\"Release\", \"Release_Portable\"", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("[switch]$SelfContained", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("[switch]$PackageZip", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("[switch]$BuildInstaller", StringComparison.Ordinal));
        Assert.IsTrue(script.Contains("7z.exe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void B12_ReleaseWorkflow_TriggersOnVersionTagsAndTargetsWindows2025()
    {
        string workflow = File.ReadAllText(TestPaths.ReleaseWorkflowPath);

        Assert.IsTrue(workflow.Contains("tags:", StringComparison.Ordinal));
        Assert.IsTrue(workflow.Contains("v*", StringComparison.Ordinal));
        Assert.IsTrue(workflow.Contains("windows-2025", StringComparison.Ordinal));
        Assert.IsTrue(workflow.Contains("-Configuration Release -SelfContained -PackageZip", StringComparison.Ordinal));
        Assert.IsTrue(workflow.Contains("-Configuration Release_Portable -SelfContained -PackageZip", StringComparison.Ordinal));
    }

    #endregion
}
