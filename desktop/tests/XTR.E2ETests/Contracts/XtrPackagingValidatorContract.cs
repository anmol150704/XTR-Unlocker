using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace XTR.E2ETests.Contracts;

public sealed class RuntimeFileHashRecord
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class XtrRuntimeManifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<RuntimeFileHashRecord> FileHashes { get; set; } = [];
}

public static class XtrPackagingValidatorContract
{
    /// <summary>
    /// R2 target contract: 18 pinned runtime &amp; license files (plugins/xtr_hud.asi removed).
    /// </summary>
    public const int ExpectedPinnedFileCount = 18;

    private static readonly HashSet<string> AllowedNonPinnedRelativeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "xtr-runtime.json",
        "setup_windows.bat",
        "setup_linux.sh",
        "!! README_EXTRACT ALL FILES TO GAME FOLDER !!.txt"
    };

    public static XtrRuntimeManifest LoadManifest(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Manifest not found at {jsonPath}");

        string json = File.ReadAllText(jsonPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var manifest = JsonSerializer.Deserialize<XtrRuntimeManifest>(json, options);
        if (manifest == null)
            throw new InvalidOperationException("Failed to deserialize xtr-runtime.json.");

        return manifest;
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static bool VerifyRuntimeFilesIntegrity(string bundleDirectory, XtrRuntimeManifest manifest, out List<string> errors)
    {
        errors = [];

        foreach (var record in manifest.FileHashes)
        {
            if (record.Path.Contains("xtr_hud.asi", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Deprecated HUD plugin still pinned in manifest: {record.Path}");
            }

            string fullPath = Path.Combine(bundleDirectory, record.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                errors.Add($"Missing runtime file: {record.Path}");
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length != record.SizeBytes)
            {
                errors.Add($"Size mismatch for {record.Path}: expected {record.SizeBytes}, got {fileInfo.Length}");
            }

            string actualHash = ComputeSha256Hex(fullPath);
            if (!string.Equals(actualHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"SHA256 mismatch for {record.Path}: expected {record.Sha256}, got {actualHash}");
            }
        }

        return errors.Count == 0;
    }

    public static List<string> FindUntrackedRuntimeFiles(string bundleDirectory, XtrRuntimeManifest manifest)
    {
        var pinned = manifest.FileHashes
            .Select(h => h.Path.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var untracked = new List<string>();
        foreach (var file in Directory.EnumerateFiles(bundleDirectory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(bundleDirectory, file).Replace('\\', '/');
            if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (AllowedNonPinnedRelativeFiles.Contains(rel))
                continue;

            if (!pinned.Contains(rel))
                untracked.Add(rel);
        }

        return untracked;
    }
}
