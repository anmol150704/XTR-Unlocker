using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DLSS_Swapper.Data.Runtime;

public enum UpscalerSwapSlot
{
    Dlss,
    DlssG,
    DlssD,
    Fsr31Dx12,
    Fsr31Vk,
    XeSs,
    XeSsFg,
    XeLl,
    XeSsDx11
}

public sealed record SwapTargetResolution(
    bool CanSwap,
    string TargetDirectory,
    string TargetFileName,
    string TargetPath,
    bool TargetAlreadyExistsOnDisk,
    string? ExistingBackupPath,
    string Reason);

public static class SwapTargetResolver
{
    private static readonly string[] ProxyFileNames =
    [
        "dxgi.dll", "version.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll"
    ];

    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sl.dlss.dll",
        "sl.dlss_d.dll",
        "sl.dlss_g.dll",
        "sl.interposer.dll",
        "sl.directsr.dll",
        "amd_fidelityfx_loader_dx12.dll",
        "amd_fidelityfx_loader_vk.dll"
    };

    private static readonly EnumerationOptions RecursiveExeEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive
    };

    public static string GetTargetFileName(UpscalerSwapSlot slot) => slot switch
    {
        UpscalerSwapSlot.Dlss => "nvngx_dlss.dll",
        UpscalerSwapSlot.DlssG => "nvngx_dlssg.dll",
        UpscalerSwapSlot.DlssD => "nvngx_dlssd.dll",
        UpscalerSwapSlot.Fsr31Dx12 => "amd_fidelityfx_dx12.dll",
        UpscalerSwapSlot.Fsr31Vk => "amd_fidelityfx_vk.dll",
        UpscalerSwapSlot.XeSs => "libxess.dll",
        UpscalerSwapSlot.XeSsFg => "libxess_fg.dll",
        UpscalerSwapSlot.XeLl => "libxell.dll",
        UpscalerSwapSlot.XeSsDx11 => "libxess_dx11.dll",
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unsupported upscaler swap slot.")
    };

    public static bool IsProtectedStreamlineOrLoaderFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && ProtectedFileNames.Contains(fileName);

    public static SwapTargetResolution ResolveTargetForMissingAsset(
        UpscalerSwapSlot slot,
        string installPath,
        IReadOnlyList<string>? knownAssetPaths = null,
        bool hasRuntimeManifest = false,
        string? runtimeInstallRoot = null,
        string? ngxModelsRootOverride = null)
    {
        var targetFileName = GetTargetFileName(slot);
        if (IsProtectedStreamlineOrLoaderFileName(targetFileName))
        {
            throw new InvalidOperationException($"Refusing to target protected file '{targetFileName}'.");
        }

        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return new SwapTargetResolution(
                CanSwap: false,
                TargetDirectory: string.Empty,
                TargetFileName: targetFileName,
                TargetPath: string.Empty,
                TargetAlreadyExistsOnDisk: false,
                ExistingBackupPath: null,
                Reason: "Game install folder was not found.");
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
        var discovery = UpscalerDiscoveryService.Discover(normalizedRoot, ngxModelsRootOverride);
        var hasRuntime = hasRuntimeManifest || HasOptiScalerRuntimeOnDisk(normalizedRoot, runtimeInstallRoot);

        if (!IsCapableForSlot(slot, discovery, hasRuntime, out var incapableReason))
        {
            return new SwapTargetResolution(
                CanSwap: false,
                TargetDirectory: string.Empty,
                TargetFileName: targetFileName,
                TargetPath: string.Empty,
                TargetAlreadyExistsOnDisk: false,
                ExistingBackupPath: null,
                Reason: incapableReason);
        }

        var targetDir = ResolveTargetDirectory(slot, normalizedRoot, discovery, knownAssetPaths, runtimeInstallRoot);
        var targetPath = Path.Combine(targetDir, targetFileName);
        var alreadyExists = File.Exists(targetPath);
        var backupCandidate = $"{targetPath}.dlsss";
        string? existingBackupPath = File.Exists(backupCandidate) ? backupCandidate : null;

        return new SwapTargetResolution(
            CanSwap: true,
            TargetDirectory: targetDir,
            TargetFileName: targetFileName,
            TargetPath: targetPath,
            TargetAlreadyExistsOnDisk: alreadyExists,
            ExistingBackupPath: existingBackupPath,
            Reason: string.Empty);
    }

    private static bool IsCapableForSlot(
        UpscalerSwapSlot slot,
        UpscalerDiscoveryResult discovery,
        bool hasRuntime,
        out string incapableReason)
    {
        if (hasRuntime)
        {
            incapableReason = string.Empty;
            return true;
        }

        switch (slot)
        {
            case UpscalerSwapSlot.Dlss:
                if (discovery.HasAnyDlssCapability || discovery.StreamlineStackDetected)
                {
                    incapableReason = string.Empty;
                    return true;
                }

                incapableReason = "This game has no detected DLSS, Streamline DLSS, or XTR runtime support. Install the XTR runtime first or verify the game supports this upscaler.";
                return false;

            case UpscalerSwapSlot.DlssG:
                if (discovery.HasAnyDlssGCapability)
                {
                    incapableReason = string.Empty;
                    return true;
                }

                incapableReason = "This game has no detected DLSS Frame Generation (DLSS-G) or XTR runtime support. Install the XTR runtime first or verify the game supports DLSS-G.";
                return false;

            case UpscalerSwapSlot.DlssD:
                if (discovery.HasAnyDlssDCapability)
                {
                    incapableReason = string.Empty;
                    return true;
                }

                incapableReason = "This game has no detected DLSS Ray Reconstruction (DLSS-D) or XTR runtime support. Install the XTR runtime first or verify the game supports DLSS-D.";
                return false;

            case UpscalerSwapSlot.Fsr31Dx12:
            case UpscalerSwapSlot.Fsr31Vk:
                if (discovery.Fsr31Dx12.Count > 0 ||
                    discovery.Fsr31Vk.Count > 0 ||
                    discovery.FidelityFxSdk2Files.Count > 0)
                {
                    incapableReason = string.Empty;
                    return true;
                }

                incapableReason = "This game has no detected FSR 3.1 / FidelityFX SDK 2 or XTR runtime support. Install the XTR runtime first or verify the game supports this upscaler.";
                return false;

            case UpscalerSwapSlot.XeSs:
            case UpscalerSwapSlot.XeSsFg:
            case UpscalerSwapSlot.XeLl:
            case UpscalerSwapSlot.XeSsDx11:
                if (discovery.XeSSFiles.Count > 0)
                {
                    incapableReason = string.Empty;
                    return true;
                }

                incapableReason = "This game has no detected XeSS or XTR runtime support. Install the XTR runtime first or verify the game supports this upscaler.";
                return false;

            default:
                incapableReason = "Unsupported upscaler type.";
                return false;
        }
    }

    private static string ResolveTargetDirectory(
        UpscalerSwapSlot slot,
        string normalizedRoot,
        UpscalerDiscoveryResult discovery,
        IReadOnlyList<string>? knownAssetPaths,
        string? runtimeInstallRoot)
    {
        // 1. Existing same-family asset directory (from knownAssetPaths or discovered files)
        var sameFamilyDir = PickBestAssetDirectory(
            GetSameFamilyCandidateFiles(slot, discovery, knownAssetPaths),
            normalizedRoot);
        if (sameFamilyDir is not null)
        {
            return sameFamilyDir;
        }

        // 2. Other detected upscaler asset directory
        var anyAssetCandidates = new List<string>();
        if (knownAssetPaths is not null)
        {
            anyAssetCandidates.AddRange(knownAssetPaths.Where(p =>
                !string.IsNullOrWhiteSpace(p) &&
                !p.EndsWith(".dlsss", StringComparison.OrdinalIgnoreCase)));
        }
        anyAssetCandidates.AddRange(discovery.Files.Select(f => f.FullPath));

        var anyAssetDir = PickBestAssetDirectory(anyAssetCandidates, normalizedRoot);
        if (anyAssetDir is not null)
        {
            return anyAssetDir;
        }

        // 3. Streamline interposer / DirectSR directory
        var slDir = PickBestAssetDirectory(
            discovery.StreamlineInterposer.Concat(discovery.StreamlineDirectSr).Select(f => f.FullPath),
            normalizedRoot);
        if (slDir is not null)
        {
            return slDir;
        }

        // 4. Runtime install root if valid and contains an .exe
        if (!string.IsNullOrWhiteSpace(runtimeInstallRoot) && Directory.Exists(runtimeInstallRoot))
        {
            try
            {
                if (Directory.EnumerateFiles(runtimeInstallRoot, "*.exe", SearchOption.TopDirectoryOnly).Any())
                {
                    return Path.TrimEndingDirectorySeparator(Path.GetFullPath(runtimeInstallRoot));
                }
            }
            catch
            {
                // Fall through to executable search.
            }
        }

        // 5. Main .exe directory
        var primaryExeDir = FindPrimaryExecutableDirectory(normalizedRoot);
        if (primaryExeDir is not null)
        {
            return primaryExeDir;
        }

        // 6. InstallPath fallback
        return normalizedRoot;
    }

    private static IEnumerable<string> GetSameFamilyCandidateFiles(
        UpscalerSwapSlot slot,
        UpscalerDiscoveryResult discovery,
        IReadOnlyList<string>? knownAssetPaths)
    {
        bool IsSameFamilyFileName(string fileName) => slot switch
        {
            UpscalerSwapSlot.Dlss or UpscalerSwapSlot.DlssG or UpscalerSwapSlot.DlssD =>
                fileName.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("sl.dlss", StringComparison.OrdinalIgnoreCase),
            UpscalerSwapSlot.Fsr31Dx12 or UpscalerSwapSlot.Fsr31Vk =>
                fileName.StartsWith("amd_fidelityfx_", StringComparison.OrdinalIgnoreCase),
            UpscalerSwapSlot.XeSs or UpscalerSwapSlot.XeSsFg or UpscalerSwapSlot.XeLl or UpscalerSwapSlot.XeSsDx11 =>
                fileName.StartsWith("libxess", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("libxell.dll", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        var results = new List<string>();
        if (knownAssetPaths is not null)
        {
            foreach (var p in knownAssetPaths)
            {
                if (!string.IsNullOrWhiteSpace(p) &&
                    !p.EndsWith(".dlsss", StringComparison.OrdinalIgnoreCase) &&
                    IsSameFamilyFileName(Path.GetFileName(p)))
                {
                    results.Add(p);
                }
            }
        }

        foreach (var file in discovery.Files)
        {
            if (IsSameFamilyFileName(file.FileName))
            {
                results.Add(file.FullPath);
            }
        }

        return results;
    }

    private static string? PickBestAssetDirectory(IEnumerable<string> filePaths, string normalizedRoot)
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            var normalizedDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            if (seen.Add(normalizedDir))
            {
                dirs.Add(normalizedDir);
            }
        }

        if (dirs.Count == 0)
        {
            return null;
        }

        return dirs
            .OrderByDescending(d => ScoreDirectory(d, normalizedRoot))
            .ThenBy(d => d.Length)
            .First();
    }

    private static int ScoreDirectory(string dir, string normalizedRoot)
    {
        var score = 0;
        var rel = Path.GetRelativePath(normalizedRoot, dir);
        if (rel == ".")
        {
            score += 20;
        }

        var normalizedRel = rel.Replace('\\', '/');
        if (normalizedRel.Contains("D3D12_Optiscaler", StringComparison.OrdinalIgnoreCase) ||
            normalizedRel.Contains("redist", StringComparison.OrdinalIgnoreCase) ||
            normalizedRel.Contains("tests", StringComparison.OrdinalIgnoreCase))
        {
            score -= 100;
        }

        try
        {
            if (Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                score += 50;
            }
            if (File.Exists(Path.Combine(dir, "sl.interposer.dll")))
            {
                score += 40;
            }
        }
        catch
        {
            // Ignore IO errors during scoring.
        }

        return score;
    }

    private static string? FindPrimaryExecutableDirectory(string normalizedRoot)
    {
        try
        {
            var topExes = Directory.EnumerateFiles(normalizedRoot, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(e => !IsIgnoredExecutable(e))
                .ToList();
            if (topExes.Count > 0)
            {
                return normalizedRoot;
            }

            var subExes = Directory.EnumerateFiles(normalizedRoot, "*.exe", RecursiveExeEnumerationOptions)
                .Where(e => !IsIgnoredExecutable(e))
                .OrderBy(e => e.Length)
                .ToList();

            if (subExes.Count > 0)
            {
                return Path.GetDirectoryName(subExes[0]);
            }
        }
        catch
        {
            // Fall back cleanly.
        }

        return null;
    }

    private static bool IsIgnoredExecutable(string exePath)
    {
        var full = exePath.Replace('\\', '/');
        if (full.Contains("/redist/", StringComparison.OrdinalIgnoreCase) ||
            full.Contains("/EasyAntiCheat/", StringComparison.OrdinalIgnoreCase) ||
            full.Contains("/BattlEye/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(exePath);
        return name.Contains("CrashReport", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("unins", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("BEService", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Launcher", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasOptiScalerRuntimeOnDisk(string installRoot, string? runtimeInstallRoot)
    {
        if (CheckDirForOptiScalerRuntime(installRoot))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(runtimeInstallRoot) &&
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(runtimeInstallRoot)), installRoot, StringComparison.OrdinalIgnoreCase) &&
            CheckDirForOptiScalerRuntime(runtimeInstallRoot))
        {
            return true;
        }

        return false;
    }

    private static bool CheckDirForOptiScalerRuntime(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return false;
        }

        if (File.Exists(Path.Combine(dir, "OptiScaler.ini")))
        {
            return true;
        }

        foreach (var proxy in ProxyFileNames)
        {
            var candidate = Path.Combine(dir, proxy);
            if (File.Exists(candidate) && RuntimeRestorePlanner.IsXtrExclusiveFile(proxy, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
