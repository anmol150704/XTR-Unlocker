using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

public enum PreflightStatus
{
    Ready,
    UpdateAvailable,
    NeedsAttention,
    RestoreAvailable,
    Experimental,
    Blocked
}

public enum CheckItemSeverity
{
    Passed,
    Warning,
    Failed
}

public sealed class PreflightCheckItem
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CheckItemSeverity Severity { get; set; } = CheckItemSeverity.Passed;
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class RuntimePreflightResult
{
    public string GameId { get; set; } = string.Empty;
    public string GameRoot { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public string DetectedApi { get; set; } = "Auto";
    public bool NativeDlssDetected { get; set; }
    public string? NativeDlssVersion { get; set; }
    public bool NativeDlssGDetected { get; set; }
    public bool StreamlineDetected { get; set; }
    public bool HasExistingDeployment { get; set; }
    public string? ExistingDeploymentVersion { get; set; }
    public bool DriftDetected { get; set; }
    public List<string> DriftDetails { get; set; } = new();
    public List<string> ProxyConflicts { get; set; } = new();
    public bool HasAntiCheatRisk { get; set; }
    public string? AntiCheatBinary { get; set; }
    public bool PackIntegrityValid { get; set; }
    public List<string> PackIntegrityErrors { get; set; } = new();
    public PreflightStatus Status { get; set; } = PreflightStatus.Ready;
    public string Headline { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool CanProceed { get; set; } = true;
    public List<PreflightCheckItem> Checks { get; set; } = new();
}

public static class RuntimePreflightService
{
    private static readonly HashSet<string> KnownAntiCheatBinaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyAntiCheat.exe", "EasyAntiCheat_Setup.exe", "EasyAntiCheat_EOS.exe", "EasyAntiCheat_EOS_Setup.exe",
        "BEService.exe", "BEService_x64.exe", "BattlEye.exe", "BEDaisy.sys",
        "start_protected_game.exe", "vgc.exe", "vgk.sys", "ACE-Base.sys", "TenProtect.exe"
    };

    public static async Task<RuntimePreflightResult> RunPreflightAsync(
        string gameId,
        string gameRoot,
        RuntimePack pack,
        string proxyName = "dxgi.dll",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentNullException.ThrowIfNull(pack);

        var result = new RuntimePreflightResult
        {
            GameId = gameId,
            GameRoot = gameRoot
        };

        // 1. Check game folder existence & executable detection
        if (!Directory.Exists(gameRoot))
        {
            result.Status = PreflightStatus.Blocked;
            result.CanProceed = false;
            result.Headline = "Game folder not found";
            result.Explanation = $"The specified folder does not exist: {gameRoot}";
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "Executable & Root",
                Name = "Directory existence",
                Severity = CheckItemSeverity.Failed,
                Message = "Folder not found",
                Details = gameRoot
            });
            return result;
        }

        var executables = Directory.EnumerateFiles(gameRoot, "*.exe", SearchOption.TopDirectoryOnly).ToList();
        var primaryExe = FindPrimaryExecutable(executables, gameRoot);
        result.ExecutablePath = primaryExe;

        if (primaryExe is null)
        {
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "Executable & Root",
                Name = "Primary executable",
                Severity = CheckItemSeverity.Failed,
                Message = "No executable detected in root folder",
                Details = "XTR installs the runtime proxy beside the main game executable."
            });
            result.Status = PreflightStatus.Blocked;
            result.CanProceed = false;
            result.Headline = "No game executable found";
            result.Explanation = "Choose the folder containing the game executable.";
            return result;
        }
        else
        {
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "Executable & Root",
                Name = "Primary executable",
                Severity = CheckItemSeverity.Passed,
                Message = Path.GetFileName(primaryExe),
                Details = primaryExe
            });
        }

        // 2. Anti-cheat / multiplayer risk check
        var detectedAntiCheat = FindAntiCheat(gameRoot);
        if (detectedAntiCheat is not null)
        {
            result.HasAntiCheatRisk = true;
            result.AntiCheatBinary = detectedAntiCheat;
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "Anti-Cheat & Multiplayer",
                Name = "Anti-cheat detection",
                Severity = CheckItemSeverity.Warning,
                Message = $"Anti-cheat binary detected: {Path.GetFileName(detectedAntiCheat)}",
                Details = "Modifying runtime DLLs in multiplayer games can trigger anti-cheat bans. Use only with offline or single-player modes."
            });
        }
        else
        {
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "Anti-Cheat & Multiplayer",
                Name = "Anti-cheat detection",
                Severity = CheckItemSeverity.Passed,
                Message = "No known multiplayer anti-cheat found",
                Details = "Clean offline environment."
            });
        }

        // 3. Graphics API and DLSS detection
        result.DetectedApi = DetectGraphicsApi(gameRoot);
        result.Checks.Add(new PreflightCheckItem
        {
            Category = "Graphics API",
            Name = "Rendering API",
            Severity = CheckItemSeverity.Passed,
            Message = $"Detected API: {result.DetectedApi}",
            Details = $"API detected via root libraries and shaders in {Path.GetFileName(gameRoot)}."
        });

        var dlssDll = FindFileIgnoreCase(gameRoot, "nvngx_dlss.dll");
        if (dlssDll is not null)
        {
            result.NativeDlssDetected = true;
            var ver = FileVersionInfo.GetVersionInfo(dlssDll).ProductVersion;
            result.NativeDlssVersion = string.IsNullOrWhiteSpace(ver) ? "detected" : ver;
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "DLSS Integration",
                Name = "Native DLSS upscaler",
                Severity = CheckItemSeverity.Passed,
                Message = $"Native DLSS {result.NativeDlssVersion} found",
                Details = dlssDll
            });
        }
        else
        {
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "DLSS Integration",
                Name = "Native DLSS upscaler",
                Severity = CheckItemSeverity.Warning,
                Message = "No nvngx_dlss.dll found in target folder",
                Details = "The game may load DLSS from a subfolder or rely on FSR/XeSS fallback."
            });
        }

        var dlssgDll = FindFileIgnoreCase(gameRoot, "nvngx_dlssg.dll");
        if (dlssgDll is not null)
        {
            result.NativeDlssGDetected = true;
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "DLSS Integration",
                Name = "Native Frame Generation",
                Severity = CheckItemSeverity.Passed,
                Message = "Native DLSS-G frame generation detected",
                Details = dlssgDll
            });
        }

        var streamlineDll = FindFileIgnoreCase(gameRoot, "sl.interposer.dll");
        if (streamlineDll is not null)
        {
            result.StreamlineDetected = true;
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "DLSS Integration",
                Name = "Streamline Interposer",
                Severity = CheckItemSeverity.Passed,
                Message = "Streamline integration detected",
                Details = streamlineDll
            });
        }

        // 4. Runtime pack integrity check
        if (pack.PackageRoot is not null && Directory.Exists(pack.PackageRoot))
        {
            var (isValid, errors) = await pack.VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
            result.PackIntegrityValid = isValid;
            result.PackIntegrityErrors = errors;
            if (isValid)
            {
                result.Checks.Add(new PreflightCheckItem
                {
                    Category = "Runtime Pack",
                    Name = "Cryptographic integrity",
                    Severity = CheckItemSeverity.Passed,
                    Message = $"{pack.DisplayName} verified ({pack.FileHashes.Count} files match SHA-256)",
                    Details = $"Pack ID: {pack.Id}, version: {pack.Version}"
                });
            }
            else
            {
                result.Checks.Add(new PreflightCheckItem
                {
                    Category = "Runtime Pack",
                    Name = "Cryptographic integrity",
                    Severity = pack.IsTrusted ? CheckItemSeverity.Failed : CheckItemSeverity.Warning,
                    Message = pack.IsTrusted ? "Package integrity check failed" : "Unverified package (custom/local)",
                    Details = string.Join("; ", errors)
                });
                if (pack.IsTrusted)
                {
                    result.CanProceed = false;
                    result.Status = PreflightStatus.Blocked;
                    result.Headline = "Runtime package integrity failed";
                    result.Explanation = "One or more package files do not match their cryptographic signatures.";
                    return result;
                }
            }
        }

        // 5. Check existing XTR deployment and drift
        var existingManifest = await RuntimeInstallManifestStore.LoadAsync(gameId, cancellationToken).ConfigureAwait(false);
        if (existingManifest is not null)
        {
            result.HasExistingDeployment = true;
            result.ExistingDeploymentVersion = existingManifest.RuntimeVersion;

            // Verify if files have drifted
            var driftList = new List<string>();
            foreach (var file in existingManifest.Files)
            {
                var filePath = Path.Combine(gameRoot, file.RelativePath);
                if (!File.Exists(filePath))
                {
                    driftList.Add($"Missing file: {file.RelativePath}");
                }
                else if (!string.Equals(file.RelativePath, "OptiScaler.ini", StringComparison.OrdinalIgnoreCase))
                {
                    var actualHash = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(actualHash, file.InstalledSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        driftList.Add($"Modified file: {file.RelativePath}");
                    }
                }
            }

            if (driftList.Count > 0)
            {
                result.DriftDetected = true;
                result.DriftDetails = driftList;
                result.Checks.Add(new PreflightCheckItem
                {
                    Category = "Existing Deployment",
                    Name = "Installation drift",
                    Severity = CheckItemSeverity.Warning,
                    Message = $"{driftList.Count} files modified since install",
                    Details = string.Join(Environment.NewLine, driftList)
                });
            }
            else
            {
                result.Checks.Add(new PreflightCheckItem
                {
                    Category = "Existing Deployment",
                    Name = "Installation drift",
                    Severity = CheckItemSeverity.Passed,
                    Message = "Installed files intact",
                    Details = $"Managed runtime {existingManifest.RuntimeVersion} cleanly verified."
                });
            }
        }

        // 6. Proxy conflict check
        var targetProxyPath = Path.Combine(gameRoot, proxyName);
        if (File.Exists(targetProxyPath))
        {
            var isOwnedByXtr = existingManifest?.Files.Any(f => string.Equals(f.RelativePath, proxyName, StringComparison.OrdinalIgnoreCase)) ?? false;
            if (!isOwnedByXtr)
            {
                result.ProxyConflicts.Add($"An existing {proxyName} is already present and not owned by XTR.");
                result.Checks.Add(new PreflightCheckItem
                {
                    Category = "Proxy Configuration",
                    Name = "Proxy collision",
                    Severity = CheckItemSeverity.Warning,
                    Message = $"{proxyName} already exists in game folder",
                    Details = "XTR will back up this file before placing the runtime proxy."
                });
            }
            else
            {
                result.Checks.Add(new PreflightCheckItem
                {
                    Category = "Proxy Configuration",
                    Name = "Proxy file",
                    Severity = CheckItemSeverity.Passed,
                    Message = $"{proxyName} owned by XTR",
                    Details = targetProxyPath
                });
            }
        }
        else
        {
            result.Checks.Add(new PreflightCheckItem
            {
                Category = "Proxy Configuration",
                Name = "Proxy destination",
                Severity = CheckItemSeverity.Passed,
                Message = $"{proxyName} destination is clear",
                Details = "No collision detected."
            });
        }

        // Synthesize status & Apple-style concise headline
        if (result.HasExistingDeployment)
        {
            if (result.DriftDetected)
            {
                result.Status = PreflightStatus.NeedsAttention;
                result.Headline = "Needs attention";
                result.Explanation = "Game files were modified externally since XTR installed the runtime.";
            }
            else if (pack.Version != result.ExistingDeploymentVersion)
            {
                result.Status = PreflightStatus.UpdateAvailable;
                result.Headline = "Update available";
                result.Explanation = $"Current runtime {result.ExistingDeploymentVersion} can be updated to {pack.Version}.";
            }
            else
            {
                result.Status = PreflightStatus.Ready;
                result.Headline = "Ready";
                result.Explanation = "Runtime installed and verified.";
            }
        }
        else if (result.HasAntiCheatRisk)
        {
            result.Status = PreflightStatus.NeedsAttention;
            result.Headline = "Needs attention";
            result.Explanation = "Multiplayer anti-cheat detected. Recommended only for offline single-player.";
        }
        else if (!pack.IsTrusted || pack.ReleaseChannel == RuntimeReleaseChannel.CustomLocal)
        {
            result.Status = PreflightStatus.Experimental;
            result.Headline = "Experimental";
            result.Explanation = "Using custom or experimental runtime pack.";
        }
        else
        {
            result.Status = PreflightStatus.Ready;
            result.Headline = "Ready";
            result.Explanation = "Compatible environment and verified runtime package.";
        }

        return result;
    }

    private static string? FindPrimaryExecutable(List<string> executables, string gameRoot)
    {
        if (executables.Count == 0) return null;
        if (executables.Count == 1) return executables[0];

        var folderName = Path.GetFileName(gameRoot);
        // Exclude common crash helpers, installers, launchers
        var filtered = executables.Where(exe =>
        {
            var name = Path.GetFileNameWithoutExtension(exe);
            return !name.Contains("CrashReport", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("unins", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("BEService", StringComparison.OrdinalIgnoreCase) &&
                   !name.EndsWith("Launcher", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        if (filtered.Count == 1) return filtered[0];

        // Try matching folder name
        var matching = (filtered.Count > 0 ? filtered : executables)
            .FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(folderName, StringComparison.OrdinalIgnoreCase));

        return matching ?? (filtered.Count > 0 ? filtered[0] : executables[0]);
    }

    private static string? FindAntiCheat(string gameRoot)
    {
        try
        {
            var files = Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories).Take(500);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (KnownAntiCheatBinaries.Contains(fileName) ||
                    fileName.StartsWith("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("BEService", StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }
        catch
        {
            // Ignore directory enumeration access issues
        }

        return null;
    }

    private static string DetectGraphicsApi(string gameRoot)
    {
        if (FindFileIgnoreCase(gameRoot, "D3D12Core.dll") is not null ||
            Directory.Exists(Path.Combine(gameRoot, "D3D12")) ||
            Directory.GetFiles(gameRoot, "*d3d12*.dll", SearchOption.TopDirectoryOnly).Length > 0)
        {
            return "DX12";
        }

        if (Directory.GetFiles(gameRoot, "*vulkan*.dll", SearchOption.TopDirectoryOnly).Length > 0 ||
            FindFileIgnoreCase(gameRoot, "vulkan-1.dll") is not null)
        {
            return "Vulkan";
        }

        if (Directory.GetFiles(gameRoot, "*d3d11*.dll", SearchOption.TopDirectoryOnly).Length > 0)
        {
            return "DX11";
        }

        return "DX12"; // Default modern target
    }

    private static string? FindFileIgnoreCase(string directory, string filename)
    {
        try
        {
            return Directory.EnumerateFiles(directory, filename, SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
