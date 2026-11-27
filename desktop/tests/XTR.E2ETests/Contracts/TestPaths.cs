using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace XTR.E2ETests.Contracts;

public static class TestPaths
{
    private static string? s_projectRoot;

    public static string ProjectRoot => s_projectRoot ??= ResolveProjectRoot();

    private static string ResolveProjectRoot()
    {
        foreach (var candidateStart in new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            GetCallerSourceDirectory()
        })
        {
            var found = FindRootUpward(candidateStart);
            if (found != null)
                return found;
        }

        throw new DirectoryNotFoundException(
            "Could not locate project root containing PROJECT.md and desktop/ from AppContext.BaseDirectory, CurrentDirectory, or CallerFilePath.");
    }

    private static string? FindRootUpward(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            return null;

        var current = Path.GetFullPath(startDirectory);
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "PROJECT.md")) &&
                Directory.Exists(Path.Combine(current, "desktop")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return null;
    }

    private static string? GetCallerSourceDirectory([CallerFilePath] string callerFilePath = "") =>
        string.IsNullOrWhiteSpace(callerFilePath) ? null : Path.GetDirectoryName(callerFilePath);

    public static string DesktopSrcRoot =>
        Path.Combine(ProjectRoot, "desktop", "src");

    public static string BundledPackagePath =>
        Path.Combine(DesktopSrcRoot, "Runtime", "OptiScaler");

    public static string OptiScalerIniPath =>
        Path.Combine(BundledPackagePath, "OptiScaler.ini");

    public static string XtrRuntimeJsonPath =>
        Path.Combine(BundledPackagePath, "xtr-runtime.json");

    public static string XtrHudAsiPath =>
        Path.Combine(BundledPackagePath, "plugins", "xtr_hud.asi");

    public static string AppXamlPath =>
        Path.Combine(DesktopSrcRoot, "App.xaml");

    public static string XtrDesignXamlPath =>
        Path.Combine(DesktopSrcRoot, "Themes", "XtrDesign.xaml");

    public static string MainWindowXamlPath =>
        Path.Combine(DesktopSrcRoot, "MainWindow.xaml");

    public static string XtrStudioPageXamlPath =>
        Path.Combine(DesktopSrcRoot, "Pages", "XtrStudioPage.xaml");

    public static string GameControlXamlPath =>
        Path.Combine(DesktopSrcRoot, "UserControls", "GameControl.xaml");

    public static string DesktopProjectPath =>
        Path.Combine(DesktopSrcRoot, "DLSS Swapper.csproj");

    public static string XtrTestsProjectPath =>
        Path.Combine(ProjectRoot, "desktop", "tests", "XTR.Tests", "XTR.Tests.csproj");

    public static string XtrE2ETestsProjectPath =>
        Path.Combine(ProjectRoot, "desktop", "tests", "XTR.E2ETests", "XTR.E2ETests.csproj");

    public static string BuildWindowsScriptPath =>
        Path.Combine(ProjectRoot, "scripts", "build-windows.ps1");

    public static string ReleaseWorkflowPath =>
        Path.Combine(ProjectRoot, ".github", "workflows", "release.yml");

    public static string DistRootPath =>
        Path.Combine(ProjectRoot, "dist");
}
