using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DLSS_Swapper.Data.Runtime;

internal enum RestoreFileActionKind
{
    SkipMissing,
    RestoreFromBackupViaCopy,
    RestoreFromBackupViaReplace,
    DeleteXtrFile,
    PreserveExternalFile
}

internal sealed record RestoreFilePlan(
    RuntimeOwnedFile Entry,
    RestoreFileActionKind Action,
    string InstalledPath,
    string? BackupPath);

internal static class RuntimeRestorePlanner
{
    private static readonly string[] XtrExclusiveExactPaths =
    [
        "OptiScaler.ini",
        "OptiScaler.log",
        "xtr-runtime.json",
        "plugins/xtr_hud.asi"
    ];

    internal static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    internal static bool IsXtrExclusiveFile(string relativePath, string? fullPathOnDisk)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = NormalizeRelativePath(relativePath);
        foreach (var exact in XtrExclusiveExactPaths)
        {
            if (string.Equals(normalized, exact, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (normalized.StartsWith("D3D12_Optiscaler/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(fullPathOnDisk) && File.Exists(fullPathOnDisk))
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(fullPathOnDisk);
                if (string.Equals(versionInfo.OriginalFilename?.Trim(), "OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Non-PE or unreadable version info: fall back to path rules only.
            }
        }

        return false;
    }

    internal static RestoreFilePlan PlanEntry(
        RuntimeOwnedFile entry,
        string installedPath,
        bool installedExists,
        string? installedSha256,
        string? backupPath,
        bool backupExists)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!installedExists)
        {
            if (entry.OriginalBackupPath is not null && backupExists && backupPath is not null)
            {
                return new RestoreFilePlan(entry, RestoreFileActionKind.RestoreFromBackupViaCopy, installedPath, backupPath);
            }

            return new RestoreFilePlan(entry, RestoreFileActionKind.SkipMissing, installedPath, backupPath);
        }

        // File exists on disk
        if (entry.OriginalBackupPath is not null)
        {
            if (backupExists && backupPath is not null)
            {
                // Restore original backup cleanly even if the deployed/updated file on disk changed externally.
                return new RestoreFilePlan(entry, RestoreFileActionKind.RestoreFromBackupViaReplace, installedPath, backupPath);
            }

            // Backup file itself is missing: clean up XTR-exclusive files, preserve non-XTR game files.
            return IsXtrExclusiveFile(entry.RelativePath, installedPath)
                ? new RestoreFilePlan(entry, RestoreFileActionKind.DeleteXtrFile, installedPath, null)
                : new RestoreFilePlan(entry, RestoreFileActionKind.PreserveExternalFile, installedPath, null);
        }

        // OriginalBackupPath == null: delete if unmodified XTR file OR XTR-exclusive; otherwise preserve externally updated game/vendor file.
        var hashMatches = installedSha256 is not null &&
                          string.Equals(installedSha256, entry.InstalledSha256, StringComparison.OrdinalIgnoreCase);

        if (hashMatches || IsXtrExclusiveFile(entry.RelativePath, installedPath))
        {
            return new RestoreFilePlan(entry, RestoreFileActionKind.DeleteXtrFile, installedPath, null);
        }

        return new RestoreFilePlan(entry, RestoreFileActionKind.PreserveExternalFile, installedPath, null);
    }

    internal static IReadOnlyList<string> GetCandidateCleanupDirectories(string installRoot, IEnumerable<string> relativePaths)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(normalizedRoot, "D3D12_Optiscaler")
        };

        foreach (var rel in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(rel))
            {
                continue;
            }

            var combined = Path.GetFullPath(Path.Combine(normalizedRoot, rel));
            var parent = Path.GetDirectoryName(combined);
            while (!string.IsNullOrEmpty(parent) &&
                   parent.Length > normalizedRoot.Length &&
                   parent.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                dirs.Add(parent);
                parent = Path.GetDirectoryName(parent);
            }
        }

        // Deepest paths first so child directories are removed before parents.
        var ordered = new List<string>(dirs);
        ordered.Sort((a, b) => b.Length.CompareTo(a.Length));
        return ordered;
    }
}
