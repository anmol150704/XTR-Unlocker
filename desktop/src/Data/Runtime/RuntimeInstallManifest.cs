using System;
using System.Collections.Generic;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Records the files installed for one game's runtime profile. A future installer must
/// verify each installed hash before replacing or removing a file.
/// </summary>
public sealed class RuntimeInstallManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string GameId { get; set; } = string.Empty;
    public string RuntimeId { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string InstallRoot { get; set; } = string.Empty;
    public DateTimeOffset InstalledUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<RuntimeOwnedFile> Files { get; set; } = new();
}

public sealed class RuntimeOwnedFile
{
    /// <summary>Relative path beneath InstallRoot; absolute paths are not permitted.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact runtime file installed by the app.</summary>
    public string InstalledSha256 { get; set; } = string.Empty;

    /// <summary>Relative path in the app-managed backup store, when the game had a file here.</summary>
    public string? OriginalBackupPath { get; set; }

    /// <summary>SHA-256 of the original file, when one existed.</summary>
    public string? OriginalSha256 { get; set; }
}
