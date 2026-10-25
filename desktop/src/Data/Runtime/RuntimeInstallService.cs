using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Installs an extracted OptiScaler release for one game and records ownership so it
/// can be safely restored later. It never overwrites a game file without first backing it up.
/// </summary>
internal static class RuntimeInstallService
{
    private const string RuntimeId = "optiscaler-xtr";
    private static readonly string BundledPackagePath = Path.Combine(AppContext.BaseDirectory, "Runtime", "OptiScaler");
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GameLocks = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ProxyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dxgi.dll", "winmm.dll", "d3d12.dll", "version.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll"
    };

    internal static string? GetBundledPackagePath()
    {
        return File.Exists(Path.Combine(BundledPackagePath, "OptiScaler.dll")) &&
               File.Exists(Path.Combine(BundledPackagePath, "OptiScaler.ini"))
            ? BundledPackagePath
            : null;
    }

    internal static async Task<RuntimeInstallManifest> InstallAsync(
        string gameId,
        string gameRoot,
        string packageRoot,
        string proxyName,
        CancellationToken cancellationToken = default)
    {
        var gameLock = GameLocks.GetOrAdd(gameId, static _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InstallCoreAsync(gameId, gameRoot, packageRoot, proxyName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gameLock.Release();
        }
    }

    internal static async Task<RuntimeInstallManifest> InstallPlanAsync(
        DeploymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExecute)
            throw new InvalidOperationException($"Cannot execute deployment plan: {plan.Summary}");

        var gameLock = GameLocks.GetOrAdd(plan.GameId, static _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await InstallCoreAsync(
                plan.GameId,
                plan.GameRoot,
                plan.Pack.PackageRoot ?? throw new InvalidOperationException("Package root is missing from plan."),
                plan.SelectedProxy,
                cancellationToken).ConfigureAwait(false);

            if (plan.PlannedConfigModifications.Count > 0)
            {
                var profile = new RuntimeIntegrationProfile
                {
                    GameId = plan.GameId,
                    RuntimeId = manifest.RuntimeId,
                    RuntimeVersion = manifest.RuntimeVersion,
                    ProxyFileName = manifest.ProxyFileName ?? plan.SelectedProxy,
                    Api = plan.PreflightResult.DetectedApi,
                    SupportState = plan.Pack.IsTrusted ? "supported" : "experimental",
                    SupportReason = plan.Summary
                };

                foreach (var mod in plan.PlannedConfigModifications)
                {
                    if (mod.Section.Equals("Upscalers", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.SelectedUpscaler = mod.Value;
                    }
                    else if (mod.Section.Equals("FrameGen", StringComparison.OrdinalIgnoreCase) && mod.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.FrameGenerationEnabled = mod.Value;
                    }
                    else if (mod.Section.Equals("FrameGen", StringComparison.OrdinalIgnoreCase) && mod.Key.Equals("FGInput", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.FrameGenerationInput = mod.Value;
                    }
                    else if (mod.Section.Equals("FrameGen", StringComparison.OrdinalIgnoreCase) && mod.Key.Equals("FGOutput", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.FrameGenerationOutput = mod.Value;
                    }
                }

                if (plan.Preset == RuntimeIntegrationPreset.TurboBoost)
                {
                    profile.Options["Preset"] = "TurboBoost";
                }
                else if (plan.Preset == RuntimeIntegrationPreset.NativeDlssG)
                {
                    profile.Options["Preset"] = "NativeDlssG";
                }
                else if (plan.Preset == RuntimeIntegrationPreset.DlssWithFrameGeneration)
                {
                    profile.Options["Preset"] = "DlssWithFrameGeneration";
                }
                else if (plan.Preset == RuntimeIntegrationPreset.LatestDlssOnly)
                {
                    profile.Options["Preset"] = "LatestDlssOnly";
                }

                await RuntimeConfigService.ApplyModificationsAsync(manifest.InstallRoot, plan.PlannedConfigModifications, cancellationToken).ConfigureAwait(false);
                await RuntimeIntegrationProfileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
            }

            return manifest;
        }
        finally
        {
            gameLock.Release();
        }
    }

    private static async Task<RuntimeInstallManifest> InstallCoreAsync(
        string gameId,
        string gameRoot,
        string packageRoot,
        string proxyName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        if (!ProxyNames.Contains(proxyName))
            throw new InvalidDataException("Choose a supported OptiScaler proxy filename.");

        var root = NormalizeExistingDirectory(gameRoot, "The game install folder could not be found.");
        var package = NormalizeExistingDirectory(packageRoot, "The selected OptiScaler package folder could not be found.");
        if (!Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly).Any())
            throw new InvalidDataException("Choose the folder containing the game's executable. XTR will place the runtime beside that executable.");

        var runtimeDll = Path.Combine(package, "OptiScaler.dll");
        var runtimeIni = Path.Combine(package, "OptiScaler.ini");
        if (!File.Exists(runtimeDll) || !File.Exists(runtimeIni))
            throw new InvalidDataException("Select an extracted OptiScaler release folder containing OptiScaler.dll and OptiScaler.ini.");

        if (await RuntimeInstallManifestStore.LoadAsync(gameId, cancellationToken).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("XTR already manages a runtime for this game. Restore it before installing another runtime package.");

        var sourceFiles = EnumeratePackageFiles(package, proxyName);
        var bundleMetadata = await RuntimeBundleMetadata.LoadAsync(package, cancellationToken).ConfigureAwait(false);
        var backupRoot = Storage.GetRuntimeBackupsPath(gameId);
        Directory.CreateDirectory(backupRoot);
        var backupSessionId = Guid.NewGuid().ToString("N");
        var manifest = new RuntimeInstallManifest
        {
            GameId = gameId,
            RuntimeId = bundleMetadata?.Id ?? RuntimeId,
            RuntimeVersion = bundleMetadata?.Version ?? FileVersionInfo.GetVersionInfo(runtimeDll).ProductVersion ?? "unknown",
            ProxyFileName = proxyName,
            InstallRoot = root,
            InstalledUtc = DateTimeOffset.UtcNow,
        };
        if (bundleMetadata is not null)
            manifest.Components.AddRange(bundleMetadata.Components.Where(component => !string.IsNullOrWhiteSpace(component)));
        if (manifest.Components.Count == 0)
            manifest.Components.Add("OptiScaler engine");
        var packageRelativePaths = sourceFiles.Select(file => file.Item2).ToArray();
        if (packageRelativePaths.Any(path => path.Contains("dlssg_sm86", StringComparison.OrdinalIgnoreCase)))
            manifest.Components.Add("RTX 20/30 frame-generation unlocker");
        if (packageRelativePaths.Any(path => path.Contains("dlss-enabler-headless", StringComparison.OrdinalIgnoreCase)))
            manifest.Components.Add("DLSS Enabler companion");
        if (packageRelativePaths.Any(path => path.StartsWith("OptiScaler/streamline/", StringComparison.OrdinalIgnoreCase)))
            manifest.Components.Add("Streamline bridge");
        if (packageRelativePaths.Any(path => path.Contains("dlssg_to_fsr3", StringComparison.OrdinalIgnoreCase)))
            manifest.Components.Add("DLSSG to FSR3 bridge");
        manifest.Components = manifest.Components
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stagingRoot = Path.Combine(Storage.GetTemp(), "runtime-install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var committed = new List<RuntimeOwnedFile>();

        try
        {
            // Stage and hash the complete payload before changing anything in the game folder.
            foreach (var (sourcePath, relativePath) in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagedPath = ResolveBeneath(stagingRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                File.Copy(sourcePath, stagedPath, overwrite: false);

                var destinationPath = ResolveBeneath(root, relativePath);
                EnsureNoReparsePoints(root, relativePath);
                if (Directory.Exists(destinationPath))
                    throw new IOException($"A folder already exists where XTR needs to install {relativePath}.");

                var entry = new RuntimeOwnedFile
                {
                    RelativePath = relativePath,
                    InstalledSha256 = await HashFileAsync(stagedPath, cancellationToken).ConfigureAwait(false),
                };

                if (File.Exists(destinationPath))
                {
                    var originalHash = await HashFileAsync(destinationPath, cancellationToken).ConfigureAwait(false);
                    var backupRelativePath = Path.Combine(backupSessionId, relativePath);
                    var backupPath = ResolveBeneath(backupRoot, backupRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destinationPath, backupPath, overwrite: false);
                    entry.OriginalBackupPath = backupRelativePath.Replace(Path.DirectorySeparatorChar, '/');
                    entry.OriginalSha256 = originalHash;
                }

                manifest.Files.Add(entry);
            }

            foreach (var entry in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagedPath = ResolveBeneath(stagingRoot, entry.RelativePath);
                var destinationPath = ResolveBeneath(root, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var temporaryDestination = destinationPath + $".{Guid.NewGuid():N}.xtrtmp";
                try
                {
                    File.Copy(stagedPath, temporaryDestination, overwrite: false);
                    if (File.Exists(destinationPath))
                        File.Replace(temporaryDestination, destinationPath, null, ignoreMetadataErrors: true);
                    else
                        File.Move(temporaryDestination, destinationPath);
                    committed.Add(entry);
                }
                finally
                {
                    TryDeleteFile(temporaryDestination);
                }
            }

            await RuntimeInstallManifestStore.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        catch (Exception installError)
        {
            if (!RollBackCommittedFiles(root, backupRoot, committed))
            {
                throw new IOException(
                    $"XTR could not completely undo the partial install. It preserved original files in {backupRoot}; keep that folder for recovery.",
                    installError);
            }

            TryDeleteDirectory(ResolveBeneath(backupRoot, backupSessionId));
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    internal static async Task RestoreAsync(
        string gameId,
        string gameRoot,
        CancellationToken cancellationToken = default)
    {
        var gameLock = GameLocks.GetOrAdd(gameId, static _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreCoreAsync(gameId, gameRoot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gameLock.Release();
        }
    }

    private static async Task RestoreCoreAsync(
        string gameId,
        string gameRoot,
        CancellationToken cancellationToken)
    {
        var manifest = await RuntimeInstallManifestStore.LoadAsync(gameId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("XTR has no runtime install record for this game.");
        var root = Path.GetFullPath(gameRoot);
        if (!PathEquals(root, manifest.InstallRoot))
            throw new InvalidOperationException("The game's install folder changed since the runtime was installed. Refresh the game library before restoring.");

        var backupRoot = Storage.GetRuntimeBackupsPath(gameId);
        foreach (var entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoints(root, entry.RelativePath);
            var installedPath = ResolveBeneath(root, entry.RelativePath);
            if (!File.Exists(installedPath) ||
                (!string.Equals(
                     await HashFileAsync(installedPath, cancellationToken).ConfigureAwait(false),
                     entry.InstalledSha256,
                     StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(entry.RelativePath, "OptiScaler.ini", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"XTR found a changed or missing game file ({entry.RelativePath}) and stopped without restoring. The file may have been changed by the game, another mod, or you.");
            }

            if (entry.OriginalBackupPath is not null)
            {
                var backupPath = ResolveBeneath(backupRoot, entry.OriginalBackupPath);
                if (!File.Exists(backupPath) || !string.Equals(
                        await HashFileAsync(backupPath, cancellationToken).ConfigureAwait(false),
                        entry.OriginalSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"XTR's backup for {entry.RelativePath} is missing or damaged. No files were restored.");
                }
            }
        }

        var rollbackPaths = new Dictionary<RuntimeOwnedFile, string>();
        var restoreCommitted = new List<RuntimeOwnedFile>();
        var preserveRollbackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var installedPath = ResolveBeneath(root, entry.RelativePath);
                var rollbackPath = installedPath + $".{Guid.NewGuid():N}.xtrrollback";
                if (entry.OriginalBackupPath is not null)
                {
                    var backupPath = ResolveBeneath(backupRoot, entry.OriginalBackupPath);
                    var temporaryPath = installedPath + $".{Guid.NewGuid():N}.xtrtmp";
                    try
                    {
                        File.Copy(backupPath, temporaryPath, overwrite: false);
                        File.Replace(temporaryPath, installedPath, rollbackPath, ignoreMetadataErrors: true);
                        rollbackPaths.Add(entry, rollbackPath);
                        restoreCommitted.Add(entry);
                    }
                    finally
                    {
                        TryDeleteFile(temporaryPath);
                    }
                }
                else
                {
                    File.Move(installedPath, rollbackPath);
                    rollbackPaths.Add(entry, rollbackPath);
                    restoreCommitted.Add(entry);
                }
            }

            await RuntimeInstallManifestStore.DeleteAsync(gameId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (var entry in restoreCommitted.AsEnumerable().Reverse())
            {
                var rollbackPath = rollbackPaths[entry];
                try
                {
                    var installedPath = ResolveBeneath(root, entry.RelativePath);
                    if (entry.OriginalBackupPath is null)
                        File.Move(rollbackPath, installedPath);
                    else
                        File.Replace(rollbackPath, installedPath, null, ignoreMetadataErrors: true);
                }
                catch
                {
                    // Keep the original restore error; the rollback file remains beside the game file.
                    preserveRollbackPaths.Add(rollbackPath);
                }
            }

            throw;
        }
        finally
        {
            foreach (var rollbackPath in rollbackPaths.Values)
            {
                if (!preserveRollbackPaths.Contains(rollbackPath))
                    TryDeleteFile(rollbackPath);
            }
        }

        DeleteManifestBackupSessions(manifest, backupRoot);
    }

    private static List<(string SourcePath, string RelativePath)> EnumeratePackageFiles(string packageRoot, string proxyName)
    {
        var results = new List<(string, string)>();
        foreach (var file in EnumerateFilesWithoutReparsePoints(packageRoot))
        {
            var relative = Path.GetRelativePath(packageRoot, file);
            if (relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relative, "setup_windows.bat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relative, "setup_linux.sh", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("!!", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ValidateRelativePath(relative);
            if (string.Equals(relative, "OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                relative = proxyName;
            results.Add((file, relative));
        }

        if (!results.Any(x => string.Equals(x.Item2, proxyName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The selected package does not contain OptiScaler.dll.");

        var duplicate = results.GroupBy(x => x.Item2, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"The package contains more than one file that would install as {duplicate.Key}.");

        return results;
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);
        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The runtime package contains a symbolic link or reparse point and cannot be installed.");

                if ((attributes & FileAttributes.Directory) != 0)
                    pendingDirectories.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    private static void EnsureNoReparsePoints(string root, string relativePath)
    {
        var current = Path.GetFullPath(root);
        var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"XTR stopped because {segment} is a symbolic link or reparse point.");
        }
    }

    private static bool RollBackCommittedFiles(string root, string backupRoot, IEnumerable<RuntimeOwnedFile> committed)
    {
        var succeeded = true;
        foreach (var entry in committed.Reverse())
        {
            try
            {
                var destinationPath = ResolveBeneath(root, entry.RelativePath);
                if (!File.Exists(destinationPath))
                {
                    if (entry.OriginalBackupPath is not null)
                    {
                        var backupPath = ResolveBeneath(backupRoot, entry.OriginalBackupPath);
                        RestoreBackupFile(backupPath, destinationPath);
                    }
                    continue;
                }

                if (!string.Equals(
                        HashFileAsync(destinationPath, CancellationToken.None).GetAwaiter().GetResult(),
                        entry.InstalledSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    succeeded = false;
                    continue;
                }

                if (entry.OriginalBackupPath is not null)
                {
                    var backupPath = ResolveBeneath(backupRoot, entry.OriginalBackupPath);
                    if (File.Exists(backupPath))
                        RestoreBackupFile(backupPath, destinationPath);
                    else
                        succeeded = false;
                }
                else
                {
                    File.Delete(destinationPath);
                }
            }
            catch
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    private static void RestoreBackupFile(string backupPath, string destinationPath)
    {
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.xtrtmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(backupPath, temporaryPath, overwrite: false);
            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void DeleteManifestBackupSessions(RuntimeInstallManifest manifest, string backupRoot)
    {
        var backupPaths = manifest.Files
            .Where(file => file.OriginalBackupPath is not null)
            .Select(file => file.OriginalBackupPath!.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        // Older manifests stored backups directly under backupRoot instead of a session folder.
        foreach (var segments in backupPaths.Where(segments => segments.Length == 1))
            TryDeleteFile(ResolveBeneath(backupRoot, segments[0]));

        var sessionIds = backupPaths
            .Where(segments => segments.Length > 1)
            .Select(segments => segments[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var sessionId in sessionIds)
            TryDeleteDirectory(ResolveBeneath(backupRoot, sessionId));
    }

    private static string NormalizeExistingDirectory(string path, string errorMessage)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(errorMessage);
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("XTR cannot install from or into a symbolic link or reparse point.");
        return fullPath;
    }

    private static string ResolveBeneath(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A runtime package path escapes its install folder.");
        return candidate;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new InvalidDataException("The runtime package contains an invalid path.");

        foreach (var segment in path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("The runtime package contains an invalid path.");
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
