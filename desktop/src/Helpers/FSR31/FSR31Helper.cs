using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DLSS_Swapper.Helpers.FSR31;

internal class FSR31Helper
{
    public static List<string?> GetVersions(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            return new List<string?>();
        }

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(dllPath);
            var version = vi.ProductVersion ?? vi.FileVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return new List<string?> { version.Trim() };
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read FileVersionInfo from {Path}", dllPath);
        }

        return new List<string?>();
    }

    public static string GetLatestVersion(string dllPath)
    {
        var versions = GetVersions(dllPath);
        return versions.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }
}
