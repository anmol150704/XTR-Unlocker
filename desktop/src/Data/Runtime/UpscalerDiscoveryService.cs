using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DLSS_Swapper.Data.Runtime;

public enum UpscalerAssetKind
{
    Unknown = 0,
    NgxDlss,
    NgxDlssG,
    NgxDlssD,
    NgxDlssNr,
    NgxDlssOther,
    StreamlineInterposer,
    StreamlineDirectSR,
    StreamlineDlss,
    StreamlineDlssG,
    StreamlineDlssD,
    StreamlineCommon,
    StreamlineReflex,
    StreamlinePcl,
    StreamlineNis,
    StreamlineDeepDvc,
    StreamlineNvPerf,
    Fsr31Dx12,
    Fsr31Vk,
    FidelityFxSdk2LoaderDx12,
    FidelityFxSdk2UpscalerDx12,
    FidelityFxSdk2FrameGenerationDx12,
    FidelityFxSdk2DenoiserDx12,
    FidelityFxSdk2RadianceCacheDx12,
    FidelityFxSdk2UpscalerVk,
    FidelityFxSdk2FrameGenerationVk,
    FidelityFxSdk2LoaderVk,
    FidelityFxSdk2Other,
    XeSS,
    XeSSDx11,
    XeSSFg,
    XeLL,
    XeSSOther
}

public sealed class DiscoveredUpscalerFile
{
    public UpscalerAssetKind Kind { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string? FileVersion { get; init; }
    public string? ProductVersion { get; init; }
    public string? ProductName { get; init; }
    public bool IsRootLevel { get; init; }
}

public sealed class NgxOtaModelStoreInfo
{
    public string ModelsRoot { get; init; } = string.Empty;
    public bool StoreExists { get; init; }
    public bool HasDlssModel { get; init; }
    public string? DlssVersion { get; init; }
    public bool HasDlssDModel { get; init; }
    public string? DlssDVersion { get; init; }
    public bool HasDlssGModel { get; init; }
    public string? DlssGVersion { get; init; }
    public bool HasStreamlineDlssOta { get; init; }
    public bool HasStreamlineDlssDOta { get; init; }
    public bool HasStreamlineDlssGOta { get; init; }
}

public sealed class UpscalerDiscoveryResult
{
    public string GameRoot { get; init; } = string.Empty;
    public IReadOnlyList<DiscoveredUpscalerFile> Files { get; init; } = Array.Empty<DiscoveredUpscalerFile>();
    public NgxOtaModelStoreInfo NgxOta { get; init; } = new();
    public string DetectedGraphicsApi { get; init; } = "DX12";

    public IReadOnlyList<DiscoveredUpscalerFile> LocalNgxDlss => Files.Where(f => f.Kind == UpscalerAssetKind.NgxDlss).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> LocalNgxDlssG => Files.Where(f => f.Kind == UpscalerAssetKind.NgxDlssG).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> LocalNgxDlssD => Files.Where(f => f.Kind == UpscalerAssetKind.NgxDlssD).ToList();

    public IReadOnlyList<DiscoveredUpscalerFile> StreamlineInterposer => Files.Where(f => f.Kind == UpscalerAssetKind.StreamlineInterposer).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> StreamlineDirectSr => Files.Where(f => f.Kind == UpscalerAssetKind.StreamlineDirectSR).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> StreamlineDlssPlugin => Files.Where(f => f.Kind == UpscalerAssetKind.StreamlineDlss).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> StreamlineDlssGPlugin => Files.Where(f => f.Kind == UpscalerAssetKind.StreamlineDlssG).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> StreamlineDlssDPlugin => Files.Where(f => f.Kind == UpscalerAssetKind.StreamlineDlssD).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> StreamlineCommon => Files.Where(f => f.Kind == UpscalerAssetKind.StreamlineCommon).ToList();

    public IReadOnlyList<DiscoveredUpscalerFile> FidelityFxSdk2Files => Files.Where(f =>
        f.Kind is UpscalerAssetKind.FidelityFxSdk2LoaderDx12
            or UpscalerAssetKind.FidelityFxSdk2UpscalerDx12
            or UpscalerAssetKind.FidelityFxSdk2FrameGenerationDx12
            or UpscalerAssetKind.FidelityFxSdk2DenoiserDx12
            or UpscalerAssetKind.FidelityFxSdk2RadianceCacheDx12
            or UpscalerAssetKind.FidelityFxSdk2UpscalerVk
            or UpscalerAssetKind.FidelityFxSdk2FrameGenerationVk
            or UpscalerAssetKind.FidelityFxSdk2LoaderVk
            or UpscalerAssetKind.FidelityFxSdk2Other).ToList();

    public IReadOnlyList<DiscoveredUpscalerFile> Fsr31Dx12 => Files.Where(f => f.Kind == UpscalerAssetKind.Fsr31Dx12).ToList();
    public IReadOnlyList<DiscoveredUpscalerFile> Fsr31Vk => Files.Where(f => f.Kind == UpscalerAssetKind.Fsr31Vk).ToList();

    public IReadOnlyList<DiscoveredUpscalerFile> XeSSFiles => Files.Where(f =>
        f.Kind is UpscalerAssetKind.XeSS
            or UpscalerAssetKind.XeSSDx11
            or UpscalerAssetKind.XeSSFg
            or UpscalerAssetKind.XeLL
            or UpscalerAssetKind.XeSSOther).ToList();

    public bool HasLocalNgxDlss => LocalNgxDlss.Count > 0;
    public bool HasLocalNgxDlssG => LocalNgxDlssG.Count > 0;
    public bool HasLocalNgxDlssD => LocalNgxDlssD.Count > 0;

    public bool StreamlineStackDetected =>
        StreamlineInterposer.Count > 0 ||
        StreamlineDirectSr.Count > 0 ||
        StreamlineDlssPlugin.Count > 0 ||
        StreamlineDlssGPlugin.Count > 0 ||
        StreamlineDlssDPlugin.Count > 0 ||
        StreamlineCommon.Count > 0 ||
        Files.Any(f => f.Kind is UpscalerAssetKind.StreamlineReflex
            or UpscalerAssetKind.StreamlinePcl
            or UpscalerAssetKind.StreamlineNis
            or UpscalerAssetKind.StreamlineDeepDvc
            or UpscalerAssetKind.StreamlineNvPerf);

    public bool HasStreamlineNgxOtaDlss =>
        !HasLocalNgxDlss &&
        (StreamlineDlssPlugin.Count > 0 ||
         (StreamlineInterposer.Count > 0 && StreamlineDirectSr.Count > 0));

    public bool HasStreamlineNgxOtaDlssG =>
        !HasLocalNgxDlssG &&
        (StreamlineDlssGPlugin.Count > 0 ||
         (StreamlineInterposer.Count > 0 && StreamlineDirectSr.Count > 0));

    public bool HasStreamlineNgxOtaDlssD =>
        !HasLocalNgxDlssD &&
        (StreamlineDlssDPlugin.Count > 0 ||
         (StreamlineInterposer.Count > 0 && StreamlineDirectSr.Count > 0));

    public bool HasAnyDlssCapability => HasLocalNgxDlss || HasStreamlineNgxOtaDlss || HasLocalNgxDlssG || HasLocalNgxDlssD;
    public bool HasAnyDlssGCapability => HasLocalNgxDlssG || HasStreamlineNgxOtaDlssG;
    public bool HasAnyDlssDCapability => HasLocalNgxDlssD || HasStreamlineNgxOtaDlssD;

    // Never conflate sl.dlss.dll (e.g. 2.14.x) with nvngx_dlss.dll (310.x).
    public string? EffectiveNgxDlssVersion =>
        LocalNgxDlss.FirstOrDefault()?.FileVersion ?? (HasStreamlineNgxOtaDlss ? NgxOta.DlssVersion : null);

    public string? EffectiveNgxDlssGVersion =>
        LocalNgxDlssG.FirstOrDefault()?.FileVersion ?? (HasStreamlineNgxOtaDlssG ? NgxOta.DlssGVersion : null);

    public string? EffectiveNgxDlssDVersion =>
        LocalNgxDlssD.FirstOrDefault()?.FileVersion ?? (HasStreamlineNgxOtaDlssD ? NgxOta.DlssDVersion : null);
}

public static class UpscalerDiscoveryService
{
    private static readonly EnumerationOptions RecursiveDllEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive
    };

    public static UpscalerDiscoveryResult Discover(string gameRoot, string? ngxModelsRootOverride = null)
    {
        var ngxOta = InspectNgxOtaStore(ngxModelsRootOverride);
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return new UpscalerDiscoveryResult
            {
                GameRoot = gameRoot ?? string.Empty,
                NgxOta = ngxOta,
                DetectedGraphicsApi = "DX12"
            };
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if ((File.GetAttributes(normalizedRoot) & FileAttributes.ReparsePoint) != 0)
        {
            return new UpscalerDiscoveryResult
            {
                GameRoot = normalizedRoot,
                NgxOta = ngxOta,
                DetectedGraphicsApi = "DX12"
            };
        }

        var discovered = new List<DiscoveredUpscalerFile>();
        var hasDx12Signal = Directory.Exists(Path.Combine(normalizedRoot, "D3D12"));
        var hasVulkanSignal = false;
        var hasDx11Signal = false;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(normalizedRoot, "*.dll", RecursiveDllEnumerationOptions))
            {
                if (!filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = Path.GetFileName(filePath);
                var relativePath = Path.GetRelativePath(normalizedRoot, filePath);
                var normalizedRel = relativePath.Replace('\\', '/');

                // Never treat OptiScaler's own runtime folders as game upscaler/API assets.
                if (IsInsideOptiScalerRuntimeFolder(normalizedRel))
                {
                    continue;
                }

                if (fileName.Contains("d3d12", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("_dx12", StringComparison.OrdinalIgnoreCase))
                {
                    hasDx12Signal = true;
                }
                else if (fileName.Contains("vulkan", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Equals("vulkan-1.dll", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("_vk.", StringComparison.OrdinalIgnoreCase))
                {
                    hasVulkanSignal = true;
                }
                else if (fileName.Contains("d3d11", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("_dx11", StringComparison.OrdinalIgnoreCase))
                {
                    hasDx11Signal = true;
                }

                var kind = ClassifyFileName(fileName);
                if (kind == UpscalerAssetKind.Unknown)
                {
                    continue;
                }

                var (fileVersion, productVersion, productName) = ReadVersionMetadataSafe(filePath);
                var parentDir = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(filePath) ?? string.Empty);

                discovered.Add(new DiscoveredUpscalerFile
                {
                    Kind = kind,
                    FileName = fileName,
                    FullPath = filePath,
                    RelativePath = relativePath,
                    FileVersion = fileVersion,
                    ProductVersion = productVersion,
                    ProductName = productName,
                    IsRootLevel = string.Equals(parentDir, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // Return whatever was discovered cleanly.
        }

        discovered.Sort(static (a, b) =>
        {
            var rootCompare = b.IsRootLevel.CompareTo(a.IsRootLevel);
            return rootCompare != 0
                ? rootCompare
                : string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
        });

        var detectedApi = hasDx12Signal
            ? "DX12"
            : hasVulkanSignal
                ? "Vulkan"
                : hasDx11Signal
                    ? "DX11"
                    : "DX12";

        return new UpscalerDiscoveryResult
        {
            GameRoot = normalizedRoot,
            Files = discovered,
            NgxOta = ngxOta,
            DetectedGraphicsApi = detectedApi
        };
    }

    public static UpscalerAssetKind ClassifyFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return UpscalerAssetKind.Unknown;
        }

        var lower = fileName.ToLowerInvariant();
        return lower switch
        {
            "nvngx_dlss.dll" => UpscalerAssetKind.NgxDlss,
            "nvngx_dlssg.dll" => UpscalerAssetKind.NgxDlssG,
            "nvngx_dlssd.dll" => UpscalerAssetKind.NgxDlssD,
            "nvngx_dlss_nr.dll" or "nvngx_dlssnr.dll" => UpscalerAssetKind.NgxDlssNr,
            "sl.interposer.dll" => UpscalerAssetKind.StreamlineInterposer,
            "sl.directsr.dll" => UpscalerAssetKind.StreamlineDirectSR,
            "sl.dlss.dll" => UpscalerAssetKind.StreamlineDlss,
            "sl.dlss_g.dll" => UpscalerAssetKind.StreamlineDlssG,
            "sl.dlss_d.dll" => UpscalerAssetKind.StreamlineDlssD,
            "sl.common.dll" => UpscalerAssetKind.StreamlineCommon,
            "sl.reflex.dll" => UpscalerAssetKind.StreamlineReflex,
            "sl.pcl.dll" => UpscalerAssetKind.StreamlinePcl,
            "sl.nis.dll" => UpscalerAssetKind.StreamlineNis,
            "sl.deepdvc.dll" => UpscalerAssetKind.StreamlineDeepDvc,
            "sl.nvperf.dll" => UpscalerAssetKind.StreamlineNvPerf,
            "amd_fidelityfx_dx12.dll" => UpscalerAssetKind.Fsr31Dx12,
            "amd_fidelityfx_vk.dll" => UpscalerAssetKind.Fsr31Vk,
            "amd_fidelityfx_loader_dx12.dll" => UpscalerAssetKind.FidelityFxSdk2LoaderDx12,
            "amd_fidelityfx_upscaler_dx12.dll" => UpscalerAssetKind.FidelityFxSdk2UpscalerDx12,
            "amd_fidelityfx_framegeneration_dx12.dll" => UpscalerAssetKind.FidelityFxSdk2FrameGenerationDx12,
            "amd_fidelityfx_denoiser_dx12.dll" => UpscalerAssetKind.FidelityFxSdk2DenoiserDx12,
            "amd_fidelityfx_radiancecache_dx12.dll" => UpscalerAssetKind.FidelityFxSdk2RadianceCacheDx12,
            "amd_fidelityfx_upscaler_vk.dll" => UpscalerAssetKind.FidelityFxSdk2UpscalerVk,
            "amd_fidelityfx_framegeneration_vk.dll" => UpscalerAssetKind.FidelityFxSdk2FrameGenerationVk,
            "amd_fidelityfx_loader_vk.dll" => UpscalerAssetKind.FidelityFxSdk2LoaderVk,
            "libxess.dll" => UpscalerAssetKind.XeSS,
            "libxess_dx11.dll" => UpscalerAssetKind.XeSSDx11,
            "libxess_fg.dll" => UpscalerAssetKind.XeSSFg,
            "libxell.dll" => UpscalerAssetKind.XeLL,
            _ when lower.StartsWith("nvngx_dlss", StringComparison.Ordinal) => UpscalerAssetKind.NgxDlssOther,
            _ when lower.StartsWith("amd_fidelityfx_", StringComparison.Ordinal) => UpscalerAssetKind.FidelityFxSdk2Other,
            _ when lower.StartsWith("libxess", StringComparison.Ordinal) => UpscalerAssetKind.XeSSOther,
            _ => UpscalerAssetKind.Unknown
        };
    }

    public static bool IsSwappableAssetKind(UpscalerAssetKind kind) => kind switch
    {
        UpscalerAssetKind.NgxDlss => true,
        UpscalerAssetKind.NgxDlssG => true,
        UpscalerAssetKind.NgxDlssD => true,
        UpscalerAssetKind.Fsr31Dx12 => true,
        UpscalerAssetKind.Fsr31Vk => true,
        UpscalerAssetKind.XeSS => true,
        UpscalerAssetKind.XeSSDx11 => true,
        UpscalerAssetKind.XeSSFg => true,
        UpscalerAssetKind.XeLL => true,
        _ => false
    };

    public static bool IsCacheableAssetKind(UpscalerAssetKind kind) => kind is
        not UpscalerAssetKind.Unknown and
        not UpscalerAssetKind.NgxDlssOther and
        not UpscalerAssetKind.XeSSOther;

    public static bool HasUncachedUpscalerFiles(string gameRoot, IEnumerable<string> cachedAssetPaths)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return false;
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if ((File.GetAttributes(normalizedRoot) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var cachedSet = new HashSet<string>(
            cachedAssetPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(normalizedRoot, "*.dll", RecursiveDllEnumerationOptions))
            {
                if (!filePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedRel = Path.GetRelativePath(normalizedRoot, filePath).Replace('\\', '/');
                if (IsInsideOptiScalerRuntimeFolder(normalizedRel))
                {
                    continue;
                }

                var kind = ClassifyFileName(Path.GetFileName(filePath));
                if (!IsCacheableAssetKind(kind))
                {
                    continue;
                }

                if (!cachedSet.Contains(Path.GetFullPath(filePath)))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsInsideOptiScalerRuntimeFolder(string normalizedRelativePath)
    {
        var segments = normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("OptiScaler", StringComparison.OrdinalIgnoreCase) ||
                segments[i].Equals("D3D12_Optiscaler", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string ResolveDlssDeployTargetDirectory(
        string gameRoot,
        UpscalerDiscoveryResult discovery,
        string? primaryExePath = null)
    {
        if (discovery.LocalNgxDlss.Count > 0)
        {
            var dir = Path.GetDirectoryName(discovery.LocalNgxDlss[0].FullPath);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }

        if (discovery.StreamlineInterposer.Count > 0)
        {
            var dir = Path.GetDirectoryName(discovery.StreamlineInterposer[0].FullPath);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }

        if (discovery.StreamlineDirectSr.Count > 0)
        {
            var dir = Path.GetDirectoryName(discovery.StreamlineDirectSr[0].FullPath);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }

        if (discovery.StreamlineCommon.Count > 0)
        {
            var dir = Path.GetDirectoryName(discovery.StreamlineCommon[0].FullPath);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }

        if (!string.IsNullOrWhiteSpace(primaryExePath))
        {
            var dir = Path.GetDirectoryName(primaryExePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) return dir;
        }

        return gameRoot;
    }

    public static NgxOtaModelStoreInfo InspectNgxOtaStore(string? ngxModelsRootOverride = null)
    {
        var root = ngxModelsRootOverride;
        if (string.IsNullOrWhiteSpace(root))
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                programData = Environment.ExpandEnvironmentVariables("%ProgramData%");
            }
            root = Path.Combine(programData, "NVIDIA", "NGX", "models");
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new NgxOtaModelStoreInfo
            {
                ModelsRoot = root ?? string.Empty,
                StoreExists = false
            };
        }

        var (hasDlss, dlssVer) = ScanNgxModelVersions(Path.Combine(root, "dlss", "versions"), "*.bin");
        var (hasDlssD, dlssDVer) = ScanNgxModelVersions(Path.Combine(root, "dlssd", "versions"), "*.bin");
        var (hasDlssG, dlssGVer) = ScanNgxModelVersions(Path.Combine(root, "dlssg", "versions"), "*.bin");

        var hasSlDlssOta =
            HasAnyFileInSubdir(Path.Combine(root, "dlss_override", "versions"), "sl.dlss.dll") ||
            HasAnyFileInSubdir(Path.Combine(root, "sl_dlss_0", "versions"), "*.dll") ||
            HasAnyFileInSubdir(Path.Combine(root, "sl_dlss_override_0", "versions"), "*.dll");

        var hasSlDlssDOta =
            HasAnyFileInSubdir(Path.Combine(root, "dlss_override", "versions"), "sl.dlss_d.dll") ||
            HasAnyFileInSubdir(Path.Combine(root, "sl_dlss_d_0", "versions"), "*.dll") ||
            HasAnyFileInSubdir(Path.Combine(root, "sl_dlss_d_override_0", "versions"), "*.dll");

        var hasSlDlssGOta =
            HasAnyFileInSubdir(Path.Combine(root, "dlss_override", "versions"), "sl.dlss_g.dll") ||
            HasAnyFileInSubdir(Path.Combine(root, "sl_dlss_g_0", "versions"), "*.dll") ||
            HasAnyFileInSubdir(Path.Combine(root, "sl_dlss_g_override_0", "versions"), "*.dll");

        return new NgxOtaModelStoreInfo
        {
            ModelsRoot = root,
            StoreExists = true,
            HasDlssModel = hasDlss,
            DlssVersion = dlssVer,
            HasDlssDModel = hasDlssD,
            DlssDVersion = dlssDVer,
            HasDlssGModel = hasDlssG,
            DlssGVersion = dlssGVer,
            HasStreamlineDlssOta = hasSlDlssOta,
            HasStreamlineDlssDOta = hasSlDlssDOta,
            HasStreamlineDlssGOta = hasSlDlssGOta
        };
    }

    private static (bool HasAny, string? HighestVersion) ScanNgxModelVersions(string versionsDir, string pattern)
    {
        if (!Directory.Exists(versionsDir))
        {
            return (false, null);
        }

        var hasAny = false;
        Version? highest = null;

        try
        {
            foreach (var file in Directory.EnumerateFiles(versionsDir, pattern, RecursiveDllEnumerationOptions))
            {
                hasAny = true;
                var (fileVerStr, _, _) = ReadVersionMetadataSafe(file);
                if (fileVerStr is not null && Version.TryParse(fileVerStr, out var parsed))
                {
                    if (highest is null || parsed > highest)
                    {
                        highest = parsed;
                    }
                }
            }
        }
        catch
        {
            // Ignore unreadable NGX folders.
        }

        return (hasAny, highest?.ToString());
    }

    private static bool HasAnyFileInSubdir(string dir, string pattern)
    {
        if (!Directory.Exists(dir))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(dir, pattern, RecursiveDllEnumerationOptions).Any();
        }
        catch
        {
            return false;
        }
    }

    private static (string? FileVersion, string? ProductVersion, string? ProductName) ReadVersionMetadataSafe(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            string? formattedFileVersion = null;
            if (info.FileMajorPart > 0 || info.FileMinorPart > 0 || info.FileBuildPart > 0 || info.FilePrivatePart > 0)
            {
                formattedFileVersion = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
            }

            var prodVer = string.IsNullOrWhiteSpace(info.ProductVersion) ? null : info.ProductVersion.Trim();
            var prodName = string.IsNullOrWhiteSpace(info.ProductName) ? null : info.ProductName.Trim();
            return (formattedFileVersion, prodVer, prodName);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
