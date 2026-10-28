using System;
using System.Collections.Generic;
using System.IO;
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
    public const int ExpectedPinnedFileCount = 18;

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
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static bool VerifyRuntimeFilesIntegrity(string bundleDirectory, XtrRuntimeManifest manifest, out List<string> errors)
    {
        errors = [];
        foreach (var record in manifest.FileHashes)
        {
            string fullPath = Path.Combine(bundleDirectory, record.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                errors.Add($"Missing runtime file: {record.Path}");
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            bool isTextFile = record.Path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ||
                              record.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                              record.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

            if (fileInfo.Length != record.SizeBytes && (!isTextFile || Math.Abs(fileInfo.Length - record.SizeBytes) > 4))
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
}
