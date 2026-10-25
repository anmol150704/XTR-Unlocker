using System;
using System.Linq;

namespace DLSS_Swapper.Data.Runtime;

public static class DlssVersionHelper
{
    public static string FormatGeneration(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "No DLSS";
        }

        var ver = version.TrimStart('v', 'V');
        var firstPart = ver.Split('.')[0];
        if (int.TryParse(firstPart, out var major))
        {
            return $"DLSS {major}";
        }

        return "DLSS";
    }

    public static string FormatVersionLabel(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "DLSS not detected";
        }

        var ver = CleanVersion(version);
        var firstPart = ver.Split('.')[0];
        if (int.TryParse(firstPart, out var major))
        {
            return $"DLSS {major} - v{ver}";
        }

        return $"DLSS v{ver}";
    }

    public static string FormatBadge(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "NO DLSS";
        }

        var ver = CleanVersion(version);
        var firstPart = ver.Split('.')[0];
        if (int.TryParse(firstPart, out var major))
        {
            return $"DLSS {major} - v{ver}";
        }

        return $"DLSS v{ver}";
    }

    public static string CleanVersion(string version)
    {
        var ver = version.TrimStart('v', 'V');
        while (ver.EndsWith(".0") && ver.Count(c => c == '.') > 1)
        {
            ver = ver[..^2];
        }

        return ver;
    }
}
