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
    NativeDlssG,
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
        var preflight = await RuntimePreflightService.RunPreflightAsync(gameId, gameRoot, pack, proxy, gameTitle, cancellationToken).ConfigureAwait(false);

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
                plan.Summary = $"Configures {pack.DisplayName} with native DLSS engine inputs and hardware frame generation on {api}.";
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = api switch
                    {
                        "DX11" => "Dx11Upscaler",
                        "DX12" => "Dx12Upscaler",
                        "Vulkan" => "VulkanUpscaler",
                        _ => "Dx12Upscaler"
                    },
                    Value = "dlss",
                    Reason = "Directs OptiScaler to prioritize DLSS upscaling on supported hardware."
                });

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "Enabled",
                    Value = "true",
                    Reason = "Activates the frame-generation pipeline."
                });

                // Under REA audit: OptiScaler requires explicit non-NoFG input to hook Streamline or upscaler
                var fgInput = (preflight.NativeDlssGDetected || preflight.StreamlineDetected)
                    ? "dlssg"
                    : "upscaler";

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGInput",
                    Value = fgInput,
                    Reason = fgInput == "dlssg"
                        ? "Intercepts native DLSS-G motion vectors, depth, and hudless buffers from the game engine."
                        : "Derives frame-generation motion vectors from the active DLSS upscaler."
                });

                // Under REA audit of OptiScaler 0.9.4 FG_Hooks.cpp:88, CheckForFGStatus() strictly requires
                // activeFgOutput to be FSRFG or XeFG for swapchain frame interpolation.
                var fgOutput = "fsrfg";

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGOutput",
                    Value = fgOutput,
                    Reason = "Routes frame generation through the verified DirectX 12 hardware frame interpolation pipeline."
                });

                // Spoof Ada Lovelace (RTX 4090) to Streamline and DXGI so games unlock the native DLSS-G switch in menu
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Spoofing",
                    Key = "StreamlineSpoofing",
                    Value = "true",
                    Reason = "Unlocks the native DLSS Frame Generation toggle in game settings on RTX 20/30 GPUs."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Spoofing",
                    Key = "SpoofHAGS",
                    Value = "true",
                    Reason = "Ensures Hardware Accelerated GPU Scheduling requirements are satisfied."
                });

                // Enable OptiScaler in-game detection overlay so the user can verify real-time FPS and FG status
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Menu",
                    Key = "OverlayMenu",
                    Value = "true",
                    Reason = "Enables OptiScaler in-game overlay and frame interpolation swapchain support."
                });

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Menu",
                    Key = "ShowFps",
                    Value = "true",
                    Reason = "Renders real-time in-game detection overlay with frame generation status."
                });

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Menu",
                    Key = "FpsOverlayType",
                    Value = "2",
                    Reason = "Detailed HUD displaying FPS, active upscaler, and generated frame delta."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Menu",
                    Key = "DisableSplash",
                    Value = "true",
                    Reason = "Suppresses external splash screens on game launch."
                });

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Menu",
                    Key = "ShortcutKey",
                    Value = "-1",
                    Reason = "Suppresses native backend developer menu to keep game UI clean."
                });

                // Anti-Ghosting Shield: HUD isolation, immutable buffer cloning & transparency masks
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "HUDFix",
                    Value = "true",
                    Reason = "Prevents HUD smearing, crosshair ghosting, and subtitle flicker."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "HUDFixExtended",
                    Value = "true",
                    Reason = "Extended HUD separation for complex post-processing game UIs."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "MakeDepthCopy",
                    Value = "true",
                    Reason = "Creates depth buffer clone to avoid race conditions with DX12 presentation."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "MakeMVCopy",
                    Value = "true",
                    Reason = "Clones motion vectors before overwriting, eliminating edge ghost trails."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "DisableHUDFix",
                    Value = "false",
                    Reason = "Ensures HUD element tracking is fully active."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "HUDFixImmediate",
                    Value = "true",
                    Reason = "Captures HUD before execution to prevent race conditions and flickering."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "AlwaysTrackHeaps",
                    Value = "true",
                    Reason = "Continuously tracks render heaps to prevent presentation stutter."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "OptiFG",
                    Key = "HUDFixRelaxedResolutionCheck",
                    Value = "true",
                    Reason = "Allows flexible resolution matching for scaled UI overlays."
                });
                // Anti-Ghosting Shield: [InitFlags]
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "InitFlags",
                    Key = "DisableReactiveMask",
                    Value = "false",
                    Reason = "Prevents stripping responsive pixel masks."
                });

                // Anti-Ghosting Shield: [FrameGen]
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "DrawUIOverFG",
                    Value = "true",
                    Reason = "Draws UI over interpolated frames."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "DisableUI",
                    Value = "false",
                    Reason = "Preserves UI textures."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "DisableHudless",
                    Value = "false",
                    Reason = "Preserves hudless frame buffers."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "ForceBorderless",
                    Value = "true",
                    Reason = "Eliminates presentation stutter and tearing under frame interpolation."
                });

                // Anti-Ghosting Shield: [FSR] (Fixed section mismatch)
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "UseReactiveMaskForTransparency",
                    Value = "true",
                    Reason = "Enables reactive transparency masks to eliminate character disocclusion trails."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "DlssReactiveMaskBias",
                    Value = "0.45",
                    Reason = "Optimal reactive bias balancing edge sharpness and ghost prevention."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "ReactiveScale",
                    Value = "2.0",
                    Reason = "Scales reactive mask reactiveness to eliminate disocclusion ghosting."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "AccAddPerFrame",
                    Value = "0.05",
                    Reason = "Reduces per-frame accumulation added at disocclusions to suppress temporal trails."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "MinDisOccAcc",
                    Value = "-0.5",
                    Reason = "Negative threshold delaying accumulation on fast swaying objects."
                });
                break;

            case RuntimeIntegrationPreset.NativeDlssG:
                plan.Headline = "Enable Native DLSS 3 Hardware Frame Generation";
                plan.Summary = "Routes frame generation directly through NVIDIA Ada Lovelace hardware optical flow acceleration (requires RTX 40 series).";
                // Set baseline DLSS upscalers across all rendering APIs
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = "Dx12Upscaler",
                    Value = "dlss",
                    Reason = "Directs OptiScaler to route DirectX 12 upscaling through native DLSS."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = "Dx11Upscaler",
                    Value = "dlss",
                    Reason = "Directs OptiScaler to route DirectX 11 upscaling through native DLSS."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = "VulkanUpscaler",
                    Value = "dlss",
                    Reason = "Directs OptiScaler to route Vulkan upscaling through native DLSS."
                });

                // Cleanly disable proxy frame interception so native NVIDIA hardware optical flow executes directly
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "Enabled",
                    Value = "false",
                    Reason = "Disables proxy frame generation interception so games directly invoke native NVIDIA hardware optical flow."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGInput",
                    Value = "nofg",
                    Reason = "Bypasses proxy frame generation input interception."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGOutput",
                    Value = "nofg",
                    Reason = "Disables proxy frame generation output to allow native DLSS-G presentation."
                });

                // Enable Streamline and HAGS spoofing
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Spoofing",
                    Key = "StreamlineSpoofing",
                    Value = "true",
                    Reason = "Ensures the game unlocks native DLSS Frame Generation toggles."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Spoofing",
                    Key = "SpoofHAGS",
                    Value = "true",
                    Reason = "Ensures Hardware Accelerated GPU Scheduling requirements are satisfied for native DLSS-G."
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
