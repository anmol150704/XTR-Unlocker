using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Persists the ownership record separately from the game. The installer must still
/// verify installed hashes before authorizing replacement or removal.
/// </summary>
internal static class RuntimeInstallManifestStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GameLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<RuntimeInstallManifest?> LoadAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        var path = GetManifestPath(gameId);
        if (!File.Exists(path))
        {
            return null;
        }

        var semaphore = GameLocks.GetOrAdd(gameId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var manifest = await JsonSerializer.DeserializeAsync<RuntimeInstallManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The runtime install manifest is empty or invalid.");

            ValidateManifest(manifest, gameId);
            return manifest;
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal static async Task SaveAsync(RuntimeInstallManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest, manifest.GameId);

        var semaphore = GameLocks.GetOrAdd(manifest.GameId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var path = GetManifestPath(manifest.GameId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";

            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDelete(temporaryPath);
            }

            semaphore.Release();
        }
    }

    internal static async Task DeleteAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        var semaphore = GameLocks.GetOrAdd(gameId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetManifestPath(gameId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string GetManifestPath(string gameId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameId)));
        return Path.Combine(Storage.GetRuntimeInstallManifestsPath(), $"{key}.json");
    }

    private static void ValidateManifest(RuntimeInstallManifest manifest, string expectedGameId)
    {
        ValidateGameId(expectedGameId);
        if (manifest.SchemaVersion != RuntimeInstallManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported runtime manifest schema version: {manifest.SchemaVersion}.");
        }

        if (!string.Equals(manifest.GameId, expectedGameId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The runtime manifest game ID does not match its storage key.");
        }

        if (string.IsNullOrWhiteSpace(manifest.RuntimeId) ||
            string.IsNullOrWhiteSpace(manifest.InstallRoot) ||
            !Path.IsPathFullyQualified(manifest.InstallRoot))
        {
            throw new InvalidDataException("The runtime manifest is missing its runtime ID or an absolute install root.");
        }

        if (manifest.Files is null)
        {
            throw new InvalidDataException("The runtime manifest file list is missing.");
        }

        if (manifest.ProxyFileName is not null &&
            (!new[] { "dxgi.dll", "winmm.dll", "d3d12.dll", "version.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll" }
                 .Contains(manifest.ProxyFileName, StringComparer.OrdinalIgnoreCase) ||
             !manifest.Files.Any(file => file is not null && string.Equals(file.RelativePath, manifest.ProxyFileName, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("The runtime manifest contains an invalid proxy filename.");
        }

        var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null ||
                !IsSafeRelativePath(file.RelativePath) ||
                !IsSha256(file.InstalledSha256) ||
                !ownedPaths.Add(file.RelativePath.Replace('\\', '/')))
            {
                throw new InvalidDataException("The runtime manifest contains an invalid owned-file entry.");
            }

            if (file.OriginalBackupPath is not null &&
                (!IsSafeRelativePath(file.OriginalBackupPath) || !IsSha256(file.OriginalSha256)))
            {
                throw new InvalidDataException("The runtime manifest contains an invalid original-file backup entry.");
            }

            if (file.OriginalBackupPath is null && file.OriginalSha256 is not null)
            {
                throw new InvalidDataException("The runtime manifest has an original hash without a backup path.");
            }
        }
    }

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
        return segments.Length > 0 && Array.TrueForAll(segments, segment =>
            segment != "." &&
            segment != ".." &&
            !segment.EndsWith(' ') &&
            !segment.EndsWith('.') &&
            segment.IndexOfAny(invalidFileNameCharacters) < 0);
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("A game ID is required.", nameof(gameId));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original operation error; orphaned temporary files are harmless.
        }
    }
}
