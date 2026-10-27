using DLSS_Swapper.Data.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTR.Tests;

[TestClass]
public class GpuHardwareDetectorTests
{
    [TestMethod]
    public void ClassifyGpu_Rtx4090_IdentifiesAdaLovelaceWithNativeDlssG()
    {
        var info = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 4090" };
        GpuHardwareDetector.ClassifyGpu(info);

        Assert.AreEqual(GpuVendor.Nvidia, info.Vendor);
        Assert.IsTrue(info.Architecture.Contains("Ada Lovelace"));
        Assert.IsTrue(info.SupportsNativeDlssG);
        Assert.IsTrue(info.RecommendedFgMode.Contains("Native DLSS 3"));
    }

    [TestMethod]
    public void ClassifyGpu_Rtx3080_IdentifiesAmpereWithUniversalBridgeAndSpoofing()
    {
        var info = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 3080 Ti" };
        GpuHardwareDetector.ClassifyGpu(info);

        Assert.AreEqual(GpuVendor.Nvidia, info.Vendor);
        Assert.IsTrue(info.Architecture.Contains("Ampere"));
        Assert.IsFalse(info.SupportsNativeDlssG);
        Assert.IsTrue(info.RecommendedFgMode.Contains("Universal FG Bridge"));
        Assert.IsTrue(info.RecommendedFgMode.Contains("Streamline Spoofing"));
        Assert.AreEqual("Preset E (Anti-Ghosting Champion)", info.RecommendedNeuralPreset);
    }

    [TestMethod]
    public void ClassifyGpu_Rtx2050_IdentifiesTuringWithUniversalBridgeAndSpoofing()
    {
        var info = new GpuHardwareInfo { Name = "NVIDIA GeForce RTX 2050" };
        GpuHardwareDetector.ClassifyGpu(info);

        Assert.AreEqual(GpuVendor.Nvidia, info.Vendor);
        Assert.IsTrue(info.Architecture.Contains("Turing"));
        Assert.IsFalse(info.SupportsNativeDlssG);
        Assert.IsTrue(info.RecommendedFgMode.Contains("Universal FG Bridge"));
        Assert.IsTrue(info.RecommendedFgMode.Contains("Streamline Spoofing"));
    }

    [TestMethod]
    public void ClassifyGpu_RadeonRx7900_IdentifiesRdna3WithUniversalBridge()
    {
        var info = new GpuHardwareInfo { Name = "AMD Radeon RX 7900 XTX" };
        GpuHardwareDetector.ClassifyGpu(info);

        Assert.AreEqual(GpuVendor.Amd, info.Vendor);
        Assert.IsTrue(info.Architecture.Contains("RDNA 3"));
        Assert.IsFalse(info.SupportsNativeDlssG);
        Assert.IsTrue(info.RecommendedFgMode.Contains("Universal FG Bridge"));
    }

    [TestMethod]
    public void ClassifyGpu_IntelArcA770_IdentifiesIntelArc()
    {
        var info = new GpuHardwareInfo { Name = "Intel(R) Arc(TM) A770 Graphics" };
        GpuHardwareDetector.ClassifyGpu(info);

        Assert.AreEqual(GpuVendor.Intel, info.Vendor);
        Assert.IsTrue(info.Architecture.Contains("Arc"));
        Assert.IsFalse(info.SupportsNativeDlssG);
    }

    [TestMethod]
    public void GetHardwareInfo_ReturnsValidDetectedInfo()
    {
        var info = GpuHardwareDetector.GetHardwareInfo();
        Assert.IsNotNull(info);
        Assert.IsFalse(string.IsNullOrWhiteSpace(info.Name));
        Assert.IsFalse(string.IsNullOrWhiteSpace(info.Architecture));
        Assert.IsFalse(string.IsNullOrWhiteSpace(info.SummaryText));
    }
}
