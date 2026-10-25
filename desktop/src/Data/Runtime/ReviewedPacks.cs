using System;
using System.Collections.Generic;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Curated registry of officially reviewed runtime packs for XTR Unlocker.
/// Prevents arbitrary unreviewed packages from masquerading as trusted production packs.
/// </summary>
public static class ReviewedPacks
{
    public const string BaselineOptiScalerId = "optiscaler-0.9.4-xtr";
    public const string DlssUnlockedId = "dlss-unlocked-0.9.72-xtr";

    public static RuntimePack CreateDlssUnlockedDescriptor(string? packageRoot = null)
    {
        return new RuntimePack
        {
            SchemaVersion = RuntimePack.CurrentSchemaVersion,
            Id = DlssUnlockedId,
            DisplayName = "DLSS-Unlocked 0.9.72 (Advanced MFG)",
            Version = "0.9.72",
            SourceUrl = "https://github.com/ShyVortex/dlss-unlocked/releases/tag/NR-v0.9.72",
            ReleaseDate = new DateTimeOffset(2024, 10, 22, 0, 0, 0, TimeSpan.Zero),
            ReleaseChannel = RuntimeReleaseChannel.Stable,
            Components = new List<string>
            {
                "OptiScaler engine",
                "dlssg_sm86 (RTX 20/30 native MFG unlocker)",
                "Streamline bridge",
                "DLSS Enabler companion",
                "DLSS-NR neural rendering layer",
                "FSR and XeSS providers"
            },
            SupportedApis = new List<string> { "DX12", "Vulkan", "DX11" },
            SupportedGpuFamilies = new List<string>
            {
                "NVIDIA RTX 20 series (Turing)",
                "NVIDIA RTX 30 series (Ampere)",
                "NVIDIA RTX 40 series (Ada)",
                "AMD RDNA",
                "Intel Arc"
            },
            ProxyChoices = new List<string>
            {
                "dxgi.dll", "winmm.dll", "d3d12.dll", "version.dll", "dbghelp.dll"
            },
            RecommendedProxy = "dxgi.dll",
            ConfigurationSchema = "optiscaler-ini-v1",
            Capabilities = new RuntimePackCapabilities
            {
                DlssUpdate = true,
                DlssFrameGeneration = true,
                MultiFrameGeneration = true,
                DlssNeuralRendering = true,
                FsrXeSsFallback = true
            },
            Notes = "Advanced reviewed all-in-one pack for DirectX 12 games with native DLSS and Frame Generation. Enables hardware frame-generation path on RTX 20 and 30 GPUs where supported.",
            IsTrusted = true,
            PackageRoot = packageRoot
        };
    }
}
