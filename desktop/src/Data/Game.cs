using CommunityToolkit.Mvvm.ComponentModel;
using DLSS_Swapper.Extensions;
using DLSS_Swapper.Helpers;
using DLSS_Swapper.Interfaces;
using DLSS_Swapper.Data.Runtime;
using DLSS_Swapper.UserControls;
using Microsoft.UI.Xaml.Controls;
using SQLite;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data;

public abstract partial class Game : ObservableObject, IComparable<Game>, IEquatable<Game> //, INotifyPropertyChanged
{
    [PrimaryKey]
    [Column("id")]
    public string ID { get; set; } = string.Empty;

    [Column("platform_id")]
    public string PlatformId { get; set; } = string.Empty;

    [ObservableProperty]
    [Column("title")]
    public partial string Title { get; set; } = string.Empty;

    // Used to cache the title as a base64 string
    string? _titleBase64;
    [Ignore]
    public string TitleBase64 => _titleBase64 ??= Convert.ToBase64String(Encoding.UTF8.GetBytes(Title));

    [Column("install_path")]
    public string InstallPath { get; set; } = string.Empty;

    [ObservableProperty]
    [Column("cover_image")]
    public partial string? CoverImage { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial uint? DlssPreset { get; set; }

    [ObservableProperty]
    [Ignore]
    public partial uint? DlssDPreset { get; set; }


    [ObservableProperty]
    [Ignore]
    public partial uint? DlssGPreset { get; set; }

    [Ignore]
    public NativeDrsProfile? DriverSettingsProfile { get; set; }

    /*
    [ObservableProperty]
    [property: Column("base_dlss_version")]
    string baseDLSSVersion = string.Empty;

    [ObservableProperty]
    [property: Column("current_dlss_version")]
    string currentDLSSVersion = string.Empty;

    [ObservableProperty]
    [property: Column("current_dlss_hash")]
    string currentDLSSHash = string.Empty;

    [ObservableProperty]
    [property: Column("base_dlss_hash")]
    string baseDLSSHash = string.Empty;

    [ObservableProperty]
    [property: Column("has_dlss")]
    bool hasDLSS = false;
    */

    [ObservableProperty]
    [Column("has_swappable_items")]
    public partial bool HasSwappableItems { get; set; } = false;

    [ObservableProperty]
    [Column("notes")]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    [Column("is_favourite")]
    public partial bool IsFavourite { get; set; } = false;

    /// <summary>
    /// If the game is hidden from the main list or not. All hidden games are still processed.
    /// If the value is null the user has not set the value and this should be considered as not hidden.
    /// </summary>
    [ObservableProperty]
    [Column("is_hidden")]
    public partial bool? IsHidden { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool Processing { get; set; } = false;

    [Ignore]
    public abstract GameLibrary GameLibrary { get; }

    [Ignore]
    //public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_600_900.jpg");
    //public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_600_900.png");
    public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_400_600.png");
    //public string ExpectedCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_600_900.webp");

    [Ignore]
    //public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_600_900.jpg");
    //public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_600_900.png");
    public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_400_600.png");
    //public string ExpectedCustomCoverImage => Path.Combine(Storage.GetImageCachePath(), $"{ID}_custom_600_900.webp");

    [Ignore]
    public List<GameAsset> GameAssets { get; } = new List<GameAsset>();

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(IntegrationStatus))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasFrameGenBadge))]
    [NotifyPropertyChangedFor(nameof(HasActiveRuntimeFg))]
    [NotifyPropertyChangedFor(nameof(HasNativeDlssGOnly))]
    [NotifyPropertyChangedFor(nameof(IsTurboBoostActive))]
    [NotifyPropertyChangedFor(nameof(HasRuntimeBoostOrFg))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeBorder))]
    [NotifyPropertyChangedFor(nameof(FrameGenDotColor))]
    public partial RuntimeIntegrationProfile? RuntimeProfile { get; set; }

    partial void OnRuntimeProfileChanged(RuntimeIntegrationProfile? value)
    {
        RefreshRuntimeHealth();
    }

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(IntegrationStatus))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasFrameGenBadge))]
    [NotifyPropertyChangedFor(nameof(HasActiveRuntimeFg))]
    [NotifyPropertyChangedFor(nameof(HasNativeDlssGOnly))]
    [NotifyPropertyChangedFor(nameof(IsTurboBoostActive))]
    [NotifyPropertyChangedFor(nameof(HasRuntimeBoostOrFg))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeBorder))]
    [NotifyPropertyChangedFor(nameof(FrameGenDotColor))]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    public partial RuntimeInstallManifest? RuntimeManifest { get; set; }

    partial void OnRuntimeManifestChanged(RuntimeInstallManifest? value)
    {
        RefreshRuntimeHealth();
    }

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(IntegrationStatus))]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    public partial bool HasStreamlineStack { get; set; }

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(IntegrationStatus))]
    [NotifyPropertyChangedFor(nameof(DlssGenerationText))]
    [NotifyPropertyChangedFor(nameof(DlssVersionLabel))]
    [NotifyPropertyChangedFor(nameof(DlssBadgeText))]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    [NotifyPropertyChangedFor(nameof(HasAnyDlss))]
    public partial bool HasStreamlineDlss { get; set; }

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    [NotifyPropertyChangedFor(nameof(HasFrameGenBadge))]
    [NotifyPropertyChangedFor(nameof(HasNativeDlssGOnly))]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeText))]
    public partial bool HasStreamlineDlssG { get; set; }

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    public partial bool HasStreamlineDlssD { get; set; }

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(DlssGenerationText))]
    [NotifyPropertyChangedFor(nameof(DlssVersionLabel))]
    [NotifyPropertyChangedFor(nameof(DlssBadgeText))]
    public partial string? StreamlineDlssVersion { get; set; }

    [ObservableProperty]
    [Ignore]
    public partial string? StreamlineDlssGVersion { get; set; }

    [ObservableProperty]
    [Ignore]
    public partial string? StreamlineDlssDVersion { get; set; }

    [ObservableProperty]
    [Ignore]
    public partial string? StreamlineSdkVersion { get; set; }

    [ObservableProperty]
    [Ignore]
    public partial bool HasFidelityFxSdk2 { get; set; }

    private RuntimeHealthReport? _cachedRuntimeHealth;

    internal RuntimeHealthReport? GetVerifiedRuntimeHealth()
    {
        if (RuntimeManifest is null || string.IsNullOrWhiteSpace(RuntimeManifest.InstallRoot))
        {
            _cachedRuntimeHealth = null;
            return null;
        }

        var proxyName = string.IsNullOrWhiteSpace(RuntimeManifest.ProxyFileName)
            ? "dxgi.dll"
            : RuntimeManifest.ProxyFileName;

        _cachedRuntimeHealth = RuntimeHealthService.CheckHealth(RuntimeManifest.InstallRoot, proxyName);
        return _cachedRuntimeHealth;
    }

    internal void RefreshRuntimeHealth()
    {
        _ = GetVerifiedRuntimeHealth();
        NotifyRuntimeHealthChanged();
    }

    internal void NotifyRuntimeHealthChanged()
    {
        OnPropertyChanged(nameof(IntegrationStatus));
        OnPropertyChanged(nameof(IsTurboBoostActive));
        OnPropertyChanged(nameof(HasActiveRuntimeFg));
        OnPropertyChanged(nameof(HasRuntimeBoostOrFg));
        OnPropertyChanged(nameof(HasFrameGenBadge));
        OnPropertyChanged(nameof(HasNativeDlssGOnly));
        OnPropertyChanged(nameof(FrameGenBadgeText));
        OnPropertyChanged(nameof(FrameGenBadgeForeground));
        OnPropertyChanged(nameof(FrameGenBadgeBackground));
        OnPropertyChanged(nameof(FrameGenBadgeBorder));
        OnPropertyChanged(nameof(FrameGenDotColor));
    }

    private bool HasOperationalRuntime()
    {
        if (RuntimeManifest is null)
            return false;

        var health = _cachedRuntimeHealth ?? GetVerifiedRuntimeHealth();
        return health?.IsRuntimeOperational == true;
    }

    [Ignore]
    public bool HasAnyDlss => CurrentDLSS is not null || HasStreamlineDlss;

    [Ignore]
    public string IntegrationStatus
    {
        get
        {
            if (RuntimeManifest is not null)
            {
                if (!HasOperationalRuntime())
                {
                    return "Runtime incomplete - Restore recommended";
                }

                return HasAnyDlss
                    ? (CurrentDLSS is null && HasStreamlineDlss ? "Runtime ready - Streamline DLSS active" : "Runtime ready - DLSS detected")
                    : "Runtime installed - DLSS not detected";
            }

            if (CurrentDLSS is not null)
            {
                return "DLSS detected - runtime not installed";
            }

            if (HasStreamlineDlss)
            {
                return "Streamline DLSS (NGX OTA) - runtime not installed";
            }

            return "No runtime configured";
        }
    }

    [Ignore]
    public string DlssGenerationText
    {
        get
        {
            if (CurrentDLSS is not null)
            {
                return DlssVersionHelper.FormatGeneration(CurrentDLSS.DisplayVersion);
            }

            if (HasStreamlineDlss)
            {
                return string.IsNullOrWhiteSpace(StreamlineDlssVersion)
                    ? "Streamline DLSS"
                    : DlssVersionHelper.FormatGeneration(StreamlineDlssVersion);
            }

            return DlssVersionHelper.FormatGeneration(null);
        }
    }

    [Ignore]
    public string DlssVersionLabel
    {
        get
        {
            if (CurrentDLSS is not null)
            {
                return DlssVersionHelper.FormatVersionLabel(CurrentDLSS.DisplayVersion);
            }

            if (HasStreamlineDlss)
            {
                return string.IsNullOrWhiteSpace(StreamlineDlssVersion)
                    ? "Streamline DLSS (NGX OTA)"
                    : $"{DlssVersionHelper.FormatVersionLabel(StreamlineDlssVersion)} (OTA)";
            }

            return DlssVersionHelper.FormatVersionLabel(null);
        }
    }

    [Ignore]
    public string DlssBadgeText
    {
        get
        {
            if (CurrentDLSS is not null)
            {
                return DlssVersionHelper.FormatBadge(CurrentDLSS.DisplayVersion);
            }

            if (HasStreamlineDlss)
            {
                return string.IsNullOrWhiteSpace(StreamlineDlssVersion)
                    ? "SL DLSS (OTA)"
                    : $"{DlssVersionHelper.FormatBadge(StreamlineDlssVersion)} (OTA)";
            }

            return DlssVersionHelper.FormatBadge(null);
        }
    }

    [Ignore]
    public bool IsTurboBoostActive
    {
        get
        {
            if (!HasOperationalRuntime())
                return false;

            if (RuntimeProfile?.Options?.TryGetPropertyValue("Preset", out var node) == true &&
                string.Equals(node?.ToString(), "TurboBoost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }

    [Ignore]
    public bool HasActiveRuntimeFg =>
        HasOperationalRuntime() &&
        (RuntimeProfile is null || !string.Equals(RuntimeProfile.FrameGenerationEnabled, "false", StringComparison.OrdinalIgnoreCase));

    [Ignore]
    public bool HasRuntimeBoostOrFg => DlssVersionHelper.ComputeHasRuntimeBoostOrFg(HasOperationalRuntime(), IsTurboBoostActive, HasActiveRuntimeFg);

    [Ignore]
    public bool HasFrameGenBadge => HasRuntimeBoostOrFg || CurrentDLSS_G is not null || HasStreamlineDlssG;

    [Ignore]
    public bool HasNativeDlssGOnly => DlssVersionHelper.ComputeHasNativeDlssGOnly(HasRuntimeBoostOrFg, CurrentDLSS_G is not null || HasStreamlineDlssG);

    [Ignore]
    public string FrameGenBadgeText => DlssVersionHelper.FormatFrameGenBadge(
        HasOperationalRuntime(),
        IsTurboBoostActive,
        HasActiveRuntimeFg,
        CurrentDLSS_G is not null || HasStreamlineDlssG);

    [Ignore]
    public string FrameGenBadgeForeground => HasRuntimeBoostOrFg ? "#E6FCEE" : "#E0F2FE";

    [Ignore]
    public string FrameGenBadgeBackground => HasRuntimeBoostOrFg ? "#DD153720" : "#DD0F2942";

    [Ignore]
    public string FrameGenBadgeBorder => HasRuntimeBoostOrFg ? "#6034C759" : "#6038BDF8";

    [Ignore]
    public string FrameGenDotColor => HasRuntimeBoostOrFg ? "#34C759" : "#38BDF8";

    [Ignore]
    public string DlssArchitectureSummary
    {
        get
        {
            if (CurrentDLSS_G is not null && CurrentDLSS_D is not null)
                return "DLSS 3.5 (Super Resolution + Frame Generation + Ray Reconstruction)";
            if (CurrentDLSS_G is not null)
                return "DLSS 3 (Super Resolution + Frame Generation)";
            if (CurrentDLSS_D is not null)
                return "DLSS 3.5 (Super Resolution + Ray Reconstruction)";
            if (CurrentDLSS is not null)
                return $"{DlssGenerationText} (Super Resolution)";
            if (HasStreamlineDlss)
            {
                var features = new List<string> { "Super Resolution" };
                if (HasStreamlineDlssG) features.Add("Frame Generation");
                if (HasStreamlineDlssD) features.Add("Ray Reconstruction");
                return $"Streamline DirectSR / NGX OTA ({string.Join(" + ", features)})";
            }
            return "No DLSS Detected";
        }
    }

    private readonly object _runtimeStateLoadLock = new();
    private Task? _runtimeStateLoadTask;

    internal Task LoadRuntimeStateAsync()
    {
        lock (_runtimeStateLoadLock)
        {
            return _runtimeStateLoadTask ??= LoadRuntimeStateCoreAsync();
        }
    }

    private async Task LoadRuntimeStateCoreAsync()
    {
        try
        {
            var profile = await RuntimeIntegrationProfileStore.LoadAsync(ID).ConfigureAwait(false);
            var manifest = await RuntimeInstallManifestStore.LoadAsync(ID).ConfigureAwait(false);
            await App.CurrentApp.RunOnUIThreadAsync(() =>
            {
                RuntimeProfile = profile;
                RuntimeManifest = manifest;
                RefreshRuntimeHealth();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Logger.Error(error);
        }
    }

    internal async Task SaveRuntimeProfileAsync(RuntimeIntegrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!string.Equals(profile.GameId, ID, StringComparison.Ordinal))
        {
            throw new ArgumentException("The runtime profile belongs to a different game.", nameof(profile));
        }

        await RuntimeIntegrationProfileStore.SaveAsync(profile).ConfigureAwait(false);
        App.CurrentApp.RunOnUIThread(() =>
        {
            RuntimeProfile = profile;
            RefreshRuntimeHealth();
        });
    }

    internal async Task SaveRuntimeManifestAsync(RuntimeInstallManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.GameId, ID, StringComparison.Ordinal))
        {
            throw new ArgumentException("The runtime manifest belongs to a different game.", nameof(manifest));
        }

        await RuntimeInstallManifestStore.SaveAsync(manifest).ConfigureAwait(false);
        App.CurrentApp.RunOnUIThread(() =>
        {
            RuntimeManifest = manifest;
            RefreshRuntimeHealth();
        });
    }

    internal async Task RemoveRuntimeProfileAsync()
    {
        await RuntimeIntegrationProfileStore.DeleteAsync(ID).ConfigureAwait(false);
        App.CurrentApp.RunOnUIThread(() =>
        {
            RuntimeProfile = null;
            RefreshRuntimeHealth();
        });
    }

    [Ignore]
    public bool NeedsProcessing { get; set; } = false;

    bool _isLoadingCoverImage;

    // NOTE: DLL type
    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(IntegrationStatus))]
    [NotifyPropertyChangedFor(nameof(DlssGenerationText))]
    [NotifyPropertyChangedFor(nameof(DlssVersionLabel))]
    [NotifyPropertyChangedFor(nameof(DlssBadgeText))]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    [NotifyPropertyChangedFor(nameof(HasAnyDlss))]
    public partial GameAsset? CurrentDLSS { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleDLSSFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(FrameGenBadgeText))]
    [NotifyPropertyChangedFor(nameof(HasFrameGenBadge))]
    [NotifyPropertyChangedFor(nameof(HasNativeDlssGOnly))]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    public partial GameAsset? CurrentDLSS_G { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleDLSSGFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    [NotifyPropertyChangedFor(nameof(DlssArchitectureSummary))]
    public partial GameAsset? CurrentDLSS_D { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleDLSSDFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentFSR_31_DX12 { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleFSR31DX12Found { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentFSR_31_VK { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleFSR31VKFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeSS { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeSSFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeLL { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeLLFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeSS_FG { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeSSFGFound { get; set; } = false;

    [ObservableProperty]
    [Ignore]
    public partial GameAsset? CurrentXeSS_DX11 { get; set; } = null;

    [ObservableProperty]
    [Ignore]
    public partial bool MultipleXeSSDX11Found { get; set; } = false;
    

    [Ignore]
    public abstract bool IsReadyToPlay { get; }

    protected void SetID()
    {
        // Seeing as we use ID, it sure would be a shame if a PlatformId was set to "C:\Program Files\"
        // So try to remove all funky characters before

        var platformId = PlatformId;
        foreach (var invalidPathChar in PathHelpers.InvalidFileNamePathChars)
        {
            if (platformId.Contains(invalidPathChar))
            {
                platformId = platformId.Replace(invalidPathChar, '_');
            }
        }

        ID = GameLibrary switch
        {
            GameLibrary.Steam => $"steam_{platformId}",
            GameLibrary.GOG => $"gog_{platformId}",
            GameLibrary.EpicGamesStore => $"epicgamesstore_{platformId}",
            GameLibrary.UbisoftConnect => $"ubisoftconnect_{platformId}",
            GameLibrary.XboxApp => $"xboxapp_{platformId}",
            GameLibrary.ManuallyAdded => $"manuallyadded_{platformId}",
            GameLibrary.BattleNet => $"battlenet_{platformId}",
            GameLibrary.EAApp => $"eaapp_{platformId}",
            _ => throw new Exception($"Unknown GameLibrary {GameLibrary} while setting ID"),
        };
    }

    // Bounds how many games can be scanned for DLLs/covers at once. Every game with no previously known
    // DLLs is re-queued for processing on every launch, so without a limit a large library fires off
    // hundreds of concurrent recursive directory scans and UI-thread updates at startup.
    static readonly SemaphoreSlim processGameSemaphore = new SemaphoreSlim(4);

    /// <summary>
    /// Detects DLSS and updates cover image.
    /// </summary>
    public void ProcessGame(bool autoSave = true, bool forceNeedsProcessing = false)
    {
        // If we are alreayd procssing we don't need to process again
        if (Processing == true)
        {
            return;
        }

        App.CurrentApp.RunOnUIThread(() =>
        {
            NeedsProcessing = false;
        });

        if (string.IsNullOrEmpty(InstallPath))
        {
            return;
        }

        if (Directory.Exists(InstallPath) == false)
        {
            return;
        }

        App.CurrentApp.RunOnUIThread(() =>
        {
            Processing = true;
            HasSwappableItems = false;
        });

        ThreadPool.QueueUserWorkItem(async (stateInfo) =>
        {
            await processGameSemaphore.WaitAsync().ConfigureAwait(false);

            var newHasSwappableItems = false;

            try
            {
                var shouldUpdatedCover = true;

                if (forceNeedsProcessing == true && File.Exists(ExpectedCustomCoverImage) == false)
                {
                    // If we are forcing game load and custom cover image doesnt exist we will force load the cover no matter what.
                }
                else
                {
                    // This shouldn't crash, bit if it does lets not take down the entire processing.
                    try
                    {
                        FileInfo? fileInfo = null;
                        if (File.Exists(ExpectedCustomCoverImage))
                        {
                            // If we are using a custom cover we don't want to try reloading any cover so we don't set fileInfo.
                            shouldUpdatedCover = false;
                        }
                        else if (File.Exists(ExpectedCoverImage))
                        {
                            fileInfo = new FileInfo(ExpectedCoverImage);
                        }

                        if (fileInfo is not null)
                        {
                            var daysSinceLastModified = (DateTime.Now - fileInfo.LastWriteTime).TotalDays;

                            // Add +/- 2 days so not all will process at the same time.
                            daysSinceLastModified += ((new Random()).NextDouble() - 0.5) * 4.0;

                            // If its less than 7 days lets not try refresh.
                            if (daysSinceLastModified < 7)
                            {
                                shouldUpdatedCover = false;
                            }
                        }
                    }
                    catch (Exception err)
                    {
                        Logger.Error(err);
                        Debugger.Break();
                    }
                }

                Task? coverImageTask = null;
                if (shouldUpdatedCover)
                {
                    coverImageTask = UpdateCacheImageAsync();
                }
                else
                {
                    Logger.Verbose($"Skipping updating cover for {Title}");
                }

                var oldGameAssets = GameAssets.ToList();
                GameAssets.Clear();
                using (await Database.Instance.Mutex.LockAsync())
                {
                    await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
                }

                var discovery = UpscalerDiscoveryService.Discover(InstallPath);

                var dllHistory = new List<GameHistory>();
                var unknownGameAssets = new List<GameAsset>();

                void ProcessGame_ProcessGameAsset(GameAsset gameAsset)
                {
                    gameAsset.LoadVersionAndHash();

                    if (!IsSwappableAssetType(gameAsset.AssetType))
                    {
                        return;
                    }

                    var oldGameAsset = oldGameAssets.FirstOrDefault(x => x.Path.Equals(gameAsset.Path, StringComparison.OrdinalIgnoreCase));

                    if (oldGameAsset is not null) // DLL existed previously
                    {
                        if (gameAsset.Version == oldGameAsset.Version)
                        {
                            // NOOP
                        }
                        else
                        {
                            dllHistory.Add(new GameHistory()
                            {
                                GameId = ID,
                                EventType = GameHistoryEventType.DLLChangedExternally,
                                EventTime = DateTime.Now,
                                AssetType = gameAsset.AssetType,
                                AssetPath = gameAsset.Path,
                                AssetVersion = gameAsset.DisplayName,
                            });

                            // If the DLL was changed externally (eg. game update) we delete the backup.
                            // This fixes the issue where looking at your game it may appear to be downgraded but
                            // in reality it is because the game updated to a newer version than you had swapped to.
                            var expectedBackupPath = $"{gameAsset.Path}.dlsss";
                            if (File.Exists(expectedBackupPath))
                            {
                                var tempBackupGameAsset = new GameAsset()
                                {
                                    Id = ID,
                                    AssetType = DLLManager.Instance.GetAssetBackupType(gameAsset.AssetType),
                                    Path = expectedBackupPath,
                                };
                                tempBackupGameAsset.LoadVersionAndHash();

                                dllHistory.Add(new GameHistory()
                                {
                                    GameId = ID,
                                    EventType = GameHistoryEventType.DLLBackupRemoved,
                                    EventTime = DateTime.Now,
                                    AssetType = tempBackupGameAsset.AssetType,
                                    AssetPath = tempBackupGameAsset.Path,
                                    AssetVersion = tempBackupGameAsset.DisplayName,
                                });

                                File.Delete(expectedBackupPath);
                            }
                        }
                    }
                    else // DLL is new
                    {
                        dllHistory.Add(new GameHistory()
                        {
                            GameId = ID,
                            EventType = GameHistoryEventType.DLLDetected,
                            EventTime = DateTime.Now,
                            AssetType = gameAsset.AssetType,
                            AssetPath = gameAsset.Path,
                            AssetVersion = gameAsset.DisplayName,
                        });
                    }

                    if (DLLManager.Instance.IsInKnownGameAsset(gameAsset, this) == false)
                    {
                        unknownGameAssets.Add(gameAsset);
                    }

                    LoadBackupForGameAsset(gameAsset);
                }

                foreach (var discoveredFile in discovery.Files)
                {
                    var assetType = MapKindToGameAssetType(discoveredFile.Kind);
                    if (assetType == GameAssetType.Unknown)
                    {
                        continue;
                    }

                    var gameAsset = new GameAsset()
                    {
                        Id = ID,
                        AssetType = assetType,
                        Path = discoveredFile.FullPath,
                    };
                    ProcessGame_ProcessGameAsset(gameAsset);
                    GameAssets.Add(gameAsset);
                }

                App.CurrentApp.RunOnUIThread(() =>
                {
                    UpdateCurrentDLLsFromGameAssets(discovery);
                    RefreshRuntimeHealth();
                });

                if (GameAssets.Any())
                {
                    newHasSwappableItems = GameAssets.Any(x => IsSwappableAssetType(x.AssetType)) || discovery.HasAnyDlssCapability;

                    //App.CurrentApp.Database.ExecuteAsync
                    //savePoint is not valid, and should be the result of a call to SaveTransactionPoint.
                    using (await Database.Instance.Mutex.LockAsync())
                    {
                        if (dllHistory.Count > 0)
                        {
                            await Database.Instance.Connection.InsertAllAsync(dllHistory, false).ConfigureAwait(false);
                        }
                        await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
                    }

                    if (unknownGameAssets.Any())
                    {
                        GameManager.Instance.AddUnknownGameAssets(GameLibrary, Title, unknownGameAssets);
                    }
                }
                else if (discovery.HasAnyDlssCapability)
                {
                    newHasSwappableItems = true;
                }

                if (coverImageTask is not null)
                {
                    await coverImageTask;
                }
            }
            catch (Exception err)
            {
                Logger.Error(err);
                Debugger.Break();
            }
            finally
            {
                processGameSemaphore.Release();

                // Now update all the data on the UI therad.
                await App.CurrentApp.RunOnUIThreadAsync(async () =>
                {
                    HasSwappableItems = newHasSwappableItems;

                    if (autoSave)
                    {
                        await SaveToDatabaseAsync();
                    }

                    Processing = false;
                });
            }
        });
    }

    void LoadBackupForGameAsset(GameAsset gameAsset)
    {
        var backupPath = $"{gameAsset.Path}.dlsss";
        if (File.Exists(backupPath))
        {
            var gameAssetBackup = new GameAsset()
            {
                Id = ID,
                AssetType = DLLManager.Instance.GetAssetBackupType(gameAsset.AssetType),
                Path = backupPath,
            };
            gameAssetBackup.LoadVersionAndHash();
            GameAssets.Add(gameAssetBackup);
        }
    }


    public async Task LoadCoverImageAsync()
    {
        if (_isLoadingCoverImage == true)
        {
            return;
        }

        _isLoadingCoverImage = true;

        // TODO: Update if the image last write is > 1 week old or something

        if (File.Exists(ExpectedCustomCoverImage))
        {
            // If a custom cover exists use it.
            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = ExpectedCustomCoverImage;
            });
        }
        else if (File.Exists(ExpectedCoverImage))
        {
            // If a standard cover exists use it.
            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = ExpectedCoverImage;
            });
        }
        else
        {
            // If no cover exists use the abstracted method to get the game as expect for this library.
            await UpdateCacheImageAsync();
        }

        _isLoadingCoverImage = false;
    }

    protected abstract Task UpdateCacheImageAsync();

    internal static bool IsSwappableAssetType(GameAssetType assetType) => assetType switch
    {
        GameAssetType.DLSS => true,
        GameAssetType.DLSS_G => true,
        GameAssetType.DLSS_D => true,
        GameAssetType.FSR_31_DX12 => true,
        GameAssetType.FSR_31_VK => true,
        GameAssetType.XeSS => true,
        GameAssetType.XeSS_FG => true,
        GameAssetType.XeSS_DX11 => true,
        GameAssetType.XeLL => true,
        _ => false
    };

    private static bool IsStreamlineAssetType(GameAssetType assetType) => assetType is
        GameAssetType.Streamline_Interposer
        or GameAssetType.Streamline_DirectSR
        or GameAssetType.Streamline_DLSS
        or GameAssetType.Streamline_DLSS_G
        or GameAssetType.Streamline_DLSS_D
        or GameAssetType.Streamline_Common
        or GameAssetType.Streamline_Reflex
        or GameAssetType.Streamline_PCL
        or GameAssetType.Streamline_NIS
        or GameAssetType.Streamline_DeepDVC
        or GameAssetType.Streamline_NvPerf;

    internal static bool TryMapAssetTypeToSwapSlot(GameAssetType assetType, out UpscalerSwapSlot slot)
    {
        switch (assetType)
        {
            case GameAssetType.DLSS: slot = UpscalerSwapSlot.Dlss; return true;
            case GameAssetType.DLSS_G: slot = UpscalerSwapSlot.DlssG; return true;
            case GameAssetType.DLSS_D: slot = UpscalerSwapSlot.DlssD; return true;
            case GameAssetType.FSR_31_DX12: slot = UpscalerSwapSlot.Fsr31Dx12; return true;
            case GameAssetType.FSR_31_VK: slot = UpscalerSwapSlot.Fsr31Vk; return true;
            case GameAssetType.XeSS: slot = UpscalerSwapSlot.XeSs; return true;
            case GameAssetType.XeSS_FG: slot = UpscalerSwapSlot.XeSsFg; return true;
            case GameAssetType.XeLL: slot = UpscalerSwapSlot.XeLl; return true;
            case GameAssetType.XeSS_DX11: slot = UpscalerSwapSlot.XeSsDx11; return true;
            default: slot = default; return false;
        }
    }

    private static GameAssetType MapKindToGameAssetType(UpscalerAssetKind kind) => kind switch
    {
        UpscalerAssetKind.NgxDlss => GameAssetType.DLSS,
        UpscalerAssetKind.NgxDlssG => GameAssetType.DLSS_G,
        UpscalerAssetKind.NgxDlssD => GameAssetType.DLSS_D,
        UpscalerAssetKind.NgxDlssNr => GameAssetType.DLSS_NR,
        UpscalerAssetKind.StreamlineInterposer => GameAssetType.Streamline_Interposer,
        UpscalerAssetKind.StreamlineDirectSR => GameAssetType.Streamline_DirectSR,
        UpscalerAssetKind.StreamlineDlss => GameAssetType.Streamline_DLSS,
        UpscalerAssetKind.StreamlineDlssG => GameAssetType.Streamline_DLSS_G,
        UpscalerAssetKind.StreamlineDlssD => GameAssetType.Streamline_DLSS_D,
        UpscalerAssetKind.StreamlineCommon => GameAssetType.Streamline_Common,
        UpscalerAssetKind.StreamlineReflex => GameAssetType.Streamline_Reflex,
        UpscalerAssetKind.StreamlinePcl => GameAssetType.Streamline_PCL,
        UpscalerAssetKind.StreamlineNis => GameAssetType.Streamline_NIS,
        UpscalerAssetKind.StreamlineDeepDvc => GameAssetType.Streamline_DeepDVC,
        UpscalerAssetKind.StreamlineNvPerf => GameAssetType.Streamline_NvPerf,
        UpscalerAssetKind.Fsr31Dx12 => GameAssetType.FSR_31_DX12,
        UpscalerAssetKind.Fsr31Vk => GameAssetType.FSR_31_VK,
        UpscalerAssetKind.FidelityFxSdk2LoaderDx12 => GameAssetType.FidelityFX_SDK2_Loader_DX12,
        UpscalerAssetKind.FidelityFxSdk2UpscalerDx12 => GameAssetType.FidelityFX_SDK2_Upscaler_DX12,
        UpscalerAssetKind.FidelityFxSdk2FrameGenerationDx12 => GameAssetType.FidelityFX_SDK2_FrameGeneration_DX12,
        UpscalerAssetKind.FidelityFxSdk2DenoiserDx12 => GameAssetType.FidelityFX_SDK2_Denoiser_DX12,
        UpscalerAssetKind.FidelityFxSdk2RadianceCacheDx12 => GameAssetType.FidelityFX_SDK2_RadianceCache_DX12,
        UpscalerAssetKind.FidelityFxSdk2UpscalerVk => GameAssetType.FidelityFX_SDK2_Upscaler_VK,
        UpscalerAssetKind.FidelityFxSdk2FrameGenerationVk => GameAssetType.FidelityFX_SDK2_FrameGeneration_VK,
        UpscalerAssetKind.FidelityFxSdk2LoaderVk => GameAssetType.FidelityFX_SDK2_Loader_VK,
        UpscalerAssetKind.FidelityFxSdk2Other => GameAssetType.FidelityFX_SDK2_Other,
        UpscalerAssetKind.XeSS => GameAssetType.XeSS,
        UpscalerAssetKind.XeSSDx11 => GameAssetType.XeSS_DX11,
        UpscalerAssetKind.XeSSFg => GameAssetType.XeSS_FG,
        UpscalerAssetKind.XeLL => GameAssetType.XeLL,
        _ => GameAssetType.Unknown
    };

    internal async Task<(bool Success, string Message, bool PromptToRelaunchAsAdmin)> ResetDllAsync(GameAssetType gameAssetType)
    {
        var backupRecordType = DLLManager.Instance.GetAssetBackupType(gameAssetType);
        var existingBackupRecords = this.GameAssets.Where(x => x.AssetType == backupRecordType).ToList();

        if (existingBackupRecords.Count == 0)
        {
            var existingRecords = this.GameAssets.Where(x => x.AssetType == gameAssetType).ToList();
            var originallyAbsentRecords = existingRecords
                .Where(x => DeployedDllMarkerStore.IsOriginallyAbsent(ID, x.Path))
                .ToList();

            if (originallyAbsentRecords.Count == 0)
            {
                Logger.Info("No backup records found.");
                return (false, "Unable to reset to default. Please repair your game manually.", false);
            }

            var resetHistory = new List<GameHistory>();
            foreach (var deployedRecord in originallyAbsentRecords)
            {
                try
                {
                    if (File.Exists(deployedRecord.Path))
                    {
                        File.Delete(deployedRecord.Path);
                    }
                }
                catch (UnauthorizedAccessException err)
                {
                    Logger.Error(err);
                    if (App.CurrentApp.IsAdminUser() is false)
                    {
                        return (false, "Unable to reset to default. Running XTR Unlocker as administrator may fix this.", true);
                    }
                    else
                    {
                        return (false, "Unable to reset to default. Please repair your game manually.", false);
                    }
                }
                catch (Exception err)
                {
                    Logger.Error(err);
                    return (false, "Unable to reset to default. Please repair your game manually.", false);
                }

                DeployedDllMarkerStore.RemoveMarker(ID, deployedRecord.Path);
                GameAssets.Remove(deployedRecord);

                resetHistory.Add(new GameHistory()
                {
                    GameId = ID,
                    EventType = GameHistoryEventType.DLLReset,
                    EventTime = DateTime.Now,
                    AssetType = gameAssetType,
                    AssetPath = deployedRecord.Path,
                    AssetVersion = "Removed deployed DLL",
                });
            }

            var preinspectedNgxOta = GameAssets.Any(x => IsStreamlineAssetType(x.AssetType))
                ? UpscalerDiscoveryService.InspectNgxOtaStore()
                : null;

            await App.CurrentApp.RunOnUIThreadAsync(() =>
            {
                UpdateCurrentDLLsFromGameAssets(preinspectedNgxOta: preinspectedNgxOta);
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.InsertAllAsync(resetHistory, false);
                await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
                await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
            }

            return (true, string.Empty, false);
        }
        else
        {
            var dllHistory = new List<GameHistory>();
            foreach (var existingBackupRecord in existingBackupRecords)
            {
                var primaryRecordName = existingBackupRecord.Path.Replace(".dlsss", string.Empty, StringComparison.OrdinalIgnoreCase);
                var existingRecords = this.GameAssets.Where(x => x.AssetType == gameAssetType && x.Path.Equals(primaryRecordName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (existingRecords.Count != 1)
                {
                    Logger.Info("Backup record was found, existing records were not.");
                    return (false, "Unable to reset to default. Please repair your game manually.", false);
                }

                var existingRecord = existingRecords[0];

                try
                {
                    File.Move(existingBackupRecord.Path, existingRecord.Path, true);
                }
                catch (UnauthorizedAccessException err)
                {
                    Logger.Error(err);
                    if (App.CurrentApp.IsAdminUser() is false)
                    {
                        return (false, "Unable to reset to default. Running XTR Unlocker as administrator may fix this.", true);
                    }
                    else
                    {
                        return (false, "Unable to reset to default. Please repair your game manually.", false);
                    }
                }
                catch (Exception err)
                {
                    Logger.Error(err);
                    return (false, "Unable to reset to default. Please repair your game manually.", false);
                }

                var newGameAsset = new GameAsset()
                {
                    Id = ID,
                    AssetType = gameAssetType,
                    Path = existingRecord.Path,
                    Version = existingBackupRecord.Version,
                    Hash = existingBackupRecord.Hash,
                };


                dllHistory.Add(new GameHistory()
                {
                    GameId = ID,
                    EventType = GameHistoryEventType.DLLReset,
                    EventTime = DateTime.Now,
                    AssetType = gameAssetType,
                    AssetPath = existingRecord.Path,
                    AssetVersion = existingBackupRecord.DisplayName,
                });

                UpdateCurrentAsset(newGameAsset, gameAssetType);

                GameAssets.Remove(existingRecord);
                GameAssets.Remove(existingBackupRecord);
                GameAssets.Add(newGameAsset);
            }

            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.InsertAllAsync(dllHistory, false);

                // Update game assets list by deleting and re-adding.
                await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
                await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
            }

            return (true, string.Empty, false);
        }
    }

    /// <summary>
    /// Attempts to update a DLSS dll in a given game.
    /// </summary>
    /// <param name="dllRecord"></param>
    /// <returns>Tuple containing a boolean of Success, if this is false there will be an error message in the Message response.</returns>
    internal async Task<(bool Success, string Message, bool PromptToRelaunchAsAdmin)> UpdateDllAsync(DLLRecord dllRecord)
    {
        if (dllRecord is null)
        {
            return (false, "Unable to swap dll as your dll record was not found.", false);
        }

        if (dllRecord.LocalRecord is null)
        {
            return (false, "Unable to swap dll as your local dll record was not found.", false);
        }

        if (File.Exists(dllRecord.LocalRecord.ExpectedPath) == false)
        {
            return (false, "Downloaded dll not found.", false);
        }

        if (!IsSwappableAssetType(dllRecord.AssetType) || !TryMapAssetTypeToSwapSlot(dllRecord.AssetType, out var swapSlot))
        {
            return (false, "Unsupported DLL asset type for swapping.", false);
        }

        var versionInfo = FileVersionInfo.GetVersionInfo(dllRecord.LocalRecord.ExpectedPath);
        var dllVersion = versionInfo.GetFormattedFileVersion();
        var md5Hash = versionInfo.GetMD5Hash();
        if (dllRecord.MD5Hash != md5Hash)
        {
            return (false, "Unable to swap dll because dll hash was invalid.", false);
        }

        // Validate new DLL
        if (Settings.Instance.AllowUntrusted == false)
        {
            var isTrusted = WinTrust.VerifyEmbeddedSignature(dllRecord.LocalRecord.ExpectedPath);
            if (isTrusted == false)
            {
                return (false, "Unable to swap dll as we are unable to verify the signature of the version you are trying to use.\nIf you wish to override this decision please enable 'Allow Untrusted' in settings.", false);
            }
        }

        var existingRecords = this.GameAssets.Where(x => x.AssetType == dllRecord.AssetType).ToList();
        var backupRecordType = DLLManager.Instance.GetAssetBackupType(dllRecord.AssetType);
        var existingBackupRecords = this.GameAssets.Where(x => x.AssetType == backupRecordType).ToList();

        var newGameAssets = new List<GameAsset>();
        var dllHistory = new List<GameHistory>();

        if (existingRecords.Count == 0)
        {
            var resolution = SwapTargetResolver.ResolveTargetForMissingAsset(
                swapSlot,
                InstallPath,
                GameAssets.Select(x => x.Path).ToList(),
                RuntimeManifest is not null,
                RuntimeManifest?.InstallRoot);

            if (!resolution.CanSwap)
            {
                return (false, resolution.Reason, false);
            }

            if (resolution.TargetAlreadyExistsOnDisk)
            {
                var backupDllPath = $"{resolution.TargetPath}.dlsss";
                if (File.Exists(backupDllPath) == false && !DeployedDllMarkerStore.IsOriginallyAbsent(ID, resolution.TargetPath))
                {
                    try
                    {
                        File.Copy(resolution.TargetPath, backupDllPath);

                        var existingOnDiskAsset = new GameAsset()
                        {
                            Id = ID,
                            AssetType = backupRecordType,
                            Path = backupDllPath,
                        };
                        existingOnDiskAsset.LoadVersionAndHash();
                        newGameAssets.Add(existingOnDiskAsset);
                    }
                    catch (UnauthorizedAccessException err)
                    {
                        Logger.Error(err);
                        if (App.CurrentApp.IsAdminUser() is false)
                        {
                            return (false, "Unable to swap dll as we are unable to write to the target directory. Running XTR Unlocker as administrator may fix this.", true);
                        }
                        else
                        {
                            return (false, "Unable to swap dll as we are unable to write to the target directory.", false);
                        }
                    }
                    catch (Exception err)
                    {
                        Logger.Error(err);
                        return (false, "Unable to swap dll. Please check your error log for more information.", false);
                    }
                }
                else if (File.Exists(backupDllPath) && existingBackupRecords.Count == 0)
                {
                    var existingBackupAsset = new GameAsset()
                    {
                        Id = ID,
                        AssetType = backupRecordType,
                        Path = backupDllPath,
                    };
                    existingBackupAsset.LoadVersionAndHash();
                    newGameAssets.Add(existingBackupAsset);
                }
            }

            try
            {
                var targetDir = Path.GetDirectoryName(resolution.TargetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(dllRecord.LocalRecord.ExpectedPath, resolution.TargetPath, true);

                if (!resolution.TargetAlreadyExistsOnDisk)
                {
                    DeployedDllMarkerStore.MarkOriginallyAbsent(ID, resolution.TargetPath);
                }

                var newGameAsset = new GameAsset()
                {
                    Id = ID,
                    AssetType = dllRecord.AssetType,
                    Path = resolution.TargetPath,
                    Version = dllVersion,
                    Hash = dllRecord.MD5Hash,
                };
                newGameAssets.Add(newGameAsset);

                dllHistory.Add(new GameHistory()
                {
                    GameId = ID,
                    EventType = GameHistoryEventType.DLLSwapped,
                    EventTime = DateTime.Now,
                    AssetType = dllRecord.AssetType,
                    AssetPath = resolution.TargetPath,
                    AssetVersion = dllRecord.DisplayName,
                });
            }
            catch (UnauthorizedAccessException err)
            {
                Logger.Error(err);
                if (App.CurrentApp.IsAdminUser() is false)
                {
                    return (false, "Unable to swap dll as we are unable to write to the target directory. Running XTR Unlocker as administrator may fix this.", true);
                }
                else
                {
                    return (false, "Unable to swap dll as we are unable to write to the target directory.", false);
                }
            }
            catch (IOException err) when (err.HResult == -2147024864)
            {
                Logger.Error(err);
                return (false, "Unable to swap dll. It appears to be in use by another program. Is your game currently running?", false);
            }
            catch (Exception err)
            {
                Logger.Error(err);
                return (false, "Unable to swap dll. Please check your error log for more information.", false);
            }
        }
        else
        {
            if (existingBackupRecords.Count == 0)
            {
                // Backup old dlls if no backup exists and the DLL wasn't originally absent on disk.
                foreach (var existingRecord in existingRecords)
                {
                    if (DeployedDllMarkerStore.IsOriginallyAbsent(ID, existingRecord.Path))
                    {
                        continue;
                    }

                    var dllPath = Path.GetDirectoryName(existingRecord.Path);
                    if (string.IsNullOrEmpty(dllPath))
                    {
                        Logger.Error("dllPath was null or empty.");
                        return (false, "Unable to swap dll. Please check your error log for more information.", false);
                    }

                    // Ensure we don't do anything if the target exists.
                    var backupDllPath = $"{existingRecord.Path}.dlsss";
                    if (File.Exists(backupDllPath) == false)
                    {
                        try
                        {
                            File.Copy(existingRecord.Path, backupDllPath);

                            var backupGameAsset = new GameAsset()
                            {
                                Id = ID,
                                AssetType = backupRecordType,
                                Path = backupDllPath,
                                Version = existingRecord.Version,
                                Hash = existingRecord.Hash,
                            };
                            newGameAssets.Add(backupGameAsset);
                        }
                        catch (UnauthorizedAccessException err)
                        {
                            Logger.Error(err);
                            if (App.CurrentApp.IsAdminUser() is false)
                            {
                                return (false, "Unable to swap dll as we are unable to write to the target directory. Running XTR Unlocker as administrator may fix this.", true);
                            }
                            else
                            {
                                return (false, "Unable to swap dll as we are unable to write to the target directory.", false);
                            }
                        }
                        catch (Exception err)
                        {
                            Logger.Error(err);
                            return (false, "Unable to swap dll. Please check your error log for more information.", false);
                        }
                    }
                }
            }

            foreach (var existingRecord in existingRecords)
            {
                try
                {
                    // Copy the DLL
                    File.Copy(dllRecord.LocalRecord.ExpectedPath, existingRecord.Path, true);

                    var newGameAsset = new GameAsset()
                    {
                        Id = ID,
                        AssetType = dllRecord.AssetType,
                        Path = existingRecord.Path,
                        Version = dllVersion,
                        Hash = dllRecord.MD5Hash,
                    };
                    // No need to call LoadVersionAndHash, the data is already here.
                    newGameAssets.Add(newGameAsset);

                    dllHistory.Add(new GameHistory()
                    {
                        GameId = ID,
                        EventType = GameHistoryEventType.DLLSwapped,
                        EventTime = DateTime.Now,
                        AssetType = dllRecord.AssetType,
                        AssetPath = existingRecord.Path,
                        AssetVersion = dllRecord.DisplayName,
                    });
                }
                catch (UnauthorizedAccessException err)
                {
                    Logger.Error(err);
                    if (App.CurrentApp.IsAdminUser() is false)
                    {
                        return (false, "Unable to swap dll as we are unable to write to the target directory. Running XTR Unlocker as administrator may fix this.", true);
                    }
                    else
                    {
                        return (false, "Unable to DLSS dll as we are unable to write to the target directory.", false);
                    }
                }
                catch (IOException err) when (err.HResult == -2147024864)
                {
                    Logger.Error(err);
                    return (false, "Unable to swap dll. It appears to be in use by another program. Is your game currently running?", false);
                }
                catch (Exception err)
                {
                    Logger.Error(err);
                    return (false, "Unable to swap dll. Please check your error log for more information.", false);
                }
            }
        }

        foreach (var existingRecrod in existingRecords)
        {
            GameAssets.Remove(existingRecrod);
        }
        GameAssets.AddRange(newGameAssets);

        // This should never be null.
        // Using FirstOrDefault as there may be multiple, but we only care about using the information of the first.
        var firstNewGameAsset = newGameAssets.FirstOrDefault(x => x.AssetType == dllRecord.AssetType);
        if (firstNewGameAsset is not null)
        {
            UpdateCurrentAsset(firstNewGameAsset, dllRecord.AssetType);
        }

        if (!HasSwappableItems)
        {
            await App.CurrentApp.RunOnUIThreadAsync(async () =>
            {
                HasSwappableItems = true;
                await SaveToDatabaseAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        // Update game assets list by deleting and re-adding.
        using (await Database.Instance.Mutex.LockAsync())
        {
            await Database.Instance.Connection.InsertAllAsync(dllHistory, false);
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
            await Database.Instance.Connection.InsertAllAsync(GameAssets, false).ConfigureAwait(false);
        }

        return (true, string.Empty, false);
    }

    void UpdateCurrentAsset(GameAsset newGameAsset, GameAssetType gameAssetType)
    {
        App.CurrentApp.RunOnUIThread(() =>
        {
            // NOTE: DLL type
            if (gameAssetType == GameAssetType.DLSS)
            {
                CurrentDLSS = null;
                CurrentDLSS = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.DLSS_G)
            {
                CurrentDLSS_G = null;
                CurrentDLSS_G = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.DLSS_D)
            {
                CurrentDLSS_D = null;
                CurrentDLSS_D = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.FSR_31_DX12)
            {
                CurrentFSR_31_DX12 = null;
                CurrentFSR_31_DX12 = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.FSR_31_VK)
            {
                CurrentFSR_31_VK = null;
                CurrentFSR_31_VK = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeSS)
            {
                CurrentXeSS = null;
                CurrentXeSS = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeSS_FG)
            {
                CurrentXeSS_FG = null;
                CurrentXeSS_FG = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeSS_DX11)
            {
                CurrentXeSS_DX11 = null;
                CurrentXeSS_DX11 = newGameAsset;
            }
            else if (gameAssetType == GameAssetType.XeLL)
            {
                CurrentXeLL = null;
                CurrentXeLL = newGameAsset;
            }
            else
            {
                Logger.Error($"Unknown AssetType: {gameAssetType}");
            }
        });
    }

    #region IComparable<Game>
    public int CompareTo(Game? other)
    {
        if (other is null)
        {
            return -1;
        }

        return Title.CompareTo(other.Title);
    }
    #endregion

    /*
    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged = null;
    void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
    */


    protected Task ResizeCoverAsync(Stream imageStream)
    {
        // TODO:
        // - find optimal format (eg, is displaying 100 webp images more intense than 100 png images)
        // - load image based on scale
        try
        {
            SaveCoverPng(imageStream, ExpectedCoverImage, 200 * 2, 300 * 2);

            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = null;
                CoverImage = ExpectedCoverImage;
            });
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }

        return Task.CompletedTask;
    }


    public void AddCustomCover(string imageSource)
    {
        using (var fileStream = File.OpenRead(imageSource))
        {
            AddCustomCover(fileStream);
        }
    }

    public void AddCustomCover(Stream stream)
    {
        // TODO:
        // - find optimal format (eg, is displaying 100 webp images more intense than 100 png images)
        // - load image based on scale
        try
        {
            SaveCoverPng(stream, ExpectedCustomCoverImage, 200 * 3, 300 * 3);

            App.CurrentApp.RunOnUIThread(() =>
            {
                CoverImage = ExpectedCustomCoverImage;
            });
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    private static void SaveCoverPng(Stream source, string destination, int maxWidth, int maxHeight)
    {
        using var image = SKBitmap.Decode(source) ?? throw new InvalidDataException("The cover image could not be decoded.");
        var scale = Math.Min(1d, Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var resized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (!image.ScalePixels(resized, new SKSamplingOptions(SKCubicResampler.CatmullRom)))
        {
            throw new InvalidDataException("The cover image could not be resized.");
        }

        using var encoded = resized.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The cover image could not be encoded as PNG.");
        using var output = File.Create(destination);
        encoded.SaveTo(output);
    }

    protected async Task<bool> DownloadCoverAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Logger.Error($"Tried to download cover image but url was null or empty. Game: {Title}, Library: {GameLibrary}");
            return false;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == false &&
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == false)
        {
            Logger.Error($"Tried to download cover image but url was not valid. Game: {Title}, Library: {GameLibrary}, Url: {url}");
            return false;
        }


        var extension = Path.GetExtension(url);

        // Path.GetExtension retains query arguments, so ths will remove them if they exist.
        if (extension.Contains('?'))
        {
            extension = extension.Substring(0, extension.IndexOf("?"));
        }
        var tempFile = Path.Combine(Storage.GetTemp(), $"{ID}{extension}");


        try
        {
            using (var memoryStream = new MemoryStream())
            {
                var fileDownloader = new FileDownloader(url, 0);
                await fileDownloader.DownloadFileToStreamAsync(memoryStream).ConfigureAwait(false);
                memoryStream.Position = 0;

                // Now if the image is downloaded lets resize it,
                await ResizeCoverAsync(memoryStream).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception err)
        {
            Logger.Error(err, $"For url: {url}");
            //Debugger.Break();
            return false;
        }
        finally
        {
            // Cleanup temp file.
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public async Task SaveToDatabaseAsync()
    {
        try
        {
            var rowsChanged = -1;
            using (await Database.Instance.Mutex.LockAsync())
            {
                rowsChanged = await Database.Instance.Connection.InsertOrReplaceAsync(this);
                // tODO: Configure await
            }
            if (rowsChanged == 0)
            {
                // TODO: Fix why this happens occasionally to reandom games.
                // This appears to change to different games in different libraries.
                Logger.Error($"Tried to save game to database but rowsChanged was 0.");
                //Debugger.Break();
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }
    }

    public async Task DeleteAsync()
    {
        try
        {
            // Sometimes when a game is uninstalled the backup files are not removed, so ensure they are.
            // https://github.com/beeradmoore/dlss-swapper/issues/236

            List<GameAsset> gameAssets;
            using (await Database.Instance.Mutex.LockAsync())
            {
                gameAssets = await Database.Instance.Connection.Table<GameAsset>().Where(ga => ga.Id == ID).ToListAsync();
            }
            foreach (var cachedGameAsset in gameAssets)
            {
                // NOTE: DLL type
                // If its a file we made we should attempt to delete it.
                if (cachedGameAsset.AssetType == GameAssetType.DLSS_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.DLSS_G_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.DLSS_D_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.FSR_31_DX12_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.FSR_31_VK_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeSS_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeSS_FG_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeSS_DX11_BACKUP ||
                    cachedGameAsset.AssetType == GameAssetType.XeLL_BACKUP)
                {
                    if (File.Exists(cachedGameAsset.Path))
                    {
                        Logger.Info($"Deleting {cachedGameAsset.Path}");
                        try
                        {
                            File.Delete(cachedGameAsset.Path);
                        }
                        catch (Exception err)
                        {
                            Logger.Error(err, $"Could not delete {cachedGameAsset.Path}");
                        }
                    }
                }
            }
            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.Table<GameAsset>().DeleteAsync(ga => ga.Id == ID).ConfigureAwait(false);
            }

            // Delete the thumbnails.
            var thumbnailImages = Directory.GetFiles(Storage.GetImageCachePath(), $"{ID}_*", SearchOption.AllDirectories);
            foreach (var thumbnailImage in thumbnailImages)
            {
                try
                {
                    Logger.Info($"Deleting {thumbnailImage}");
                    File.Delete(thumbnailImage);
                }
                catch (Exception err)
                {
                    Logger.Error(err, $"Could not delete {thumbnailImage}");
                }
            }

            // Delete deployed DLL markers.
            DeployedDllMarkerStore.DeleteGameMarkers(ID);

            // Delete the game itself.
            using (await Database.Instance.Mutex.LockAsync())
            {
                await Database.Instance.Connection.DeleteAsync(this).ConfigureAwait(false);
            }

            // Remove the game from the list.
            GameManager.Instance.RemoveGame(this);
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    public async Task PromptToRemoveCustomCover()
    {
        var dialog = new EasyContentDialog(App.CurrentApp.MainWindow.Content.XamlRoot)
        {
            Title = ResourceHelper.GetString("Game_CustomCoverRemove"),
            PrimaryButtonText = ResourceHelper.GetString("General_Remove"),
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = ResourceHelper.GetString("Game_AreYouSureRemoveCustomCover"),
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            CoverImage = null;

            if (File.Exists(ExpectedCustomCoverImage))
            {
                File.Delete(ExpectedCustomCoverImage);
            }

            if (this.GameLibrary == GameLibrary.ManuallyAdded)
            {
                await SaveToDatabaseAsync();
            }

            // Will load default or attempt to fetch fresh.
            await LoadCoverImageAsync();
        }
    }

    public void PromptToBrowseCustomCover()
    {
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);

            var fileFilters = new List<FileSystemHelper.FileFilter>()
            {
                new FileSystemHelper.FileFilter("Image files", "*.jpg; *.jpeg; *.png; *.webp"),
            };

            var coverImageFile = FileSystemHelper.OpenFile(hWnd, fileFilters, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

            //                    ViewMode = PickerViewMode.Thumbnail,


            if (string.IsNullOrWhiteSpace(coverImageFile))
            {
                return;
            }

            AddCustomCover(coverImageFile);
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    public bool Equals(Game? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ID == other.ID)
        {
            return true;
        }

        if (PlatformId == other.PlatformId)
        {
            return true;
        }

        return false;
    }

    protected bool ParentUpdateFromGame(Game game)
    {
        var didChange = false;

        if (Title != game.Title)
        {
            Title = game.Title;
            didChange = true;
        }

        if (InstallPath != game.InstallPath)
        {
            InstallPath = PathHelpers.NormalizePath(game.InstallPath);
            didChange = true;
        }

        if (CoverImage != game.CoverImage)
        {
            CoverImage = game.CoverImage;
            didChange = true;
        }

        if (HasSwappableItems != game.HasSwappableItems)
        {
            HasSwappableItems = game.HasSwappableItems;
            didChange = true;
        }

        // NOTE: DLL type
        if (CurrentDLSS != game.CurrentDLSS)
        {
            CurrentDLSS = game.CurrentDLSS;
            didChange = true;
        }

        if (DlssPreset != game.DlssPreset)
        {
            DlssPreset = game.DlssPreset;
            didChange = true;
        }

        if (DlssDPreset != game.DlssDPreset)
        {
            DlssDPreset = game.DlssDPreset;
            didChange = true;
        }

        if (CurrentDLSS_G != game.CurrentDLSS_G)
        {
            CurrentDLSS_G = game.CurrentDLSS_G;
            didChange = true;
        }

        if (CurrentDLSS_D != game.CurrentDLSS_D)
        {
            CurrentDLSS_D = game.CurrentDLSS_D;
            didChange = true;
        }

        if (CurrentFSR_31_DX12 != game.CurrentFSR_31_DX12)
        {
            CurrentFSR_31_DX12 = game.CurrentFSR_31_DX12;
            didChange = true;
        }

        if (CurrentFSR_31_VK != game.CurrentFSR_31_VK)
        {
            CurrentFSR_31_VK = game.CurrentFSR_31_VK;
            didChange = true;
        }

        if (CurrentXeSS != game.CurrentXeSS)
        {
            CurrentXeSS = game.CurrentXeSS;
            didChange = true;
        }

        if (CurrentXeSS_FG != game.CurrentXeSS_FG)
        {
            CurrentXeSS_FG = game.CurrentXeSS_FG;
            didChange = true;
        }

        if (CurrentXeSS_DX11 != game.CurrentXeSS_DX11)
        {
            CurrentXeSS_DX11 = game.CurrentXeSS_DX11;
            didChange = true;
        }

        if (CurrentXeLL != game.CurrentXeLL)
        {
            CurrentXeLL = game.CurrentXeLL;
            didChange = true;
        }

        if (HasStreamlineStack != game.HasStreamlineStack)
        {
            HasStreamlineStack = game.HasStreamlineStack;
            didChange = true;
        }

        if (HasStreamlineDlss != game.HasStreamlineDlss)
        {
            HasStreamlineDlss = game.HasStreamlineDlss;
            didChange = true;
        }

        if (HasStreamlineDlssG != game.HasStreamlineDlssG)
        {
            HasStreamlineDlssG = game.HasStreamlineDlssG;
            didChange = true;
        }

        if (HasStreamlineDlssD != game.HasStreamlineDlssD)
        {
            HasStreamlineDlssD = game.HasStreamlineDlssD;
            didChange = true;
        }

        if (StreamlineDlssVersion != game.StreamlineDlssVersion)
        {
            StreamlineDlssVersion = game.StreamlineDlssVersion;
            didChange = true;
        }

        if (StreamlineDlssGVersion != game.StreamlineDlssGVersion)
        {
            StreamlineDlssGVersion = game.StreamlineDlssGVersion;
            didChange = true;
        }

        if (StreamlineDlssDVersion != game.StreamlineDlssDVersion)
        {
            StreamlineDlssDVersion = game.StreamlineDlssDVersion;
            didChange = true;
        }

        if (StreamlineSdkVersion != game.StreamlineSdkVersion)
        {
            StreamlineSdkVersion = game.StreamlineSdkVersion;
            didChange = true;
        }

        if (HasFidelityFxSdk2 != game.HasFidelityFxSdk2)
        {
            HasFidelityFxSdk2 = game.HasFidelityFxSdk2;
            didChange = true;
        }

        // We don't copy across the following properties as it is assume this object has the latest revisions:
        // - Notes
        // - IsFavourite

        return didChange;
    }

    public abstract bool UpdateFromGame(Game game);

    private bool IsRootAsset(GameAsset asset)
    {
        if (string.IsNullOrWhiteSpace(InstallPath) || string.IsNullOrWhiteSpace(asset.Path))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(asset.Path);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        return string.Equals(
            Path.TrimEndingDirectorySeparator(dir),
            Path.TrimEndingDirectorySeparator(InstallPath),
            StringComparison.OrdinalIgnoreCase);
    }

    void UpdateCurrentDLLsFromGameAssets(UpscalerDiscoveryResult? discovery = null, NgxOtaModelStoreInfo? preinspectedNgxOta = null)
    {
        CurrentDLSS = null;
        CurrentDLSS_G = null;
        CurrentDLSS_D = null;
        CurrentFSR_31_DX12 = null;
        CurrentFSR_31_VK = null;
        CurrentXeSS = null;
        CurrentXeSS_FG = null;
        CurrentXeSS_DX11 = null;
        CurrentXeLL = null;

        // NOTE: DLL type
        MultipleDLSSFound = GameAssets.Count(x => x.AssetType == GameAssetType.DLSS) > 1;
        MultipleDLSSGFound = GameAssets.Count(x => x.AssetType == GameAssetType.DLSS_G) > 1;
        MultipleDLSSDFound = GameAssets.Count(x => x.AssetType == GameAssetType.DLSS_D) > 1;
        MultipleFSR31DX12Found = GameAssets.Count(x => x.AssetType == GameAssetType.FSR_31_DX12) > 1;
        MultipleFSR31VKFound = GameAssets.Count(x => x.AssetType == GameAssetType.FSR_31_VK) > 1;
        MultipleXeSSFound = GameAssets.Count(x => x.AssetType == GameAssetType.XeSS) > 1;
        MultipleXeSSFGFound = GameAssets.Count(x => x.AssetType == GameAssetType.XeSS_FG) > 1;
        MultipleXeSSDX11Found = GameAssets.Count(x => x.AssetType == GameAssetType.XeSS_DX11) > 1;
        MultipleXeLLFound = GameAssets.Count(x => x.AssetType == GameAssetType.XeLL) > 1;

        CurrentDLSS = GameAssets.Where(x => x.AssetType == GameAssetType.DLSS).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentDLSS_G = GameAssets.Where(x => x.AssetType == GameAssetType.DLSS_G).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentDLSS_D = GameAssets.Where(x => x.AssetType == GameAssetType.DLSS_D).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentFSR_31_DX12 = GameAssets.Where(x => x.AssetType == GameAssetType.FSR_31_DX12).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentFSR_31_VK = GameAssets.Where(x => x.AssetType == GameAssetType.FSR_31_VK).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentXeSS = GameAssets.Where(x => x.AssetType == GameAssetType.XeSS).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentXeSS_FG = GameAssets.Where(x => x.AssetType == GameAssetType.XeSS_FG).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentXeSS_DX11 = GameAssets.Where(x => x.AssetType == GameAssetType.XeSS_DX11).OrderByDescending(IsRootAsset).FirstOrDefault();
        CurrentXeLL = GameAssets.Where(x => x.AssetType == GameAssetType.XeLL).OrderByDescending(IsRootAsset).FirstOrDefault();

        if (discovery is not null)
        {
            HasStreamlineStack = discovery.StreamlineStackDetected;
            HasStreamlineDlss = discovery.HasStreamlineNgxOtaDlss;
            HasStreamlineDlssG = discovery.HasStreamlineNgxOtaDlssG;
            HasStreamlineDlssD = discovery.HasStreamlineNgxOtaDlssD;
            StreamlineDlssVersion = discovery.HasStreamlineNgxOtaDlss ? discovery.NgxOta.DlssVersion : null;
            StreamlineDlssGVersion = discovery.HasStreamlineNgxOtaDlssG ? discovery.NgxOta.DlssGVersion : null;
            StreamlineDlssDVersion = discovery.HasStreamlineNgxOtaDlssD ? discovery.NgxOta.DlssDVersion : null;
            StreamlineSdkVersion = discovery.StreamlineInterposer.FirstOrDefault()?.FileVersion
                ?? discovery.StreamlineDirectSr.FirstOrDefault()?.FileVersion;
            HasFidelityFxSdk2 = discovery.FidelityFxSdk2Files.Count > 0;
        }
        else
        {
            var hasInterposer = GameAssets.Any(x => x.AssetType == GameAssetType.Streamline_Interposer);
            var hasDirectSr = GameAssets.Any(x => x.AssetType == GameAssetType.Streamline_DirectSR);
            var hasSlDlss = GameAssets.Any(x => x.AssetType == GameAssetType.Streamline_DLSS);
            var hasSlDlssG = GameAssets.Any(x => x.AssetType == GameAssetType.Streamline_DLSS_G);
            var hasSlDlssD = GameAssets.Any(x => x.AssetType == GameAssetType.Streamline_DLSS_D);

            HasStreamlineStack = GameAssets.Any(x => IsStreamlineAssetType(x.AssetType));

            HasFidelityFxSdk2 = GameAssets.Any(x => x.AssetType is GameAssetType.FidelityFX_SDK2_Loader_DX12
                or GameAssetType.FidelityFX_SDK2_Upscaler_DX12
                or GameAssetType.FidelityFX_SDK2_FrameGeneration_DX12
                or GameAssetType.FidelityFX_SDK2_Denoiser_DX12
                or GameAssetType.FidelityFX_SDK2_RadianceCache_DX12
                or GameAssetType.FidelityFX_SDK2_Upscaler_VK
                or GameAssetType.FidelityFX_SDK2_FrameGeneration_VK
                or GameAssetType.FidelityFX_SDK2_Loader_VK
                or GameAssetType.FidelityFX_SDK2_Other);

            if (HasStreamlineStack)
            {
                var ngxOta = preinspectedNgxOta ?? UpscalerDiscoveryService.InspectNgxOtaStore();
                HasStreamlineDlss = CurrentDLSS is null && (hasSlDlss || (hasInterposer && hasDirectSr));
                HasStreamlineDlssG = CurrentDLSS_G is null && (hasSlDlssG || (hasInterposer && hasDirectSr && (ngxOta.HasDlssGModel || ngxOta.HasStreamlineDlssGOta)));
                HasStreamlineDlssD = CurrentDLSS_D is null && hasSlDlssD;
                StreamlineDlssVersion = HasStreamlineDlss ? ngxOta.DlssVersion : null;
                StreamlineDlssGVersion = HasStreamlineDlssG ? ngxOta.DlssGVersion : null;
                StreamlineDlssDVersion = HasStreamlineDlssD ? ngxOta.DlssDVersion : null;
                StreamlineSdkVersion = GameAssets.FirstOrDefault(x => x.AssetType == GameAssetType.Streamline_Interposer)?.Version
                    ?? GameAssets.FirstOrDefault(x => x.AssetType == GameAssetType.Streamline_DirectSR)?.Version;
            }
            else
            {
                HasStreamlineDlss = false;
                HasStreamlineDlssG = false;
                HasStreamlineDlssD = false;
                StreamlineDlssVersion = null;
                StreamlineDlssGVersion = null;
                StreamlineDlssDVersion = null;
                StreamlineSdkVersion = null;
            }
        }
    }

    public async Task RemoveGameAssetsFromCacheAsync()
    {
        using (await Database.Instance.Mutex.LockAsync())
        {
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", ID).ConfigureAwait(false);
        }
    }

    public async Task LoadGameAssetsFromCacheAsync()
    {
        await LoadCoverImageAsync();

        GameAssets.Clear();
        using (await Database.Instance.Mutex.LockAsync())
        {
            var gameAssets = await Database.Instance.Connection.Table<GameAsset>().Where(ga => ga.Id == ID).ToListAsync().ConfigureAwait(false);
            if (gameAssets?.Any() == true)
            {
                GameAssets.AddRange(gameAssets);
            }
        }

        var preinspectedNgxOta = GameAssets.Any(x => IsStreamlineAssetType(x.AssetType))
            ? UpscalerDiscoveryService.InspectNgxOtaStore()
            : null;

        App.CurrentApp.RunOnUIThread(() =>
        {
            UpdateCurrentDLLsFromGameAssets(preinspectedNgxOta: preinspectedNgxOta);
        });

        // TODO: Add auto reload by storing last full reload time on game

        if (GameAssets.Any())
        {
            foreach (var gameAsset in GameAssets)
            {
                // Check that each of the game assets exist, after we will check if they are what we expect them to be
                if (File.Exists(gameAsset.Path) == false)
                {
                    NeedsProcessing = true;
                    break;
                }
            }

            if (NeedsProcessing == false)
            {
                var unknownGameAssets = new List<GameAsset>();
                foreach (var gameAsset in GameAssets)
                {
                    if (!IsSwappableAssetType(gameAsset.AssetType))
                    {
                        continue;
                    }

                    if (DLLManager.Instance.IsInKnownGameAsset(gameAsset, this) == false)
                    {
                        unknownGameAssets.Add(gameAsset);
                    }
                }
                if (unknownGameAssets.Any())
                {
                    GameManager.Instance.AddUnknownGameAssets(GameLibrary, Title, unknownGameAssets);
                }

                foreach (var gameAsset in GameAssets)
                {
                    var fileVersionInfo = FileVersionInfo.GetVersionInfo(gameAsset.Path);
                    var freshVersion = fileVersionInfo.GetFormattedFileVersion();

                    if (gameAsset.Version != freshVersion)
                    {
                        NeedsProcessing = true;
                        break;
                    }
                }
            }

            if (NeedsProcessing == false && !string.IsNullOrWhiteSpace(InstallPath) && Directory.Exists(InstallPath))
            {
                if (UpscalerDiscoveryService.HasUncachedUpscalerFiles(InstallPath, GameAssets.Select(x => x.Path)))
                {
                    NeedsProcessing = true;
                }
            }
        }
        else
        {
            // If there is no known current DLLs then we likely want to do a full reload in case the game got updated.
            // TODO: Also add a time last reloaded here.
            NeedsProcessing = true;
            return;
        }
    }

    public bool IsInIgnoredPath()
    {
        // If there are no ignored paths we can skip this altogether.
        if (Settings.Instance.IgnoredPaths.Length == 0)
        {
            return false;
        }

        // If installed path is empty we should consider it ignored.
        if (string.IsNullOrWhiteSpace(InstallPath))
        {
            return true;
        }

        foreach (var ignoredPath in Settings.Instance.IgnoredPaths)
        {
            // Because we make IgnoredPaths have a / on the end it will fail the below check.
            // In the cases where the path could be off by one we will do a manual check.
            if (ignoredPath.Length - 1 == InstallPath.Length)
            {
                var tempInstallPath = InstallPath + Path.DirectorySeparatorChar;
                if (tempInstallPath.Equals(ignoredPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }


            if (InstallPath.StartsWith(ignoredPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
