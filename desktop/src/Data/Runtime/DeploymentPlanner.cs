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
    TurboBoost,
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
        string? fgBackendOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentNullException.ThrowIfNull(pack);

        var compatRecord = CompatibilityDatabase.LookupGame(gameTitle) ?? CompatibilityDatabase.LookupGame(gameId);
        var proxy = proxyOverride ?? compatRecord?.RecommendedProxy ?? (IsForzaOrVirtualTextureTitle(gameTitle) ? "version.dll" : null) ?? pack.RecommendedProxy ?? "dxgi.dll";


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

                var sourceHash = pack.GetExpectedHash(relative)
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

                bool isStreamlineDlssG = IsStreamlineOrOpenWorldTitle(gameTitle, preflight);
                var fgInput = isStreamlineDlssG ? "dlssg" : "upscaler";

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGInput",
                    Value = fgInput,
                    Reason = fgInput == "dlssg"
                        ? "Intercepts native DLSS-G motion vectors, depth, and hudless buffers from the game engine."
                        : "Derives frame-generation motion vectors from the active DLSS upscaler."
                });

                var fgOutput = ResolveFgOutput(fgBackendOverride, pack, preflight);

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FrameGen",
                    Key = "FGOutput",
                    Value = fgOutput,
                    Reason = fgOutput switch
                    {
                        "nukems" => "Routes frame generation through the Nukem9 Streamline DLSS-G to FSR 3.1 bridge.",
                        "xefg" => "Routes frame generation through the Intel XeFG hardware interpolation pipeline.",
                        _ => "Routes frame generation through the verified DirectX 12 hardware frame interpolation pipeline."
                    }
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

                // FrameGen Presentation flags
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

                if (isStreamlineDlssG)
                {
                    // Clean Streamline Passthrough: Zero invasive hooks, pristine texture streaming & zero crashes
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "OptiFG",
                        Key = "HUDFix",
                        Value = "false",
                        Reason = "Disabled: native Streamline engine provides pristine hudless buffers directly."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "OptiFG",
                        Key = "DisableHUDFix",
                        Value = "true",
                        Reason = "Eliminates render-target hooking overhead on native Streamline titles."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "OptiFG",
                        Key = "AlwaysTrackHeaps",
                        Value = "false",
                        Reason = "Disabled to eliminate virtual texture streaming stalls in open world titles (ForzaTech/REDengine)."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "OptiFG",
                        Key = "MakeDepthCopy",
                        Value = "false",
                        Reason = "Disabled to avoid duplicate depth buffer allocation and swapchain conflicts."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "OptiFG",
                        Key = "MakeMVCopy",
                        Value = "false",
                        Reason = "Disabled: native motion vectors are passed directly without duplication."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "DepthValidNow",
                        Value = "false",
                        Reason = "Native Streamline buffers manage depth validation automatically."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "VelocityValidNow",
                        Value = "false",
                        Reason = "Native Streamline buffers manage velocity validation automatically."
                    });
                }
                else
                {
                    // Universal FG Bridge: Calibrated HUD separation and clean FSR 3.1
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
                        Reason = "Extended HUD element coverage for crosshairs and subtitles."
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
                }

                // Anti-Ghosting Shield Calibration for all FrameGen bridge modes:
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "InitFlags",
                    Key = "DisableReactiveMask",
                    Value = "false",
                    Reason = "Prevents stripping responsive pixel masks."
                });
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
                    Reason = "Calibrated reactive mask scale for rock-solid stability."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "AccAddPerFrame",
                    Value = "0.05",
                    Reason = "High-velocity disocclusion flush eliminating ghost smears at high speeds."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "FSR",
                    Key = "MinDisOccAcc",
                    Value = "-0.5",
                    Reason = "Disocclusion accumulation threshold delaying history blending."
                });

                // DLSS Neural Model Preset E (Anti-Ghosting Champion)
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "DLSS",
                    Key = "RenderPresetOverride",
                    Value = "true",
                    Reason = "Overrides DLSS neural network model to eliminate motion smear."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "DLSS",
                    Key = "RenderPresetForAll",
                    Value = "5",
                    Reason = "Forces DLSS Preset E across all performance modes to eliminate ghosting trails."
                });

                // Enable OptiScaler in-game detection overlay and full XTR Apple Liquid styling & hotkeys
                AddXtrMenuAndOverlayEntries(plan);
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

                // DLSS Neural Model Preset E (Anti-Ghosting Champion)
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "DLSS",
                    Key = "RenderPresetOverride",
                    Value = "true",
                    Reason = "Overrides DLSS neural network model to eliminate motion smear."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "DLSS",
                    Key = "RenderPresetForAll",
                    Value = "5",
                    Reason = "Forces DLSS Preset E across all performance modes to eliminate ghosting trails."
                });

                // Enable OptiScaler in-game detection overlay and full XTR Apple Liquid styling & hotkeys
                AddXtrMenuAndOverlayEntries(plan);
                break;

            case RuntimeIntegrationPreset.TurboBoost:
                plan.Headline = "⚡ Activate XTR Turbo Boost";
                plan.Summary = "Maximum performance and responsiveness: DLSS upscaling, optimized frame generation, Preset E anti-ghosting, and calibrated frame pacing.";

                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = "Dx12Upscaler",
                    Value = "dlss",
                    Reason = "Prioritizes DLSS upscaling for pristine image quality and performance."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = "Dx11Upscaler",
                    Value = "dlss",
                    Reason = "Directs OptiScaler to route DirectX 11 upscaling through DLSS."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Upscalers",
                    Key = "VulkanUpscaler",
                    Value = "dlss",
                    Reason = "Directs OptiScaler to route Vulkan upscaling through DLSS."
                });

                bool isForzaOrVirtualStreaming = IsForzaOrVirtualTextureTitle(gameTitle);

                if (!isForzaOrVirtualStreaming && preflight.NativeDlssGDetected)
                {
                    // Native Ada Lovelace Optical Flow bypass for non-virtual-streaming titles
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "Enabled",
                        Value = "false",
                        Reason = "Disables proxy frame generation to let native NVIDIA hardware optical flow run directly."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "FGInput",
                        Value = "nofg",
                        Reason = "Bypasses proxy FG inputs for pure hardware acceleration."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "FGOutput",
                        Value = "nofg",
                        Reason = "Direct hardware presentation without translation overhead."
                    });
                }
                else
                {
                    // Frame Generation Bridge
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "Enabled",
                        Value = "true",
                        Reason = "Activates high-performance frame generation bridge."
                    });

                    bool isStreamlineOrOpenWorld = isForzaOrVirtualStreaming || IsStreamlineOrOpenWorldTitle(gameTitle, preflight);
                    var fgIn = isStreamlineOrOpenWorld ? "dlssg" : "upscaler";

                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "FGInput",
                        Value = fgIn,
                        Reason = fgIn == "dlssg"
                            ? "Derives motion vectors directly from native game Streamline engine."
                            : "Derives motion vectors from the active upscaler pipeline."
                    });
                    var turboFgOutput = ResolveFgOutput(fgBackendOverride, pack, preflight);

                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "FGOutput",
                        Value = turboFgOutput,
                        Reason = turboFgOutput switch
                        {
                            "nukems" => "Routes frame generation through the Nukem9 Streamline DLSS-G to FSR 3.1 bridge.",
                            "xefg" => "Routes frame generation through the Intel XeFG hardware interpolation pipeline.",
                            _ => "Routes through optimized DirectX 12 hardware frame interpolation."
                        }
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "DrawUIOverFG",
                        Value = "true",
                        Reason = "Preserves sharp HUD rendering above interpolated frames."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FrameGen",
                        Key = "DisableUI",
                        Value = "false",
                        Reason = "Ensures HUD texture capture is enabled."
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
                        Reason = "Eliminates presentation stutter under frame interpolation."
                    });

                    if (isStreamlineOrOpenWorld)
                    {
                        // Clean Streamline Passthrough: Zero intrusive hooking
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "HUDFix",
                            Value = "false",
                            Reason = "Streamline provides its own hudless buffer; disables intrusive render-target hooking."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "DisableHUDFix",
                            Value = "true",
                            Reason = "Eliminates UI hooking overhead."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "AlwaysTrackHeaps",
                            Value = "false",
                            Reason = "Prevents virtual texture streaming pool starvation in open world titles."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "MakeDepthCopy",
                            Value = "false",
                            Reason = "Eliminates redundant depth copying in Streamline."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "MakeMVCopy",
                            Value = "false",
                            Reason = "Eliminates redundant motion vector copying."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "FrameGen",
                            Key = "DepthValidNow",
                            Value = "false",
                            Reason = "Preserves native buffer validation."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "FrameGen",
                            Key = "VelocityValidNow",
                            Value = "false",
                            Reason = "Preserves native velocity validation."
                        });
                    }
                    else
                    {
                        // Universal fallback
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "HUDFix",
                            Value = "true",
                            Reason = "Anti-Ghosting Shield: eliminates HUD smearing and text trails."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "HUDFixExtended",
                            Value = "true",
                            Reason = "Anti-Ghosting Shield: extended HUD tracking."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "MakeDepthCopy",
                            Value = "true",
                            Reason = "Depth buffer clone avoids race conditions."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "MakeMVCopy",
                            Value = "true",
                            Reason = "Motion vector cloning eliminates edge ghost trails."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "OptiFG",
                            Key = "DisableHUDFix",
                            Value = "false",
                            Reason = "Maintains HUD fix activity."
                        });

                        // Ultra-low Latency Frame Pacing Tuning for universal pipeline
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "FSRFG",
                            Key = "FramePacingTuning",
                            Value = "true",
                            Reason = "Enables custom ultra-responsive frame pacing."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "FSRFG",
                            Key = "FPTSafetyMarginInMs",
                            Value = "0.01",
                            Reason = "Minimum latency safety margin."
                        });
                        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                        {
                            Section = "FSRFG",
                            Key = "FPTVarianceFactor",
                            Value = "0.3",
                            Reason = "Dynamic variance tuning for lowest input lag."
                        });
                    }

                    // Anti-Ghosting Calibration for all FrameGen bridge modes in TurboBoost:
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "InitFlags",
                        Key = "DisableReactiveMask",
                        Value = "false",
                        Reason = "Retains pixel reactivity."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FSR",
                        Key = "UseReactiveMaskForTransparency",
                        Value = "true",
                        Reason = "Transparency reactive masks remove character disocclusion trails."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FSR",
                        Key = "DlssReactiveMaskBias",
                        Value = "0.45",
                        Reason = "Optimal reactive bias."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FSR",
                        Key = "ReactiveScale",
                        Value = "2.0",
                        Reason = "Calibrated FSR reactivity."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FSR",
                        Key = "AccAddPerFrame",
                        Value = "0.05",
                        Reason = "High-velocity disocclusion flush eliminating ghost smears at high speeds."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "FSR",
                        Key = "MinDisOccAcc",
                        Value = "-0.5",
                        Reason = "Disocclusion accumulation threshold delaying history blending."
                    });
                }

                // DLSS Neural Model Preset E (Anti-Ghosting Champion)
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "DLSS",
                    Key = "RenderPresetOverride",
                    Value = "true",
                    Reason = "Overrides neural network model for ghosting elimination."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "DLSS",
                    Key = "RenderPresetForAll",
                    Value = "5",
                    Reason = "DLSS Preset E: state-of-the-art motion stability with zero ghost trails."
                });

                // Only apply Anisotropic and Mipmap overrides on non-virtual-streaming titles
                // to prevent starving virtual texture streaming pools in games like Forza Horizon
                if (!isForzaOrVirtualStreaming)
                {
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "Anisotropy",
                        Key = "AnisotropyOverride",
                        Value = "16",
                        Reason = "Enforces 16x anisotropic texture filtering for razor-sharp surface detail."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "Anisotropy",
                        Key = "ModifyComparison",
                        Value = "true",
                        Reason = "Upgrades comparison texture filters to anisotropic."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "Anisotropy",
                        Key = "ModifyMinMax",
                        Value = "true",
                        Reason = "Upgrades min/max texture filters to anisotropic."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "Anisotropy",
                        Key = "SkipPointFilter",
                        Value = "true",
                        Reason = "Preserves pixel point filters."
                    });

                    // Negative Mipmap LOD Bias
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "Mipmap",
                        Key = "MipmapBiasOverride",
                        Value = "-0.5",
                        Reason = "Negative Mipmap LOD bias compensates for upscaler resolution, keeping textures sharp."
                    });
                    plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                    {
                        Section = "Mipmap",
                        Key = "MipmapBiasFixedOverride",
                        Value = "true",
                        Reason = "Applies fixed optimal LOD bias across all textures."
                    });
                }

                // AI RCAS Sharpness
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Sharpness",
                    Key = "OverrideSharpness",
                    Value = "true",
                    Reason = "Enables AI super-resolution sharpness filter."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Sharpness",
                    Key = "Sharpness",
                    Value = "0.35",
                    Reason = "RCAS 35% sharpness for edge clarity without ringing artifacts."
                });

                // Hardware Spoofing
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Spoofing",
                    Key = "StreamlineSpoofing",
                    Value = "true",
                    Reason = "Unlocks DLSS Frame Generation menus."
                });
                plan.PlannedConfigModifications.Add(new PlannedConfigEntry
                {
                    Section = "Spoofing",
                    Key = "SpoofHAGS",
                    Value = "true",
                    Reason = "Satisfies HAGS hardware scheduling requirement."
                });

                // Menu & Telemetry: Apple Liquid overlay & hotkeys
                AddXtrMenuAndOverlayEntries(plan);
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

    public static string? OverrideDevHotkey { get; set; }
    public static string? OverrideHudHotkey { get; set; }
    public static string? OverrideHudCycleHotkey { get; set; }
    public static string? OverrideFrameGenHotkey { get; set; }
    public static string? OverrideHudPosition { get; set; }
    public static string? OverrideHudMode { get; set; }
    public static string? OverrideTheme { get; set; }
    private static readonly AsyncLocal<string?> FgOutputOverride = new();
    public static string? OverrideFgOutput
    {
        get => FgOutputOverride.Value;
        set => FgOutputOverride.Value = value;
    }

    public static string ResolveFgOutput(
        string? requestedBackend = null,
        RuntimePack? pack = null,
        RuntimePreflightResult? preflight = null)
    {
        var candidate = (requestedBackend ?? OverrideFgOutput)?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(candidate))
        {
            return candidate switch
            {
                "nukem" or "nukems" or "nukem9" => "nukems",
                "xefg" or "xess" or "intel" => "xefg",
                "fsr" or "fsrfg" or "fsr3" or "fsr31" or "amd" => "fsrfg",
                _ => candidate
            };
        }

        return "fsrfg";
    }

    public static bool IsForzaOrVirtualTextureTitle(string gameTitle)
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
            return false;

        return gameTitle.Contains("Forza", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Horizon", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStreamlineOrOpenWorldTitle(string gameTitle, RuntimePreflightResult preflight)
    {
        if (preflight.StreamlineDetected || preflight.NativeDlssGDetected)
            return true;

        var compat = CompatibilityDatabase.LookupGame(gameTitle);
        if (compat?.SupportsNativeFrameGen == true)
            return true;

        if (string.IsNullOrWhiteSpace(gameTitle))
            return false;

        return gameTitle.Contains("Forza", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Horizon", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Witcher", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Spider-Man", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Miles Morales", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Alan Wake", StringComparison.OrdinalIgnoreCase) ||
               gameTitle.Contains("Avatar", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddXtrMenuAndOverlayEntries(DeploymentPlan plan)
    {
        string? devHotkey = OverrideDevHotkey;
        string? hudHotkey = OverrideHudHotkey;
        string? cycleHotkey = OverrideHudCycleHotkey;
        string? fgHotkey = OverrideFrameGenHotkey;
        string? hudPos = OverrideHudPosition;
        string? hudType = OverrideHudMode;
        string? theme = OverrideTheme;

#if !XTR_TESTS
        devHotkey ??= Settings.Instance.SelectedDevMenuHotkey ?? "0x2D";
        hudHotkey ??= Settings.Instance.SelectedHudHotkey ?? "0x79";
        cycleHotkey ??= Settings.Instance.SelectedHudCycleHotkey ?? "0x78";
        fgHotkey ??= Settings.Instance.SelectedFrameGenHotkey ?? "0x7B";
        hudPos ??= Settings.Instance.SelectedHudPosition.ToString(System.Globalization.CultureInfo.InvariantCulture);
        hudType ??= Settings.Instance.SelectedHudMode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        theme ??= Settings.Instance.SelectedOverlayTheme ?? "Apple Emerald Glass";
#else
        devHotkey ??= "0x2D";
        hudHotkey ??= "0x79";
        cycleHotkey ??= "0x78";
        fgHotkey ??= "0x7B";
        hudPos ??= "0";
        hudType ??= "2";
        theme ??= "Apple Emerald Glass";
#endif

        string accentR = "0.20", accentG = "0.78", accentB = "0.35";
        string bgR = "0.08", bgG = "0.09", bgB = "0.11", bgA = "0.92";

        if (theme == "Cyberpunk Neon")
        {
            accentR = "0.00"; accentG = "0.94"; accentB = "1.00";
            bgR = "0.04"; bgG = "0.06"; bgB = "0.09"; bgA = "0.92";
        }
        else if (theme == "Stealth Obsidian")
        {
            accentR = "0.96"; accentG = "0.62"; accentB = "0.07";
            bgR = "0.10"; bgG = "0.12"; bgB = "0.15"; bgA = "0.95";
        }

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
            Reason = "Renders real-time in-game telemetry overlay with frame generation status."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsOverlayType",
            Value = hudType,
            Reason = "Detailed HUD displaying FPS, active upscaler, and generated frame delta."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsOverlayHorizontal",
            Value = "true",
            Reason = "Renders sleek horizontal pill HUD layout."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsOverlayPos",
            Value = hudPos,
            Reason = "Anchors HUD overlay position."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsScale",
            Value = "1.05",
            Reason = "Optimal HiDPI scale for FPS telemetry."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsOverlayAlpha",
            Value = "0.75",
            Reason = "Apple dark glass overlay transparency."
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
            Value = devHotkey,
            Reason = "Enables hotkey to toggle engine diagnostics overlay."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsShortcutKey",
            Value = hudHotkey,
            Reason = "Enables hotkey to toggle real-time FPS & FG HUD."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FpsCycleShortcutKey",
            Value = cycleHotkey,
            Reason = "Enables hotkey to cycle telemetry detail modes."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "FGShortcutKey",
            Value = fgHotkey,
            Reason = "Enables hotkey to dynamically toggle Frame Generation."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "AccentColorR",
            Value = accentR,
            Reason = "Theme accent color."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "AccentColorG",
            Value = accentG,
            Reason = "Theme accent color."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "AccentColorB",
            Value = accentB,
            Reason = "Theme accent color."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "BGColorR",
            Value = bgR,
            Reason = "Theme background color."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "BGColorG",
            Value = bgG,
            Reason = "Theme background color."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "BGColorB",
            Value = bgB,
            Reason = "Theme background color."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "BGColorA",
            Value = bgA,
            Reason = "Theme background opacity."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "Scale",
            Value = "1.1",
            Reason = "HiDPI scaling for developer overlay."
        });
        plan.PlannedConfigModifications.Add(new PlannedConfigEntry
        {
            Section = "Menu",
            Key = "UseHQFont",
            Value = "true",
            Reason = "High-quality monospace typography."
        });
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
