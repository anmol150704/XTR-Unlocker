using System;
using System.Globalization;

namespace XTR.E2ETests.Contracts;

public static class XtrHudFormatterContract
{
    // Hotkeys from ORIGINAL_REQUEST & rea_discovery_report
    public const int ShortcutKey_Insert = 0x2D;
    public const int FpsShortcutKey_F10 = 0x79;
    public const int FpsCycleShortcutKey_F9 = 0x78;
    public const int FGShortcutKey_F12 = 0x7B;

    // Apple Emerald Palette
    public const float AccentColorR = 0.20f;
    public const float AccentColorG = 0.78f;
    public const float AccentColorB = 0.35f;

    public const float BGColorR = 0.08f;
    public const float BGColorG = 0.09f;
    public const float BGColorB = 0.11f;
    public const float BGColorA = 0.92f;
    public const float FpsOverlayAlpha = 0.75f;

    public static string FormatHudString(float fps, uint multiplier, float frametimeMs, string? gpuName)
    {
        string safeGpu = string.IsNullOrWhiteSpace(gpuName) ? "Unknown GPU" : gpuName.Trim();
        int roundedFps = Math.Max(0, (int)Math.Round(fps));
        string formattedFt = Math.Max(0.0f, frametimeMs).ToString("F1", CultureInfo.InvariantCulture);

        if (multiplier > 1)
        {
            return $"[ XTR ENGINE ] • FPS: {roundedFps} (FG {multiplier}X) • {formattedFt} ms • {safeGpu}";
        }

        return $"[ XTR ENGINE ] • FPS: {roundedFps} • {formattedFt} ms • {safeGpu}";
    }

    public static string FormatFromPacket(in XtrTelemetryPacket packet)
    {
        string gpuName = packet.GetGpuName();
        return FormatHudString(packet.InGameFps, packet.Multiplier, packet.InGameFrametimeMs, gpuName);
    }
}
