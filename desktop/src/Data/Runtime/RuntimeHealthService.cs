using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DLSS_Swapper.Data.Runtime;

public enum RuntimeHealthState
{
    InstalledNotYetLaunched,
    HealthyActive,
    ProxyBypassed,
    ErrorDetected
}

public sealed class RuntimeHealthReport
{
    public RuntimeHealthState State { get; set; } = RuntimeHealthState.InstalledNotYetLaunched;
    public bool ProxyFilePresent { get; set; }
    public bool ProxyHookConfirmed { get; set; }
    public bool UpscalerActive { get; set; }
    public bool FrameGenerationActive { get; set; }
    public string? ActiveApi { get; set; }
    public string? ActiveUpscaler { get; set; }
    public string? ActiveFrameGen { get; set; }
    public string Summary { get; set; } = "Runtime installed; waiting for first launch.";
    public List<string> Details { get; set; } = new();
    public string? LogPath { get; set; }
    public DateTimeOffset? LastLogTimestamp { get; set; }
}

public static class RuntimeHealthService
{
    public static RuntimeHealthReport CheckHealth(string gameRoot, string proxyName = "dxgi.dll")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        var report = new RuntimeHealthReport();

        if (!Directory.Exists(gameRoot))
        {
            report.State = RuntimeHealthState.ErrorDetected;
            report.Summary = "Game folder not found.";
            return report;
        }

        var proxyPath = Path.Combine(gameRoot, proxyName);
        report.ProxyFilePresent = File.Exists(proxyPath);

        if (!report.ProxyFilePresent)
        {
            report.State = RuntimeHealthState.ErrorDetected;
            report.Summary = $"Proxy DLL ({proxyName}) is missing from game folder.";
            report.Details.Add($"Expected proxy file {proxyName} was not found in {gameRoot}.");
            return report;
        }

        var logPath = Path.Combine(gameRoot, "OptiScaler.log");
        if (!File.Exists(logPath))
        {
            report.State = RuntimeHealthState.InstalledNotYetLaunched;
            report.Summary = "Files installed · Launch the game to initialize runtime.";
            report.Details.Add($"Proxy {proxyName} is present on disk. OptiScaler has not generated OptiScaler.log yet.");
            return report;
        }

        report.LogPath = logPath;
        try
        {
            var fileInfo = new FileInfo(logPath);
            report.LastLogTimestamp = fileInfo.LastWriteTimeUtc;

            // Read the last 150 lines from the log file safely
            var lines = ReadTailLines(logPath, 150);
            report.ProxyHookConfirmed = lines.Any(l =>
                l.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Hook", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Init", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("CreateDevice", StringComparison.OrdinalIgnoreCase));

            // Detect Active API
            if (lines.Any(l => l.Contains("D3D12", StringComparison.OrdinalIgnoreCase) || l.Contains("DX12", StringComparison.OrdinalIgnoreCase)))
                report.ActiveApi = "DX12";
            else if (lines.Any(l => l.Contains("Vulkan", StringComparison.OrdinalIgnoreCase)))
                report.ActiveApi = "Vulkan";
            else if (lines.Any(l => l.Contains("D3D11", StringComparison.OrdinalIgnoreCase) || l.Contains("DX11", StringComparison.OrdinalIgnoreCase)))
                report.ActiveApi = "DX11";

            // Detect Active Upscaler
            if (lines.Any(l => l.Contains("DLSS", StringComparison.OrdinalIgnoreCase) && !l.Contains("DLSSG", StringComparison.OrdinalIgnoreCase)))
            {
                report.UpscalerActive = true;
                report.ActiveUpscaler = "DLSS";
            }
            else if (lines.Any(l => l.Contains("FSR", StringComparison.OrdinalIgnoreCase)))
            {
                report.UpscalerActive = true;
                report.ActiveUpscaler = "FSR";
            }
            else if (lines.Any(l => l.Contains("XeSS", StringComparison.OrdinalIgnoreCase)))
            {
                report.UpscalerActive = true;
                report.ActiveUpscaler = "XeSS";
            }

            // Detect Frame Generation
            if (lines.Any(l =>
                (l.Contains("FrameGen", StringComparison.OrdinalIgnoreCase) && l.Contains("Enabled", StringComparison.OrdinalIgnoreCase)) ||
                l.Contains("FGOutput", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("dlssg", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("fsrfg", StringComparison.OrdinalIgnoreCase)))
            {
                report.FrameGenerationActive = true;
                if (lines.Any(l => l.Contains("dlssg", StringComparison.OrdinalIgnoreCase)))
                    report.ActiveFrameGen = "DLSS-G native bridge";
                else if (lines.Any(l => l.Contains("fsrfg", StringComparison.OrdinalIgnoreCase)))
                    report.ActiveFrameGen = "FSR Frame Generation";
                else
                    report.ActiveFrameGen = "Frame Generation active";
            }

            // Check for Errors
            var errors = lines.Where(l =>
                l.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Failed to hook", StringComparison.OrdinalIgnoreCase)).ToList();

            if (errors.Count > 0)
            {
                report.State = RuntimeHealthState.ErrorDetected;
                report.Summary = "Error reported by runtime engine.";
                report.Details.AddRange(errors.Take(5));
            }
            else if (report.ProxyHookConfirmed)
            {
                report.State = RuntimeHealthState.HealthyActive;
                var upscalerStr = report.ActiveUpscaler ?? "Upscaler";
                var fgStr = report.FrameGenerationActive ? $" + {report.ActiveFrameGen}" : string.Empty;
                report.Summary = $"Active · {upscalerStr}{fgStr} confirmed ({report.ActiveApi ?? "DirectX"}).";
                report.Details.Add($"Proxy {proxyName} successfully hooked by game process.");
            }
            else
            {
                report.State = RuntimeHealthState.ProxyBypassed;
                report.Summary = "Proxy present but hook not yet confirmed.";
                report.Details.Add("Log file was present but does not show active interception calls.");
            }
        }
        catch (Exception ex)
        {
            report.State = RuntimeHealthState.ErrorDetected;
            report.Summary = "Failed to inspect runtime log.";
            report.Details.Add(ex.Message);
        }

        return report;
    }

    private static List<string> ReadTailLines(string path, int maxLines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var queue = new Queue<string>(maxLines);
            while (reader.ReadLine() is { } line)
            {
                if (queue.Count >= maxLines) queue.Dequeue();
                queue.Enqueue(line);
            }
            return queue.ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
