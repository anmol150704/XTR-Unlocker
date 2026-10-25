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
    public bool StreamlineHookConfirmed { get; set; }
    public bool SwapchainHookConfirmed { get; set; }
    public bool GeneratedFramesConfirmed { get; set; }
    public bool UpscalerActive { get; set; }
    public bool FrameGenerationActive { get; set; }
    public string? ActiveApi { get; set; }
    public string? ActiveUpscaler { get; set; }
    public string? ActiveFrameGen { get; set; }
    public string InGameOverlayGuide { get; set; } = "[Insert] In-Game Menu - [Page Up] FPS & FG Overlay";
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
            report.Summary = "Files installed - Launch the game to initialize runtime.";
            report.Details.Add($"Proxy {proxyName} is present on disk. OptiScaler has not generated OptiScaler.log yet.");
            return report;
        }

        report.LogPath = logPath;
        try
        {
            var fileInfo = new FileInfo(logPath);
            report.LastLogTimestamp = fileInfo.LastWriteTimeUtc;

            // Read the last 200 lines from the log file safely
            var lines = ReadTailLines(logPath, 200);
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

            // Streamline hooks
            report.StreamlineHookConfirmed = lines.Any(l =>
                l.Contains("Streamline", StringComparison.OrdinalIgnoreCase) &&
                (l.Contains("hook", StringComparison.OrdinalIgnoreCase) ||
                 l.Contains("slInit", StringComparison.OrdinalIgnoreCase) ||
                 l.Contains("slSetTag", StringComparison.OrdinalIgnoreCase)));

            // Swapchain and Frame Generation hooks
            report.SwapchainHookConfirmed = lines.Any(l =>
                l.Contains("HookFGSwapchain", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("CreateSwapChain", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("FSRFG_Dx12", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("currentFGSwapchain", StringComparison.OrdinalIgnoreCase));

            report.GeneratedFramesConfirmed = lines.Any(l =>
                l.Contains("Frametime:", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("FGLastFrame", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("fg->Present", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("FGPresent", StringComparison.OrdinalIgnoreCase));

            var fgConfigured = lines.Any(l =>
                (l.Contains("FrameGen", StringComparison.OrdinalIgnoreCase) && l.Contains("Enabled", StringComparison.OrdinalIgnoreCase)) ||
                l.Contains("FGInput", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("FGOutput", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("dlssg", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("fsrfg", StringComparison.OrdinalIgnoreCase));

            if (report.GeneratedFramesConfirmed)
            {
                report.FrameGenerationActive = true;
                report.ActiveFrameGen = report.StreamlineHookConfirmed
                    ? "DLSS-G Intercept + Hardware Frame Generation (Active)"
                    : "Hardware Frame Generation (Active)";
            }
            else if (report.SwapchainHookConfirmed || fgConfigured)
            {
                report.FrameGenerationActive = true;
                report.ActiveFrameGen = report.StreamlineHookConfirmed
                    ? "DLSS-G Streamline Hooked (Standby for 3D load)"
                    : "Frame Generation Initialized (Standby for 3D load)";
            }

            // Check for Errors
            var errors = lines.Where(l =>
                l.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Failed to hook", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Can't init FG Feature", StringComparison.OrdinalIgnoreCase)).ToList();

            if (errors.Count > 0)
            {
                report.State = RuntimeHealthState.ErrorDetected;
                report.Summary = "Error reported by runtime engine.";
                report.Details.AddRange(errors.Take(5));
            }
            else if (report.ProxyHookConfirmed)
            {
                report.State = RuntimeHealthState.HealthyActive;
                var upscalerStr = report.ActiveUpscaler ?? "DLSS / Upscaler";
                var fgStr = report.FrameGenerationActive ? $" + {report.ActiveFrameGen}" : string.Empty;
                report.Summary = $"Active - {upscalerStr}{fgStr} ({report.ActiveApi ?? "DirectX"}).";
                report.Details.Add($"Proxy {proxyName} successfully hooked by game process.");
                if (report.StreamlineHookConfirmed)
                    report.Details.Add("Engine Streamline DLSS-G tags successfully intercepted.");
                if (report.GeneratedFramesConfirmed)
                    report.Details.Add("Interpolated frames actively presenting to display.");
                report.Details.Add($"In-Game Overlay: {report.InGameOverlayGuide}");
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