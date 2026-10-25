using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Legacy metadata adapter for backward compatibility.
/// Delegates to the trusted RuntimePack model.
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
        var pack = await RuntimePack.LoadFromDirectoryAsync(packageRoot, cancellationToken).ConfigureAwait(false);
        if (pack is null)
            return null;

        var fgModes = new List<string>();
        if (pack.Capabilities.MultiFrameGeneration)
            fgModes.Add("Native RTX 20/30 MFG");
        if (pack.Capabilities.DlssFrameGeneration)
            fgModes.Add("Native DLSS-G");
        if (pack.Components.Contains("DLSSG-to-FSR3 bridge"))
            fgModes.Add("FSR3 bridge");

        return new RuntimeBundleMetadata
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = pack.Id,
            DisplayName = pack.DisplayName,
            Version = pack.Version,
            Source = pack.SourceUrl,
            Components = new List<string>(pack.Components),
            SupportedApis = new List<string>(pack.SupportedApis),
            FrameGenerationModes = fgModes,
            Notes = pack.Notes
        };
    }
}
