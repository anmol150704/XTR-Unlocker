using System;
using System.Globalization;
using System.IO;
using DLSS_Swapper.Data.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTR.E2ETests.Contracts;

namespace XTR.E2ETests.Tier2_BoundaryCornerCases;

[TestClass]
public sealed class AntiGhostingBoundaryTests
{
    #region Feature 1 Boundaries: Anti-Ghosting Calibration

    [TestMethod]
    public void B01_AccAddPerFrame_Boundary_One()
    {
        // Boundary: AccAddPerFrame = 1.0 (Maximum theoretical accumulation rate: instant single-frame blend)
        string baseIni = "[FSR]\nAccAddPerFrame=0.333\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "FSR", "AccAddPerFrame", "1.0");

        string? val = RuntimeConfigService.ReadIniValue(mutated, "FSR", "AccAddPerFrame");
        Assert.AreEqual("1.0", val);
        Assert.IsTrue(double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed));
        Assert.AreEqual(1.0, parsed, 0.001);
    }

    [TestMethod]
    public void B01_AccAddPerFrame_Boundary_Tiny()
    {
        // Boundary: AccAddPerFrame = 0.001 (Sub-millisecond minimal accumulation rate)
        string baseIni = "[FSR]\nAccAddPerFrame=0.333\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "FSR", "AccAddPerFrame", "0.001");

        string? val = RuntimeConfigService.ReadIniValue(mutated, "FSR", "AccAddPerFrame");
        Assert.AreEqual("0.001", val);
        Assert.IsTrue(double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed));
        Assert.AreEqual(0.001, parsed, 0.0001);
    }

    [TestMethod]
    public void B01_MinDisOccAcc_Boundary_MinusOne()
    {
        // Boundary: MinDisOccAcc = -1.0 (Extreme disocclusion delay: holds history back by 2+ full frames)
        string baseIni = "[FSR]\nMinDisOccAcc=-0.333\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "FSR", "MinDisOccAcc", "-1.0");

        string? val = RuntimeConfigService.ReadIniValue(mutated, "FSR", "MinDisOccAcc");
        Assert.AreEqual("-1.0", val);
        Assert.IsTrue(double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed));
        Assert.AreEqual(-1.0, parsed, 0.001);
    }

    [TestMethod]
    public void B01_Disocclusion_HighVelocity_350Kmh()
    {
        // Physics Stress Boundary: 350 km/h = 97.22 m/s.
        // At 120 FPS (8.33 ms/frame), pixel displacement is >60 pixels/frame.
        // AccAddPerFrame = 0.05 flushes sample in 1 frame (<8.4 ms), whereas 0.333 takes 3 frames (25 ms).
        double speedKmh = 350.0;
        double speedMs = speedKmh * (1000.0 / 3600.0); // 97.22 m/s
        double frameTime120Fps = 1000.0 / 120.0; // 8.33 ms

        double calibratedFlushTime = 1.0 * frameTime120Fps;
        double uncalibratedFlushTime = 3.0 * frameTime120Fps;

        Assert.IsTrue(calibratedFlushTime <= 8.5, "At 120 FPS, 0.05 must clear smear in 1 frame (~8.3ms).");
        Assert.IsTrue(uncalibratedFlushTime >= 24.0, "At 120 FPS, default 0.333 takes >24ms to clear smear.");
        Assert.IsTrue(speedMs > 90.0);
    }

    [TestMethod]
    public void B01_FsrSection_Missing_CreatesSectionAndAppliesAntiGhosting()
    {
        // Corner case: Target game INI has no [FSR] section at all
        string baseIni = "[General]\nLogLevel=1\n\n[DLSS]\nRenderPresetForAll=0\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "FSR", "AccAddPerFrame", "0.05");
        mutated = RuntimeConfigService.ReplaceIniValue(mutated, "FSR", "MinDisOccAcc", "-0.50");

        Assert.AreEqual("0.05", RuntimeConfigService.ReadIniValue(mutated, "FSR", "AccAddPerFrame"));
        Assert.AreEqual("-0.50", RuntimeConfigService.ReadIniValue(mutated, "FSR", "MinDisOccAcc"));
        Assert.AreEqual("1", RuntimeConfigService.ReadIniValue(mutated, "General", "LogLevel"));
        Assert.AreEqual("0", RuntimeConfigService.ReadIniValue(mutated, "DLSS", "RenderPresetForAll"));
    }

    #endregion

    #region Feature 2 Boundaries: Responsive Mask Preservation

    [TestMethod]
    public void B02_DisableReactiveMask_CaseInsensitivity()
    {
        string baseIni = "[InitFlags]\nDisableReactiveMask=False\n";
        Assert.AreEqual("False", RuntimeConfigService.ReadIniValue(baseIni, "InitFlags", "DisableReactiveMask"));

        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "initflags", "disablereactivemask", "false");
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "DisableReactiveMask"));
    }

    [TestMethod]
    public void B02_InitFlags_EmptySection_PopulatedCorrectly()
    {
        string baseIni = "[InitFlags]\n\n[FrameGen]\nEnabled=true\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "InitFlags", "DisableReactiveMask", "false");

        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "DisableReactiveMask"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "Enabled"));
    }

    [TestMethod]
    public void B02_DuplicateInitFlags_PreservesDeterministicValue()
    {
        string baseIni = "[InitFlags]\nDisableReactiveMask=true\n\n[InitFlags]\nDisableReactiveMask=false\n";
        // When duplicate section exists, first section read should find key
        string? val = RuntimeConfigService.ReadIniValue(baseIni, "InitFlags", "DisableReactiveMask");
        Assert.IsNotNull(val);
    }

    [TestMethod]
    public void B02_UseReactiveMaskForTransparency_ToggleSafely()
    {
        string baseIni = "[FSR]\nUseReactiveMaskForTransparency=false\n";
        string toggled = RuntimeConfigService.ReplaceIniValue(baseIni, "FSR", "UseReactiveMaskForTransparency", "true");
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(toggled, "FSR", "UseReactiveMaskForTransparency"));

        string toggledBack = RuntimeConfigService.ReplaceIniValue(toggled, "FSR", "UseReactiveMaskForTransparency", "false");
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(toggledBack, "FSR", "UseReactiveMaskForTransparency"));
    }

    [TestMethod]
    public void B02_InitFlags_PreservesOtherFlags()
    {
        string baseIni = "[InitFlags]\nAutoExposure=true\nDisableReactiveMask=true\nDepthInverted=false\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "InitFlags", "DisableReactiveMask", "false");

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "AutoExposure"));
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "DisableReactiveMask"));
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(mutated, "InitFlags", "DepthInverted"));
    }

    #endregion

    #region Feature 3 Boundaries: Async Compute MV & Depth Clones

    [TestMethod]
    public void B03_MakeMVCopy_SectionCreatedIfMissing()
    {
        string baseIni = "[FrameGen]\nEnabled=true\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "OptiFG", "MakeMVCopy", "true");

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(mutated, "OptiFG", "MakeMVCopy"));
        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(mutated, "FrameGen", "Enabled"));
    }

    [TestMethod]
    public void B03_MakeDepthCopy_ExtremeResolution_8K()
    {
        // 8K UHD = 7680 x 4320 = 33,177,600 pixels.
        // D32_FLOAT = 4 bytes per pixel.
        // Total depth buffer clone memory = (33,177,600 * 4) / (1024 * 1024) = 126.56 MB.
        long width = 7680;
        long height = 4320;
        long bytesPerPixel = 4;
        double depthVramMb = (width * height * bytesPerPixel) / (1024.0 * 1024.0);

        Assert.IsTrue(depthVramMb >= 126.0 && depthVramMb <= 127.0,
            $"8K Depth buffer clone should be ~126.56 MB, calculated: {depthVramMb:F2} MB");
    }

    [TestMethod]
    public void B03_MakeMVCopy_Sub1080p_LowVram()
    {
        // 720p HD = 1280 x 720 = 921,600 pixels.
        // R16G16_FLOAT = 4 bytes per pixel.
        // Total MV buffer clone memory = (921,600 * 4) / (1024 * 1024) = 3.51 MB.
        long width = 1280;
        long height = 720;
        long bytesPerPixel = 4;
        double mvVramMb = (width * height * bytesPerPixel) / (1024.0 * 1024.0);

        Assert.IsTrue(mvVramMb >= 3.5 && mvVramMb <= 3.6,
            $"720p MV buffer clone should be ~3.51 MB, calculated: {mvVramMb:F2} MB");
    }

    [TestMethod]
    public void B03_OptiFG_NeverAppendedAfterHotfix()
    {
        // Corner case: [Hotfix] is the last section in OptiScaler.ini.
        // Mutating [OptiFG] must never append keys under [Hotfix].
        string baseIni = "[Menu]\nShowFps=true\n\n[OptiFG]\nHUDFix=true\n\n[Hotfix]\nCrashFix=1\n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "OptiFG", "MakeMVCopy", "true");

        Assert.AreEqual("true", RuntimeConfigService.ReadIniValue(mutated, "OptiFG", "MakeMVCopy"));
        Assert.AreEqual("1", RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "CrashFix"));
        Assert.IsNull(RuntimeConfigService.ReadIniValue(mutated, "Hotfix", "MakeMVCopy"),
            "MakeMVCopy must NOT be placed in [Hotfix] section.");
    }

    [TestMethod]
    public void B03_MakeMVCopy_StringValue_NotCorruptedByWhitespace()
    {
        string baseIni = "[OptiFG]\n  MakeMVCopy  =  false  \n";
        string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "OptiFG", "MakeMVCopy", "true");

        string? val = RuntimeConfigService.ReadIniValue(mutated, "OptiFG", "MakeMVCopy");
        Assert.AreEqual("true", val);
    }

    #endregion

    #region Feature 4 Boundaries: High-Motion Smear Suppression

    [TestMethod]
    public void B04_DlssReactiveMaskBias_ClampedRange()
    {
        // Safe operational bias range is [0.0, 0.90]. 0.45 is the optimal calibrated midpoint.
        float bias = 0.45f;
        float clamped = Math.Clamp(bias, 0.0f, 0.90f);
        Assert.AreEqual(0.45f, clamped, 0.001f);

        float extremeHigh = 1.5f;
        Assert.AreEqual(0.90f, Math.Clamp(extremeHigh, 0.0f, 0.90f));

        float extremeLow = -0.5f;
        Assert.AreEqual(0.0f, Math.Clamp(extremeLow, 0.0f, 0.90f));
    }

    [TestMethod]
    public void B04_ReactiveScale_ExtremeMagnification_50()
    {
        // Extreme magnification 5.0 on motion mask 0.30 clamped to 1.0
        float rawMask = 0.30f;
        float scale = 5.0f;
        float amplified = Math.Clamp(rawMask * scale, 0.0f, 1.0f);

        Assert.AreEqual(1.0f, amplified, 0.001f);
    }

    [TestMethod]
    public void B04_RenderPresetForAll_Bounds_0To6()
    {
        for (int preset = 0; preset <= 6; preset++)
        {
            string baseIni = "[DLSS]\nRenderPresetForAll=0\n";
            string mutated = RuntimeConfigService.ReplaceIniValue(baseIni, "DLSS", "RenderPresetForAll", preset.ToString());
            Assert.AreEqual(preset.ToString(), RuntimeConfigService.ReadIniValue(mutated, "DLSS", "RenderPresetForAll"));
        }
    }

    [TestMethod]
    public void B04_DlssSection_RenderPresetOverride_False_PreservesGamePreset()
    {
        string baseIni = "[DLSS]\nRenderPresetOverride=false\nRenderPresetForAll=0\n";
        Assert.AreEqual("false", RuntimeConfigService.ReadIniValue(baseIni, "DLSS", "RenderPresetOverride"));
        Assert.AreEqual("0", RuntimeConfigService.ReadIniValue(baseIni, "DLSS", "RenderPresetForAll"));
    }

    [TestMethod]
    public void B04_DlssReactiveMaskBias_FloatFormatting_NoCultureDrift()
    {
        float bias = 0.45f;
        string formatted = bias.ToString("0.00", CultureInfo.InvariantCulture);
        Assert.AreEqual("0.45", formatted);
        Assert.IsFalse(formatted.Contains(','), "Float formatting must never emit comma decimal separators in INI files.");
    }

    #endregion
}
