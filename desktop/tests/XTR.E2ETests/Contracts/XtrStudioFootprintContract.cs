using System;
using System.IO;
using System.Reflection;

namespace XTR.E2ETests.Contracts;

public static class XtrStudioFootprintContract
{
    public const double MaxWorkingSetMbBudget = 45.0;
    public const double MinWorkingSetMbBudget = 35.0;
    public const int TargetColdStartupBudgetMs = 200;
    public const int MaxColdStartupBudgetMs = 250;
    public const double BackgroundGamingCpuPercentBudget = 0.0;

    // DwmSetWindowAttribute Windows 11 Fluent Constants
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;       // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;     // Mica Alt

    private static readonly string[] s_prohibitedWebDependencies =
    [
        "Microsoft.Web.WebView2.Core",
        "Microsoft.Web.WebView2.WinForms",
        "Microsoft.Web.WebView2.Wpf",
        "CefSharp",
        "CefSharp.Wpf",
        "Electron"
    ];

    public static bool IsProhibitedWebDependency(string assemblyName)
    {
        foreach (var prohibited in s_prohibitedWebDependencies)
        {
            if (assemblyName.Contains(prohibited, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool ValidateAssemblyReferences(Assembly assembly, out string? offendingDependency)
    {
        offendingDependency = null;
        var referencedAssemblies = assembly.GetReferencedAssemblies();
        foreach (var refAsm in referencedAssemblies)
        {
            if (IsProhibitedWebDependency(refAsm.Name ?? string.Empty))
            {
                offendingDependency = refAsm.Name;
                return false;
            }
        }
        return true;
    }
}
