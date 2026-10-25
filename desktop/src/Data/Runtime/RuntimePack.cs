using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

public enum RuntimeReleaseChannel
{
    Stable,
    Experimental,
    CustomLocal
}

public sealed class RuntimePackCapabilities
{
    [JsonPropertyName("dlssUpdate")]
    public bool DlssUpdate { get; set; } = true;

    [JsonPropertyName("dlssFrameGeneration")]
    public bool DlssFrameGeneration { get; set; }

    [JsonPropertyName("multiFrameGeneration")]
    public bool MultiFrameGeneration { get; set; }

    [JsonPropertyName("dlssNeuralRendering")]
    public bool DlssNeuralRendering { get; set; }

    [JsonPropertyName("fsrXeSsFallback")]
    public bool FsrXeSsFallback { get; set; } = true;
}

public sealed class RuntimePackFileHash
{
    [JsonPropertyName("path")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

public sealed class RuntimePackLicense
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("notice")]
    public string Notice { get; set; } = string.Empty;
}

/// <summary>
/// A trusted, version-pinned runtime package descriptor.
/// Governs package identity, supported graphics APIs, proxy strategies, configuration schema,
/// and SHA-256 cryptographic hashes for every deployed file.
/// </summary>
public sealed class RuntimePack
{
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("sourceUrl")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public DateTimeOffset? ReleaseDate { get; set; }

    [JsonPropertyName("releaseChannel")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RuntimeReleaseChannel ReleaseChannel { get; set; } = RuntimeReleaseChannel.Stable;

    [JsonPropertyName("components")]
    public List<string> Components { get; set; } = new();

    [JsonPropertyName("supportedApis")]
    public List<string> SupportedApis { get; set; } = new();

    [JsonPropertyName("supportedGpuFamilies")]
    public List<string> SupportedGpuFamilies { get; set; } = new();

    [JsonPropertyName("proxyChoices")]
    public List<string> ProxyChoices { get; set; } = new();

    [JsonPropertyName("recommendedProxy")]
    public string RecommendedProxy { get; set; } = "dxgi.dll";

    [JsonPropertyName("configurationSchema")]
    public string ConfigurationSchema { get; set; } = "optiscaler-ini-v1";

    [JsonPropertyName("capabilities")]
    public RuntimePackCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("fileHashes")]
    public List<RuntimePackFileHash> FileHashes { get; set; } = new();

    [JsonPropertyName("licenses")]
    public List<RuntimePackLicense> Licenses { get; set; } = new();

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsTrusted { get; set; } = true;

    [JsonIgnore]
    public string? PackageRoot { get; set; }

    public static async Task<RuntimePack?> LoadFromDirectoryAsync(
        string packageRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!Directory.Exists(packageRoot))
            return null;

        var manifestPath = Path.Combine(packageRoot, "xtr-runtime.json");
        if (!File.Exists(manifestPath))
        {
            return InferCustomPackage(packageRoot);
        }

        await using var stream = File.OpenRead(manifestPath);
        var pack = await JsonSerializer.DeserializeAsync<RuntimePack>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken).ConfigureAwait(false);

        if (pack is null)
            throw new InvalidDataException("The XTR runtime metadata file is empty.");

        pack.PackageRoot = packageRoot;

        // Populate defaults if loading legacy schema v1
        if (pack.ProxyChoices.Count == 0)
        {
            pack.ProxyChoices.AddRange(new[]
            {
                "dxgi.dll", "winmm.dll", "d3d12.dll", "version.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll"
            });
        }

        if (string.IsNullOrWhiteSpace(pack.RecommendedProxy))
        {
            pack.RecommendedProxy = "dxgi.dll";
        }

        if (pack.SupportedGpuFamilies.Count == 0)
        {
            pack.SupportedGpuFamilies.AddRange(new[]
            {
                "NVIDIA RTX 20/30/40", "AMD RDNA", "Intel Arc"
            });
        }

        // Verify cryptographic hashes if fileHashes are present
        if (pack.FileHashes.Count > 0)
        {
            var integrity = await pack.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
            pack.IsTrusted = integrity.IsValid;
        }
        else
        {
            // Without hashes, cannot be verified as fully trusted
            pack.IsTrusted = false;
        }

        return pack;
    }

    public async Task<(bool IsValid, List<string> Errors)> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(PackageRoot) || !Directory.Exists(PackageRoot))
        {
            errors.Add("Package directory does not exist.");
            return (false, errors);
        }

        if (FileHashes.Count == 0)
        {
            errors.Add("No file hashes specified in runtime pack manifest.");
            return (false, errors);
        }

        foreach (var expectedFile in FileHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = Path.Combine(PackageRoot, expectedFile.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                errors.Add($"Missing required package file: {expectedFile.RelativePath}");
                continue;
            }

            var actualHash = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, expectedFile.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Hash mismatch for {expectedFile.RelativePath}. Expected: {expectedFile.Sha256}, actual: {actualHash}");
            }
        }

        return (errors.Count == 0, errors);
    }

    public static RuntimePack? InferCustomPackage(string packageRoot)
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
        var capabilities = new RuntimePackCapabilities
        {
            DlssUpdate = true,
            FsrXeSsFallback = true
        };

        if (relativeFiles.Any(path => path.Contains("dlssg_sm86", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("RTX 20/30 frame-generation unlocker");
            capabilities.MultiFrameGeneration = true;
            capabilities.DlssFrameGeneration = true;
        }

        if (relativeFiles.Any(path => path.Contains("dlss-enabler-headless", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("DLSS Enabler companion");
        }

        if (relativeFiles.Any(path => path.Contains("streamline", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("Streamline bridge");
        }

        if (relativeFiles.Any(path => path.Contains("dlssg_to_fsr3", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("DLSSG-to-FSR3 bridge");
        }

        if (relativeFiles.Any(path => path.Contains("nvngx_dlss", StringComparison.OrdinalIgnoreCase) ||
                                      path.Contains("dlssnr", StringComparison.OrdinalIgnoreCase)))
        {
            components.Add("DLSS provider / neural-rendering layer");
            capabilities.DlssNeuralRendering = true;
        }

        return new RuntimePack
        {
            SchemaVersion = CurrentSchemaVersion,
            Id = $"custom-optiscaler-{version}",
            DisplayName = "Custom local package",
            Version = version,
            ReleaseChannel = RuntimeReleaseChannel.CustomLocal,
            Components = components,
            SupportedApis = new List<string> { "DX11", "DX12", "Vulkan" },
            SupportedGpuFamilies = new List<string> { "Unverified GPU compatibility" },
            ProxyChoices = new List<string>
            {
                "dxgi.dll", "winmm.dll", "d3d12.dll", "version.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll"
            },
            RecommendedProxy = "dxgi.dll",
            ConfigurationSchema = "optiscaler-ini-v1",
            Capabilities = capabilities,
            Notes = "Custom or unverified package. Metadata inferred from folder contents; no cryptographic verification available.",
            IsTrusted = false,
            PackageRoot = packageRoot
        };
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
