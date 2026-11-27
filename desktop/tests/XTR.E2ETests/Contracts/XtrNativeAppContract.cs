using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace XTR.E2ETests.Contracts;

public static class XtrNativeAppContract
{
    // Windows 11 Fluent / DWM backdrop attribute constants (dwmapi.h)
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMSBT_MAINWINDOW = 2;
    public const int DWMSBT_TRANSIENTWINDOW = 3;
    public const int DWMSBT_TABBEDWINDOW = 4;

    private static readonly string[] s_prohibitedDependencies =
    [
        "Microsoft.Web.WebView2.Core",
        "Microsoft.Web.WebView2.WinForms",
        "Microsoft.Web.WebView2.Wpf",
        "System.Drawing.Common",
        "CommunityToolkit.WinUI.UI.Controls.DataGrid",
        "NvAPIWrapper",
        "CefSharp",
        "Electron"
    ];

    public static IReadOnlyList<string> FindAllCompiledAppAssemblyPaths()
    {
        var candidates = new[]
        {
            Path.Combine(TestPaths.DistRootPath, "XTR-Unlocker-Release-win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DistRootPath, "XTR-Unlocker-Release_Portable-win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "x64", "Release", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "x64", "Release_Portable", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "x64", "Debug", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "x64", "Debug_Portable", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "Release", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "Release_Portable", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll"),
            Path.Combine(TestPaths.DesktopSrcRoot, "bin", "Debug", "net10.0-windows10.0.26100.0", "win-x64", "XTR Unlocker.dll")
        };

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public static string? FindCompiledAppAssemblyPath() =>
        FindAllCompiledAppAssemblyPaths().FirstOrDefault();

    public static bool IsProhibitedDependency(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return false;

        foreach (var prohibited in s_prohibitedDependencies)
        {
            if (assemblyName.Contains(prohibited, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool ValidateProjectDependencies(
        string csprojPath,
        out List<string> packageReferences,
        out List<string> offendingDependencies)
    {
        packageReferences = [];
        offendingDependencies = [];

        var doc = XDocument.Load(csprojPath);
        foreach (var elem in doc.Descendants().Where(e =>
                     e.Name.LocalName is "PackageReference" or "Reference"))
        {
            string? include = elem.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            if (elem.Name.LocalName == "PackageReference")
                packageReferences.Add(include);

            if (IsProhibitedDependency(include))
                offendingDependencies.Add(include);
        }

        return offendingDependencies.Count == 0;
    }

    public static List<string> FindMonitoringCompileReferencesInCsproj(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var hits = new List<string>();
        foreach (var elem in doc.Descendants().Where(e =>
                     e.Name.LocalName is "Compile" or "Page" or "Content"))
        {
            string? include = elem.Attribute("Include")?.Value ?? elem.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            if (include.Contains("Engine\\IPC", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("Engine/IPC", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("Engine\\Telemetry", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("Engine/Telemetry", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("Engine\\Watcher", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("Engine/Watcher", StringComparison.OrdinalIgnoreCase) ||
                include.Contains("xtr_hud", StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(include);
            }
        }

        return hits;
    }

    public static List<string> FindProhibitedMonitoringSourceFiles(string desktopSrcRoot)
    {
        var hits = new List<string>();
        string[] bannedTokens =
        [
            "PresentMonTelemetryEngine",
            "XtrSharedMemory",
            "XtrSeqlock",
            "LibraryGameProcessWatcher",
            "HudOverlay"
        ];

        foreach (var file in Directory.EnumerateFiles(desktopSrcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(desktopSrcRoot, file);
            if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = Path.GetFileName(file);
            if (bannedTokens.Any(t => fileName.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                hits.Add(rel);
                continue;
            }

            string content = File.ReadAllText(file);
            if (content.Contains("namespace DLSS_Swapper.Engine.Telemetry", StringComparison.Ordinal) ||
                content.Contains("namespace DLSS_Swapper.Engine.IPC", StringComparison.Ordinal) ||
                content.Contains("namespace DLSS_Swapper.Engine.Watcher", StringComparison.Ordinal))
            {
                hits.Add(rel);
            }
        }

        return hits;
    }

    public static bool ValidateAssemblyReferences(Assembly assembly, out string? offendingDependency)
    {
        offendingDependency = null;
        foreach (var refAsm in assembly.GetReferencedAssemblies())
        {
            if (IsProhibitedDependency(refAsm.Name ?? string.Empty))
            {
                offendingDependency = refAsm.Name;
                return false;
            }
        }

        return true;
    }

    public static bool ValidatePeAssemblyReferences(string assemblyPath, out List<string> offendingDependencies)
    {
        offendingDependencies = [];
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var mr = pe.GetMetadataReader();

        foreach (var handle in mr.AssemblyReferences)
        {
            var refName = mr.GetString(mr.GetAssemblyReference(handle).Name);
            if (IsProhibitedDependency(refName))
            {
                offendingDependencies.Add(refName);
            }
        }

        return offendingDependencies.Count == 0;
    }

    public static List<string> FindProhibitedMonitoringTypeNames(string assemblyPath)
    {
        var offendingTypes = new List<string>();
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var mr = pe.GetMetadataReader();

        foreach (var handle in mr.TypeDefinitions)
        {
            var typeDef = mr.GetTypeDefinition(handle);
            var ns = mr.GetString(typeDef.Namespace);
            var name = mr.GetString(typeDef.Name);
            var full = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            if (full.Contains("Telemetry", StringComparison.OrdinalIgnoreCase) ||
                full.Contains("PresentMon", StringComparison.OrdinalIgnoreCase) ||
                full.Contains("XtrSeqlock", StringComparison.OrdinalIgnoreCase) ||
                full.Contains("ProcessWatcher", StringComparison.OrdinalIgnoreCase) ||
                full.Contains("HudOverlay", StringComparison.OrdinalIgnoreCase))
            {
                offendingTypes.Add(full);
            }
        }

        return offendingTypes;
    }
}
