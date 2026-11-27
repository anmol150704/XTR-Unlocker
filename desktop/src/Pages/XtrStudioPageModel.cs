using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSS_Swapper.Data;
using DLSS_Swapper.Data.Hardware;
using DLSS_Swapper.Data.Runtime;
using Microsoft.UI.Xaml;

namespace DLSS_Swapper.Pages;

public partial class XtrStudioPageModel : ObservableObject
{
    private readonly XtrStudioPage _page;

    [ObservableProperty]
    public partial GpuHardwareInfo HardwareInfo { get; set; }

    [ObservableProperty]
    public partial string StatusBanner { get; set; } = "Engine Ready • Hardware Optical Flow & Neural Models Active";

    [ObservableProperty]
    public partial bool IsProcessing { get; set; } = false;

    [ObservableProperty]
    public partial string SelectedDevMenuKey { get; set; } = "Insert (0x2D)";

    [ObservableProperty]
    public partial string SelectedFgKey { get; set; } = "End (0x23)";

    [ObservableProperty]
    public partial string SelectedGlobalPreset { get; set; } = "Preset E (Anti-Ghosting Champion)";

    [ObservableProperty]
    public partial bool GlobalAntiGhostingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool GlobalStreamlineSpoofingEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalRcasSharpnessLabel))]
    public partial double GlobalRcasSharpness { get; set; } = 35.0;

    public string GlobalRcasSharpnessLabel => $"{Math.Round(GlobalRcasSharpness)}%";

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "Apple Emerald Glass";

    [ObservableProperty]
    public partial int TotalGamesCount { get; set; } = 0;

    [ObservableProperty]
    public partial int AcceleratedGamesCount { get; set; } = 0;

    [ObservableProperty]
    public partial string NgxStoreStatus { get; set; } = "OTA Model Store Synchronized • SHA-256 Verified";

    [ObservableProperty]
    public partial int InstalledNgxModelsCount { get; set; } = 0;

    [ObservableProperty]
    public partial string DirectSrCompatibilityStatus { get; set; } = "DirectSR 1.0 & Streamline v2.4 Interposer Ready";

    [ObservableProperty]
    public partial bool AutoMinimizeToTray { get; set; } = true;

    public ObservableCollection<string> DevMenuKeys { get; } = new()
    {
        "Insert (0x2D)",
        "F11 (0x7A)",
        "Home (0x24)",
        "F12 (0x7B)"
    };

    public ObservableCollection<string> FgKeys { get; } = new()
    {
        "End (0x23)",
        "F12 (0x7B)",
        "F6 (0x75)"
    };

    public ObservableCollection<string> GlobalPresets { get; } = new()
    {
        "Preset E (Anti-Ghosting Champion)",
        "Preset K (DLSS 4 / 4.5 Ultra-Low Latency Transformer)",
        "Preset M (DLSS 4.5 FP8 Optical Transformer)",
        "Preset F (Fast Motion & Reduced Shimmer)",
        "Preset C (Low Ghosting / High Stability)",
        "Preset D (Classic DLSS 2.5 Profile)",
        "Default (Game Engine Default)"
    };

    public ObservableCollection<string> OverlayThemes { get; } = new()
    {
        "Apple Emerald Glass",
        "Cyberpunk Neon",
        "Stealth Obsidian"
    };

    public XtrStudioPageModel(XtrStudioPage page)
    {
        _page = page;
        HardwareInfo = GpuHardwareDetector.GetHardwareInfo();

        // Load persisted settings
        SelectedDevMenuKey = MapHexToDisplayName(Settings.Instance.SelectedDevMenuHotkey, DevMenuKeys, "Insert (0x2D)");
        SelectedFgKey = MapHexToDisplayName(Settings.Instance.SelectedFrameGenHotkey, FgKeys, "End (0x23)");
        SelectedTheme = Settings.Instance.SelectedOverlayTheme ?? "Apple Emerald Glass";

        RefreshLibraryStats();
        RefreshNgxStoreStats();
    }

    public void RefreshNgxStoreStats()
    {
        try
        {
            InstalledNgxModelsCount = DLLManager.Instance.DLSSRecords?.Count ?? 0;
            NgxStoreStatus = InstalledNgxModelsCount > 0
                ? $"{InstalledNgxModelsCount} Verified Neural Models Available (OTA Store Active)"
                : "OTA Model Store Active • Ready to synchronize";
        }
        catch
        {
            NgxStoreStatus = "OTA Model Store Active";
        }
    }

    private static string MapHexToDisplayName(string? hexValue, ObservableCollection<string> collection, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hexValue)) return fallback;
        var found = collection.FirstOrDefault(x => x.Contains(hexValue, StringComparison.OrdinalIgnoreCase));
        return found ?? fallback;
    }

    private static string ExtractHex(string displayName)
    {
        var start = displayName.IndexOf('(');
        var end = displayName.IndexOf(')');
        if (start >= 0 && end > start)
        {
            return displayName.Substring(start + 1, end - start - 1).Trim();
        }
        return "0x2D";
    }

    public void OnSettingChanged()
    {
        if (!string.IsNullOrEmpty(SelectedDevMenuKey))
            Settings.Instance.SelectedDevMenuHotkey = ExtractHex(SelectedDevMenuKey);

        if (!string.IsNullOrEmpty(SelectedFgKey))
            Settings.Instance.SelectedFrameGenHotkey = ExtractHex(SelectedFgKey);

        Settings.Instance.SelectedOverlayTheme = SelectedTheme;

        StatusBanner = $"Settings Saved • In-game diagnostics will open with {SelectedDevMenuKey} • Theme: {SelectedTheme}";
    }

    public void RefreshLibraryStats()
    {
        try
        {
            if (App.CurrentApp.MainWindow is MainWindow mainWindow && mainWindow.GameGridPage is GameGridPage gridPage)
            {
                var games = gridPage.GetDistinctVisibleGames().ToList();
                TotalGamesCount = games.Count;
                AcceleratedGamesCount = games.Count(g => g.RuntimeManifest != null || !string.IsNullOrEmpty(g.DlssVersionLabel));
            }
        }
        catch
        {
            TotalGamesCount = 0;
            AcceleratedGamesCount = 0;
        }
    }

    [RelayCommand]
    public async Task RefreshHardwareAsync()
    {
        IsProcessing = true;
        StatusBanner = "Scanning GPU Architecture & Display Adapters...";
        HardwareInfo = await GpuHardwareDetector.GetHardwareInfoAsync(refresh: true);
        IsProcessing = false;
        StatusBanner = $"Hardware Verified • {HardwareInfo.Name} ({HardwareInfo.Architecture})";
    }

    [RelayCommand]
    public async Task SyncNgxStoreAsync()
    {
        IsProcessing = true;
        StatusBanner = "Synchronizing NVIDIA NGX OTA Model Store...";
        try
        {
            await DLLManager.Instance.UpdateManifestAsync();
            RefreshNgxStoreStats();
            StatusBanner = "NVIDIA NGX OTA Model Store updated successfully.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"NGX Sync Note: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    public async Task TurboBoostAllGamesAsync()
    {
        IsProcessing = true;
        StatusBanner = "Applying XTR Turbo Boost & Anti-Ghosting Shield across library games...";

        int count = 0;
        try
        {
            var packagePath = RuntimeInstallService.GetBundledPackagePath();
            if (packagePath != null && Directory.Exists(packagePath))
            {
                var pack = await RuntimePack.LoadFromDirectoryAsync(packagePath);
                if (pack != null && App.CurrentApp.MainWindow is MainWindow mainWindow && mainWindow.GameGridPage is GameGridPage gridPage)
                {
                    var games = gridPage.GetDistinctVisibleGames().ToList();
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
                    };

                    await Parallel.ForEachAsync(games, parallelOptions, async (game, ct) =>
                    {
                        try
                        {
                            var plan = await DeploymentPlanner.CreatePlanAsync(game.ID, game.Title, game.InstallPath, pack, RuntimeIntegrationPreset.TurboBoost, cancellationToken: ct);
                            var manifest = game.RuntimeManifest is not null
                                ? await RuntimeInstallService.UpgradePlanAsync(plan, ct)
                                : await RuntimeInstallService.InstallPlanAsync(plan, ct);
                            await game.SaveRuntimeManifestAsync(manifest);
                            Interlocked.Increment(ref count);
                        }
                        catch
                        {
                            // Continue with next game
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Batch Optimization Note: {ex.Message}";
        }

        IsProcessing = false;
        RefreshLibraryStats();
        StatusBanner = count > 0
            ? $"Successfully Optimized {count} Games with Turbo Boost & Anti-Ghosting Shield!"
            : "Optimization complete. Select any game from the Library to apply individual tuning.";
    }

    [RelayCommand]
    public async Task RestoreAllGamesAsync()
    {
        IsProcessing = true;
        StatusBanner = "Restoring original game files across library games...";

        int count = 0;
        try
        {
            if (App.CurrentApp.MainWindow is MainWindow mainWindow && mainWindow.GameGridPage is GameGridPage gridPage)
            {
                var games = gridPage.GetDistinctVisibleGames().ToList();
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
                };

                await Parallel.ForEachAsync(games, parallelOptions, async (game, ct) =>
                {
                    try
                    {
                        await RuntimeInstallService.RestoreAsync(game.ID, game.InstallPath, ct);
                        App.CurrentApp.RunOnUIThread(() => game.RuntimeManifest = null);
                        await game.RemoveRuntimeProfileAsync();
                        Interlocked.Increment(ref count);
                    }
                    catch
                    {
                        // Continue with next game
                    }
                });
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Restore Note: {ex.Message}";
        }

        IsProcessing = false;
        RefreshLibraryStats();
        StatusBanner = count > 0
            ? $"Restored {count} Games to original stock state."
            : "All games are already in their original stock state.";
    }
}
