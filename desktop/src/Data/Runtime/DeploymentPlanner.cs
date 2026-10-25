using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

public enum RuntimeIntegrationPreset
{
    LatestDlssOnly,
    DlssWithFrameGeneration,
    Performance,
    Quality,
    RestoreOriginal
}

public sealed class PlannedFileAction
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public bool WillOverwriteExisting { get; set; }
    public bool RequiresBackup { get; set; }
}

public sealed class PlannedBackupAction
{
    public string GameRelativePath { get; set; } = string.Empty;
    public string ExistingSha256 { get; set; } = string.Empty;
}

public sealed class PlannedConfigEntry
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class DeploymentPlan
{
    public string GameId { get; set; } = string.Empty;
    public string GameTitle { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public RuntimeIntegrationPreset Preset { get; set; } = RuntimeIntegrationPreset.LatestDlssOnly;
    public RuntimePack Pack { get; set; } = null!;
    public string SelectedProxy { get; set; } = "dxgi.dll";
    public RuntimePreflightResult PreflightResult { get; set; } = null!;
    public List<PlannedFileAction> PlannedCopies { get; set; } = new();
    public List<PlannedBackupAction> PlannedBackups { get; set; } = new();
    public List<PlannedConfigEntry> PlannedConfigModifications { get; set; } = new();
    public bool CanExecute { get; set; } = true;
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public static class DeploymentPlanner
{
    public static async Task<DeploymentPlan> CreatePlanAsync(
        string gameId,
        string gameTitle,
        string gameRoot,
        RuntimePack pack,
        RuntimeIntegrationPreset preset,
        string? proxyOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentNullException.ThrowIfNull(pack);

        var proxy = proxyOverride ?? pack.RecommendedProxy ?? "dxgi.dll";
        var preflight = await RuntimePreflightService.RunPreflightAsync(gameId, gameRoot, pack, proxy, cancellationToken).ConfigureAwait(false);

        var plan = new DeploymentPlan
        {
            GameId = gameId,
            GameTitle = gameTitle,
            GameRoot = gameRoot,
            Preset = preset,
            Pack = pack,
            SelectedProxy = proxy,
            PreflightResult = preflight,
            CanExecute = preflight.CanProceed
        };

        if (!plan.CanExecute)
        {
            plan.Headline = preflight.Headline;
            plan.Summary = preflight.Explanation;
            return plan;
        }

        // Determine file deployments from package
        var packageRoot = pack.PackageRoot;
        if (packageRoot is not null && Directory.Exists(packageRoot))
        {
            var files = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(packageRoot, file);
                if (relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relative, "setup_windows.bat", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relative, "setup_linux.sh", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relative, "xtr-runtime.json", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("!!", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetRelative = string.Equals(relative, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase)
                    ? proxy
                    : relative;

                var destinationPath = Path.Combine(gameRoot, targetRelative);
                var willOverwrite = File.Exists(destinationPath);
                string existingHash = string.Empty;

                if (willOverwrite)
                {
                    existingHash = await ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
                    plan.PlannedBackups.Add(new PlannedBackupAction
                    {
                        GameRelativePath = targetRelative.Replace('\\', '/'),
                        ExistingSha256 = existingHash
                    });
                }

                var sourceHash = pack.FileHashes.FirstOrDefault(h => string.Equals(h.RelativePath, relative.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))?.Sha256
                    ?? await ComputeSha256Async(file, cancellationToken).ConfigureAwait(false);

                plan.PlannedCopies.Add(new PlannedFileAction
                {
                    SourcePath = file,
                    DestinationPath = destinationPath,
                    RelativePath = targetRelative.Replace('\\', '/'),
                    Sha256 = sourceHash,
                    WillOverwriteExisting = willOverwrite,
                    RequiresBackup = willOverwrite
                });
            }
        }

        // Determine configuration settings based on preset & detected API
        var api = preflight.DetectedApi;
        switch (preset)
        {
            case RuntimeIntegrationPreset.LatestDlssOnly:
                plan.Headline = "Enable latest DLSS";
                plan.Summary = $"Configures {pack.DisplayName} to route upscaling through DLSS on {api}. No frame generation forced.";
                var upscalerKey = api switch
                {
                    "DX11" => "Dx11Upscaler",
                    "DX12" => "Dx12Upscaler",
                    "Vulkan" => "VulkanUpscaler",
                    _ => "Dx12Upscaler"
                };
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = upscalerKey,
                    Value = "auto",
                    Reason = "Directs OptiScaler to select the best available upscaler automatically."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGInput",
                    Value = "auto",
                    Reason = "Preserves game-side frame generation settings."
                });
                break;

            case RuntimeIntegrationPreset.DlssWithFrameGeneration:
                plan.Headline = "Enable DLSS + Frame Generation";
                plan.Summary = $"Configures {pack.DisplayName} with frame generation bridge on {api}.";
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = api == "DX11" ? "Dx11Upscaler" : "Dx12Upscaler",
                    Value = "auto",
                    Reason = "Automatic upscaler selection."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGInput",
                    Value = "auto",
                    Reason = "Intercepts native frame-generation inputs."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGOutput",
                    Value = "auto",
                    Reason = "Routes generated frames through the compatible bridge."
                });
                break;

            case RuntimeIntegrationPreset.Performance:
                plan.Headline = "Performance preset";
                plan.Summary = "Optimizes configuration for frame rate and lower latency.";
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = api == "DX11" ? "Dx11Upscaler" : "Dx12Upscaler",
                    Value = "auto",
                    Reason = "Automatic high-performance upscaler."
                });
                break;

            case RuntimeIntegrationPreset.Quality:
                plan.Headline = "Quality preset";
                plan.Summary = "Optimizes configuration for image fidelity and stability.";
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = api == "DX11" ? "Dx11Upscaler" : "Dx12Upscaler",
                    Value = "auto",
                    Reason = "Automatic high-fidelity upscaler."
                });
                break;

            case RuntimeIntegrationPreset.RestoreOriginal:
                plan.Headline = "Restore game files";
                plan.Summary = "Removes runtime deployment and restores original backed-up game files.";
                break;
        }

        return plan;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
