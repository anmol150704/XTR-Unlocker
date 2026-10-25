using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Describes the runtime layers shipped in a package. Keeping this beside the payload
/// lets XTR explain what will be installed instead of guessing from filenames alone.
/// </summary>
internal sealed class RuntimeBundleMetadata
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<string> Components { get; set; } = new();
    public List<string> SupportedApis { get; set; } = new();
    public List<string> FrameGenerationModes { get; set; } = new();
    public string Notes { get; set; } = string.Empty;

    public static async Task<RuntimeBundleMetadata?> LoadAsync(
        string packageRoot,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(packageRoot, "xtr-runtime.json");
        if (!File.Exists(metadataPath))
            return InferFromPackage(packageRoot);

        await using var stream = File.OpenRead(metadataPath);
        var metadata = await JsonSerializer.DeserializeAsync<RuntimeBundleMetadata>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (metadata is null)
            throw new InvalidDataException("The XTR runtime metadata file is empty.");

        if (metadata.SchemaVersion != CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(metadata.Id) ||
            string.IsNullOrWhiteSpace(metadata.Version))
        {
            throw new InvalidDataException("The XTR runtime metadata file is not supported.");
        }

        return metadata;
    }

    private static RuntimeBundleMetadata? InferFromPackage(string packageRoot)
    {
        var runtimeDll = Path.Combine(packageRoot, "OptiScaler.dll");
        var runtimeIni = Path.Combine(packageRoot, "OptiScaler.ini");
        if (!File.Exists(runtimeDll) || !File.Exists(runtimeIni))
            return null;

        var relativeFiles = Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'))
            .ToArray();
        var version = FileVersionInfo.GetVersionInfo(runtimeDll).ProductVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = "unknown";

        var components = new List<string> { "OptiScaler engine" };
        var frameGenerationModes = new List<string>();
        if (relativeFiles.Any(path => path.Contains("dlssg_sm86", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("RTX 20/30 frame-generation unlocker");
            frameGenerationModes.Add("Native RTX 20/30 MFG");
        }
        if (relativeFiles.Any(path => path.Contains("dlss-enabler-headless", StringComparison.OrdinalIgnoreCase)))
            components.Add("DLSS Enabler companion");
        if (relativeFiles.Any(path => path.Contains("streamline", StringComparison.OrdinalIgnoreCase)))
            components.Add("Streamline bridge");
        if (relativeFiles.Any(path => path.Contains("dlssg_to_fsr3", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("DLSSG-to-FSR3 bridge");
            frameGenerationModes.Add("FSR3 bridge");
        }
        if (relativeFiles.Any(path => path.Contains("nvngx_dlss", StringComparison.OrdinalIgnoreCase) ||
                                      path.Contains("dlssnr", StringComparison.OrdinalIgnoreCase)))
            components.Add("DLSS provider / neural-rendering layer");

        return new RuntimeBundleMetadata
        {
            Id = $"external-optiscaler-{version}",
            DisplayName = "External OptiScaler package",
            Version = version,
            Components = components,
            SupportedApis = new List<string> { "DX11", "DX12", "Vulkan" },
            FrameGenerationModes = frameGenerationModes,
            Notes = "Metadata inferred from the package contents; review compatibility before enabling experimental paths."
        };
    }
}
