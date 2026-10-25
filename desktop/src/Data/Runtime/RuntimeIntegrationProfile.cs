using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Per-game configuration for a game-side runtime. Runtime options stay as JSON values
/// until a runtime-specific schema validates and applies them.
/// </summary>
public sealed class RuntimeIntegrationProfile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string GameId { get; set; } = string.Empty;
    public string RuntimeId { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string ProxyFileName { get; set; } = string.Empty;
    public string Api { get; set; } = string.Empty;
    public string SelectedUpscaler { get; set; } = "auto";
    public string FrameGenerationEnabled { get; set; } = "auto";
    public string FrameGenerationInput { get; set; } = "auto";
    public string FrameGenerationOutput { get; set; } = "auto";
    public string SupportState { get; set; } = "experimental";
    public string SupportReason { get; set; } = string.Empty;
    public JsonObject Options { get; set; } = new();
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Stores profiles outside game directories so a game update cannot remove them.
/// The file name is derived from the game ID; IDs are never used as paths directly.
/// </summary>
internal static class RuntimeIntegrationProfileStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GameLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<RuntimeIntegrationProfile?> LoadAsync(string gameId, CancellationToken cancellationToken = default)
    {
        ValidateGameId(gameId);
        var path = GetProfilePath(gameId);
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
            var profile = await JsonSerializer.DeserializeAsync<RuntimeIntegrationProfile>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The runtime profile is empty or invalid.");

            ValidateProfile(profile, gameId);

            return profile;
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal static async Task SaveAsync(RuntimeIntegrationProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateGameId(profile.GameId);

        ValidateProfile(profile, profile.GameId);

        var semaphore = GameLocks.GetOrAdd(profile.GameId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var path = GetProfilePath(profile.GameId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            profile.UpdatedUtc = DateTimeOffset.UtcNow;
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken).ConfigureAwait(false);
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
            var path = GetProfilePath(gameId);
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

    private static string GetProfilePath(string gameId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameId)));
        return Path.Combine(Storage.GetRuntimeProfilesPath(), $"{key}.json");
    }

    private static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new ArgumentException("A game ID is required.", nameof(gameId));
        }
    }

    private static void ValidateProfile(RuntimeIntegrationProfile profile, string expectedGameId)
    {
        if (profile.SchemaVersion != RuntimeIntegrationProfile.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported runtime profile schema version: {profile.SchemaVersion}.");
        }

        if (!string.Equals(profile.GameId, expectedGameId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The runtime profile game ID does not match its storage key.");
        }

        if (string.IsNullOrWhiteSpace(profile.RuntimeId))
        {
            throw new InvalidDataException("A runtime ID is required.");
        }

        if (profile.Options is null)
        {
            throw new InvalidDataException("The runtime profile options object is missing.");
        }

        if (profile.SupportState is not ("supported" or "experimental" or "unavailable"))
        {
            throw new InvalidDataException("The runtime profile support state is invalid.");
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
