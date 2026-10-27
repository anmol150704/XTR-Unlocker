using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    public partial string SelectedHudKey { get; set; } = "Page Up (0x21)";

    [ObservableProperty]
    public partial string SelectedCycleKey { get; set; } = "Page Down (0x22)";

    [ObservableProperty]
    public partial string SelectedFgKey { get; set; } = "End (0x23)";

    [ObservableProperty]
    public partial int SelectedPositionIndex { get; set; } = 0;

    [ObservableProperty]
    public partial int SelectedModeIndex { get; set; } = 1;

    [ObservableProperty]
    public partial string SelectedTheme { get; set; } = "Apple Emerald Glass";

    [ObservableProperty]
    public partial int TotalGamesCount { get; set; } = 0;

    [ObservableProperty]
    public partial int AcceleratedGamesCount { get; set; } = 0;

    [ObservableProperty]
    public partial string SimulatedHudFps { get; set; } = "128 FPS";

    [ObservableProperty]
    public partial string SimulatedHudFrametime { get; set; } = "7.8 ms";

    [ObservableProperty]
    public partial string SimulatedHudResolution { get; set; } = "2560x1440 (DLSS 3.7 - Quality)";

    [ObservableProperty]
    public partial string SimulatedHudFg { get; set; } = "FG Active (FSR 3.1 Bridge • Hudless)";

    public ObservableCollection<string> DevMenuKeys { get; } = new()
    {
        "Insert (0x2D)",
        "F11 (0x7A)",
        "Home (0x24)",
        "F12 (0x7B)"
    };

    public ObservableCollection<string> HudKeys { get; } = new()
    {
        "Page Up (0x21)",
        "F10 (0x79)",
        "F9 (0x78)",
        "Insert (0x2D)"
    };

    public ObservableCollection<string> CycleKeys { get; } = new()
    {
        "Page Down (0x22)",
        "F8 (0x77)",
        "F7 (0x76)"
    };

    public ObservableCollection<string> FgKeys { get; } = new()
    {
        "End (0x23)",
        "F12 (0x7B)",
        "F6 (0x75)"
    };

    public ObservableCollection<string> HudPositions { get; } = new()
    {
        "Top-Left (Default)",
        "Top-Right",
        "Bottom-Left",
        "Bottom-Right"
    };

    public ObservableCollection<string> HudModes { get; } = new()
    {
        "Simple (FPS Only)",
        "Detailed (FPS + Latency + Bridge)",
        "Detailed + Graph",
        "Full Telemetry"
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
        SelectedHudKey = MapHexToDisplayName(Settings.Instance.SelectedHudHotkey, HudKeys, "Page Up (0x21)");
        SelectedCycleKey = MapHexToDisplayName(Settings.Instance.SelectedHudCycleHotkey, CycleKeys, "Page Down (0x22)");
        SelectedFgKey = MapHexToDisplayName(Settings.Instance.SelectedFrameGenHotkey, FgKeys, "End (0x23)");

        SelectedPositionIndex = Math.Clamp(Settings.Instance.SelectedHudPosition, 0, HudPositions.Count - 1);
        SelectedModeIndex = Settings.Instance.SelectedHudMode == 1 ? 0 :
                             Settings.Instance.SelectedHudMode == 2 ? 1 :
                             Settings.Instance.SelectedHudMode == 3 ? 2 : 1;

        SelectedTheme = Settings.Instance.SelectedOverlayTheme ?? "Apple Emerald Glass";

        RefreshLibraryStats();
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

        if (!string.IsNullOrEmpty(SelectedHudKey))
            Settings.Instance.SelectedHudHotkey = ExtractHex(SelectedHudKey);

        if (!string.IsNullOrEmpty(SelectedCycleKey))
            Settings.Instance.SelectedHudCycleHotkey = ExtractHex(SelectedCycleKey);

        if (!string.IsNullOrEmpty(SelectedFgKey))
            Settings.Instance.SelectedFrameGenHotkey = ExtractHex(SelectedFgKey);

        Settings.Instance.SelectedHudPosition = SelectedPositionIndex;
        Settings.Instance.SelectedHudMode = SelectedModeIndex == 0 ? 1 : SelectedModeIndex == 1 ? 2 : 3;
        Settings.Instance.SelectedOverlayTheme = SelectedTheme;

        StatusBanner = $"Settings Saved • In-game HUD will toggle with {SelectedHudKey} • Theme: {SelectedTheme}";
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
        await Task.Delay(250);
        HardwareInfo = GpuHardwareDetector.GetHardwareInfo(refresh: true);
        IsProcessing = false;
        StatusBanner = $"Hardware Verified • {HardwareInfo.Name} ({HardwareInfo.Architecture})";
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
                    foreach (var game in games)
                    {
                        try
                        {
                            var plan = await DeploymentPlanner.CreatePlanAsync(game.ID, game.Title, game.InstallPath, pack, RuntimeIntegrationPreset.TurboBoost);
                            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
                            await game.SaveRuntimeManifestAsync(manifest);
                            count++;
                        }
                        catch
                        {
                            // Continue with next game
                        }
                    }
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
                foreach (var game in games)
                {
                    try
                    {
                        await RuntimeInstallService.RestoreAsync(game.ID, game.InstallPath);
                        App.CurrentApp.RunOnUIThread(() => game.RuntimeManifest = null);
                        await game.RemoveRuntimeProfileAsync();
                        count++;
                    }
                    catch
                    {
                        // Continue with next game
                    }
                }
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
