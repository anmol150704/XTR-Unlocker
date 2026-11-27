using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DLSS_Swapper.Data.Runtime;

internal static class DeployedDllMarkerStore
{
    private static readonly ConcurrentDictionary<string, object> GameLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class MarkerFilePayload
    {
        public int SchemaVersion { get; set; } = 1;
        public string GameId { get; set; } = string.Empty;
        public List<string> OriginallyAbsentPaths { get; set; } = new();
    }

    internal static void MarkOriginallyAbsent(string gameId, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        var normalizedPath = NormalizePath(fullPath);
        var gate = GameLocks.GetOrAdd(gameId, static _ => new object());
        lock (gate)
        {
            var payload = LoadPayloadUnsafe(gameId);
            if (!payload.OriginallyAbsentPaths.Any(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                payload.OriginallyAbsentPaths.Add(normalizedPath);
                SavePayloadUnsafe(gameId, payload);
            }
        }
    }

    internal static bool IsOriginallyAbsent(string gameId, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var normalizedPath = NormalizePath(fullPath);
        var gate = GameLocks.GetOrAdd(gameId, static _ => new object());
        lock (gate)
        {
            var payload = LoadPayloadUnsafe(gameId);
            return payload.OriginallyAbsentPaths.Any(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static void RemoveMarker(string gameId, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        var normalizedPath = NormalizePath(fullPath);
        var gate = GameLocks.GetOrAdd(gameId, static _ => new object());
        lock (gate)
        {
            var payload = LoadPayloadUnsafe(gameId);
            var removed = payload.OriginallyAbsentPaths.RemoveAll(p => string.Equals(p, normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                if (payload.OriginallyAbsentPaths.Count == 0)
                {
                    DeleteFileUnsafe(GetMarkerFilePath(gameId));
                }
                else
                {
                    SavePayloadUnsafe(gameId, payload);
                }
            }
        }
    }

    internal static void DeleteGameMarkers(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return;
        }

        var gate = GameLocks.GetOrAdd(gameId, static _ => new object());
        lock (gate)
        {
            DeleteFileUnsafe(GetMarkerFilePath(gameId));
        }
    }

    private static string GetMarkerFilePath(string gameId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameId)));
        return Path.Combine(Storage.GetStorageFolder(), "deployed_dlls", $"{key}.json");
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static MarkerFilePayload LoadPayloadUnsafe(string gameId)
    {
        var path = GetMarkerFilePath(gameId);
        if (!File.Exists(path))
        {
            return new MarkerFilePayload { GameId = gameId };
        }

        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<MarkerFilePayload>(json, JsonOptions);
            if (payload is null || !string.Equals(payload.GameId, gameId, StringComparison.Ordinal))
            {
                return new MarkerFilePayload { GameId = gameId };
            }

            payload.OriginallyAbsentPaths ??= new List<string>();
            return payload;
        }
        catch
        {
            return new MarkerFilePayload { GameId = gameId };
        }
    }

    private static void SavePayloadUnsafe(string gameId, MarkerFilePayload payload)
    {
        var path = GetMarkerFilePath(gameId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            DeleteFileUnsafe(tempPath);
        }
    }

    private static void DeleteFileUnsafe(string path)
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
            // Ignore cleanup errors.
        }
    }
}
