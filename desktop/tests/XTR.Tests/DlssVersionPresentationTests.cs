using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public sealed class DlssVersionPresentationTests
{
    [TestMethod]
    public void DlssPresentation_WhenNoDlss_ReportsNotDetected()
    {
        Assert.AreEqual("No DLSS", DlssVersionHelper.FormatGeneration(null));
        Assert.AreEqual("DLSS not detected", DlssVersionHelper.FormatVersionLabel(null));
        Assert.AreEqual("NO DLSS", DlssVersionHelper.FormatBadge(null));
    }

    [TestMethod]
    public void DlssPresentation_WhenDlss3_ReportsDlss3AndVersion()
    {
        var rawVersion = "3.7.10.0";
        Assert.AreEqual("DLSS 3", DlssVersionHelper.FormatGeneration(rawVersion));
        Assert.AreEqual("DLSS 3 - v3.7.10", DlssVersionHelper.FormatVersionLabel(rawVersion));
        Assert.AreEqual("DLSS 3 - v3.7.10", DlssVersionHelper.FormatBadge(rawVersion));
    }

    [TestMethod]
    public void DlssPresentation_WhenDlss2_ReportsDlss2AndVersion()
    {
        var rawVersion = "2.5.1.0";
        Assert.AreEqual("DLSS 2", DlssVersionHelper.FormatGeneration(rawVersion));
        Assert.AreEqual("DLSS 2 - v2.5.1", DlssVersionHelper.FormatVersionLabel(rawVersion));
        Assert.AreEqual("DLSS 2 - v2.5.1", DlssVersionHelper.FormatBadge(rawVersion));
    }

    [TestMethod]
    public void DlssPresentation_WhenDlss4_ReportsDlss4AndVersion()
    {
        var rawVersion = "4.0.0.0";
        Assert.AreEqual("DLSS 4", DlssVersionHelper.FormatGeneration(rawVersion));
        Assert.AreEqual("DLSS 4 - v4.0", DlssVersionHelper.FormatVersionLabel(rawVersion));
        Assert.AreEqual("DLSS 4 - v4.0", DlssVersionHelper.FormatBadge(rawVersion));
    }

    [TestMethod]
    public void CleanVersion_StripsTrailingZeroesProperly()
    {
        Assert.AreEqual("3.7.10", DlssVersionHelper.CleanVersion("3.7.10.0"));
        Assert.AreEqual("2.4", DlssVersionHelper.CleanVersion("2.4.0.0"));
        Assert.AreEqual("3.5", DlssVersionHelper.CleanVersion("3.5.0"));
    }

    [TestMethod]
    public void QuickSwapVersionMatching_Prefers310xAndRejectsDlss1x()
    {
        Assert.IsTrue(DlssVersionHelper.MatchesAnyVersionPrefix("310.9.16.0", "310.", "3.8", "3.7"));
        Assert.IsTrue(DlssVersionHelper.MatchesAnyVersionPrefix("310.6.0.0", "310.", "3.8", "3.7"));
        Assert.IsTrue(DlssVersionHelper.MatchesAnyVersionPrefix("3.7.20.0", "310.", "3.8", "3.7"));
        Assert.IsFalse(DlssVersionHelper.MatchesAnyVersionPrefix("1.0.13.0", "310.", "3.8", "3.7"));
        Assert.IsTrue(DlssVersionHelper.IsDlss1xVersion("1.0.13.0"));
        Assert.IsFalse(DlssVersionHelper.IsDlss1xVersion("310.9.16.0"));
    }

    [TestMethod]
    public void RuntimeBoostAndFrameGenBadge_WhenNotOperational_ReturnsFalseAndEmpty()
    {
        Assert.IsFalse(DlssVersionHelper.ComputeHasRuntimeBoostOrFg(hasRuntimeManifest: false, isTurboBoost: true, hasActiveRuntimeFg: true));
        Assert.AreEqual(string.Empty, DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: false, isTurboBoost: true, hasActiveRuntimeFg: true, hasNativeDlssG: false));
        Assert.AreEqual("DLSS 3 FG", DlssVersionHelper.FormatFrameGenBadge(hasRuntimeManifest: false, isTurboBoost: false, hasActiveRuntimeFg: false, hasNativeDlssG: true));
    }
}
