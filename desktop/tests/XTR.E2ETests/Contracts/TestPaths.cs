using System;
using System.IO;

namespace XTR.E2ETests.Contracts;

public static class TestPaths
{
    private static string? s_projectRoot;

    public static string ProjectRoot
    {
        get
        {
            if (s_projectRoot != null) return s_projectRoot;

            var current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "PROJECT.md")) &&
                    Directory.Exists(Path.Combine(current, "desktop")))
                {
                    s_projectRoot = Path.GetFullPath(current);
                    return s_projectRoot;
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate project root containing PROJECT.md and desktop/.");
        }
    }

    public static string BundledPackagePath =>
        Path.Combine(ProjectRoot, "desktop", "src", "Runtime", "OptiScaler");

    public static string OptiScalerIniPath =>
        Path.Combine(BundledPackagePath, "OptiScaler.ini");

    public static string XtrRuntimeJsonPath =>
        Path.Combine(BundledPackagePath, "xtr-runtime.json");

    public static string BuildWindowsScriptPath =>
        Path.Combine(ProjectRoot, "scripts", "build-windows.ps1");
}
