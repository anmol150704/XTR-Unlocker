using System;
using System.Collections.Generic;
using System.IO;
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
            return null;

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
}
