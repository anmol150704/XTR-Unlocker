using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSS_Swapper.Data;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.IO;
using Windows.System;
using DLSS_Swapper.Helpers;
using DLSS_Swapper.Data.Runtime;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DLSS_Swapper.Data.DLSS;
using System.ComponentModel;

namespace DLSS_Swapper.UserControls;

public partial class GameControlModel : ObservableObject
{
    WeakReference<GameControl> gameControlWeakReference;

    public Game Game { get; init; }

    public bool IsManuallyAdded => Game.GameLibrary == Interfaces.GameLibrary.ManuallyAdded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameTitleHasChanged))]
    public partial string GameTitle { get; set; }

    [ObservableProperty]
    public partial PresetOption? SelectedDlssPreset { get; set; }

    [ObservableProperty]
    public partial PresetOption? SelectedDlssDPreset { get; set; }

    [ObservableProperty]
    public partial PresetOption? SelectedDlssGPreset { get; set; }

    public bool CanSelectDlssPreset { get; private set; }

    public bool CanSelectDlssDPreset { get; private set; }

    public bool CanSelectDlssGPreset { get; private set; }

    public List<PresetOption> DlssPresetOptions { get; } = new List<PresetOption>();

    public List<PresetOption> DlssDPresetOptions { get; } = new List<PresetOption>();

    public List<PresetOption> DlssGPresetOptions { get; } = new List<PresetOption>();

    PresetOption? _previousDlssPreset;
    PresetOption? _previousDlssDPreset;
    PresetOption? _previousDlssGPreset;

    public bool GameTitleHasChanged
    {
        get
        {
            if (IsManuallyAdded == false)
            {
                return false;
            }

            if (string.IsNullOrEmpty(GameTitle))
            {
                return false;
            }

            return GameTitle.Equals(Game.Title) == false;
        }
    }

    public GameControlModelTranslationProperties TranslationProperties { get; } = new GameControlModelTranslationProperties();

    public IReadOnlyList<string> RuntimeProxyNames { get; } = new[]
    {
        "dxgi.dll", "winmm.dll", "d3d12.dll", "version.dll", "dbghelp.dll", "winhttp.dll", "wininet.dll"
    };

    public IReadOnlyList<string> AvailableDlssPresets { get; } = new[]
    {
        "Default (Game Engine Default)",
        "Preset E (Anti-Ghosting Champion)",
        "Preset F (Fast Motion & Reduced Shimmer)",
        "Preset C (Low Ghosting / High Stability)",
        "Preset D (Classic DLSS 2.5 Profile)",
        "Preset J (Transformer Model)"
    };

    public IReadOnlyList<string> AvailableOverlayPositions { get; } = new[]
    {
        "Top Left",
        "Top Right",
        "Bottom Left",
        "Bottom Right"
    };

    public IReadOnlyList<string> AvailableFramerateLimits { get; } = new[]
    {
        "Off (Unlimited)",
        "60 FPS",
        "120 FPS",
        "144 FPS",
        "165 FPS",
        "240 FPS"
    };

    private bool _isLoadingSettings;

    [ObservableProperty]
    public partial string SelectedRuntimeProxy { get; set; } = "dxgi.dll";

    [ObservableProperty]
    public partial string ActiveFrameGenMode { get; set; } = "Universal FG Bridge (FSR 3.1)";
    [ObservableProperty]
    public partial bool AntiGhostingShieldEnabled { get; set; } = true;

    partial void OnAntiGhostingShieldEnabledChanged(bool value)
    {
        if (_isLoadingSettings) return;
        ApplyAntiGhostingSetting(value);
    }

    [ObservableProperty]
    public partial bool InGameHudOverlayEnabled { get; set; } = true;

    partial void OnInGameHudOverlayEnabledChanged(bool value)
    {
        if (_isLoadingSettings) return;
        ApplyHudOverlaySetting(value);
    }

    [ObservableProperty]
    public partial bool StreamlineSpoofingEnabled { get; set; } = true;
    [ObservableProperty]
    public partial bool InGameDevMenuEnabled { get; set; } = true;

    partial void OnInGameDevMenuEnabledChanged(bool value)
    {
        if (_isLoadingSettings) return;
        ApplyDevMenuSetting(value);
    }

    partial void OnStreamlineSpoofingEnabledChanged(bool value)
    {
        if (_isLoadingSettings) return;
        ApplyStreamlineSpoofingSetting(value);
    }

    [ObservableProperty]
    public partial string SelectedEngineNeuralPreset { get; set; } = "Preset E (Anti-Ghosting Champion)";

    partial void OnSelectedEngineNeuralPresetChanged(string value)
    {
        if (_isLoadingSettings) return;
        ApplyEngineNeuralPresetSetting(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharpnessPercentLabel))]
    public partial double SharpnessPercentage { get; set; } = 35.0;

    public string SharpnessPercentLabel => $"{Math.Round(SharpnessPercentage)}%";

    partial void OnSharpnessPercentageChanged(double value)
    {
        if (_isLoadingSettings) return;
        ApplySharpnessSetting(value);
    }

    [ObservableProperty]
    public partial string SelectedOverlayPosition { get; set; } = "Top Left";

    partial void OnSelectedOverlayPositionChanged(string value)
    {
        if (_isLoadingSettings) return;
        ApplyOverlayPositionSetting(value);
    }

    [ObservableProperty]
    public partial string SelectedFramerateLimit { get; set; } = "Off (Unlimited)";

    [ObservableProperty]
    public partial bool IsTurboBoostActive { get; set; } = false;

    partial void OnSelectedFramerateLimitChanged(string value)
    {
        if (_isLoadingSettings) return;
        ApplyFramerateLimitSetting(value);
    }

    public string RuntimeComponentsText => Game.RuntimeManifest?.Components is { Count: > 0 } components
        ? string.Join(" | ", components)
        : "Engine components not recorded";

    public string RuntimeStatusText => Game.RuntimeManifest is not null
        ? "Restore available"
        : "Ready";

    public string DlssEngineSummaryText => $"Game engine using {Game.DlssGenerationText}";

    public string FrameGenerationStatusText => Game.CurrentDLSS_G is not null
        ? "Native DLSS-G detected"
        : Game.RuntimeManifest is not null
            ? "Frame generation bridge active"
            : "Available via runtime";

    public string PrimaryActionText => Game.RuntimeManifest is not null
        ? "Restore original files"
        : "Enable latest DLSS";

    public string RuntimeHealthSummaryText
    {
        get
        {
            if (Game.RuntimeManifest is null)
                return "Ready for deployment";

            var health = RuntimeHealthService.CheckHealth(Game.RuntimeManifest.InstallRoot, Game.RuntimeManifest.ProxyFileName ?? SelectedRuntimeProxy);
            return health.Summary;
        }
    }

    public string PrimaryActionExplanationText => Game.RuntimeManifest is not null
        ? RuntimeHealthSummaryText
        : "Installs the latest compatible DLSS runtime and verified configuration.";

    public GameControlModel(GameControl gameControl, Game game) : base()
    {
        gameControlWeakReference = new WeakReference<GameControl>(gameControl);
        Game = game;
        Game.PropertyChanged += Game_PropertyChanged;
        GameTitle = game.Title;
        if (RuntimeProxyNames.Contains(game.RuntimeManifest?.ProxyFileName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            SelectedRuntimeProxy = game.RuntimeManifest!.ProxyFileName!;
        }
        else
        {
            var compat = CompatibilityDatabase.LookupGame(game.Title) ?? CompatibilityDatabase.LookupGame(game.ID);
            if (compat != null && RuntimeProxyNames.Contains(compat.RecommendedProxy, StringComparer.OrdinalIgnoreCase))
            {
                SelectedRuntimeProxy = compat.RecommendedProxy;
            }
        }

        InitializeDeployedSettings();


        // Make sure NVAPIHelper is supported and the game has DLSS.
        if (NVAPIHelper.Instance.IsSupported && game.CurrentDLSS is not null)
        {
            // Try load the DriverSettingProfile for the given game. If it is not found the game is not supported.

            var gameProfile = NVAPIHelper.Instance.FindGameProfile(game);
            if (gameProfile is not null)
            {
                var gameDLSSPresetResult = NVAPIHelper.Instance.GetGameDLSSPreset(game);
                if (gameDLSSPresetResult.Success)
                {
                    CanSelectDlssPreset = true;
                    game.DlssPreset = gameDLSSPresetResult.Result;
                    DlssPresetOptions.AddRange(NVAPIHelper.Instance.DlssPresetOptions);
                    if (game.DlssPreset is null)
                    {
                        // If it was never set, ensure it goes to default.
                        SelectedDlssPreset = DlssPresetOptions.FirstOrDefault(x => x.Value == 0);
                    }
                    else
                    {
                        SelectedDlssPreset = DlssPresetOptions.FirstOrDefault(x => x.Value == game.DlssPreset);
                    }


                    if (Game.CurrentDLSS_D is not null)
                    {
                        var gameDLSSDPresetResult = NVAPIHelper.Instance.GetGameDLSSDPreset(game);
                        if (gameDLSSDPresetResult.Success)
                        {
                            CanSelectDlssDPreset = true;

                            game.DlssDPreset = gameDLSSDPresetResult.Result;
                            DlssDPresetOptions.AddRange(NVAPIHelper.Instance.DlssDPresetOptions);
                            if (game.DlssDPreset is null)
                            {
                                // If it was never set, ensure it goes to default.
                                SelectedDlssDPreset = DlssDPresetOptions.FirstOrDefault(x => x.Value == 0);
                            }
                            else
                            {
                                SelectedDlssDPreset = DlssDPresetOptions.FirstOrDefault(x => x.Value == game.DlssDPreset);
                            }
                        }
                    }


                    if (Game.CurrentDLSS_G is not null)
                    {
                        var gameDLSSGPresetResult = NVAPIHelper.Instance.GetGameDLSSGPreset(game);
                        if (gameDLSSGPresetResult.Success)
                        {
                            CanSelectDlssGPreset = true;

                            game.DlssGPreset = gameDLSSGPresetResult.Result;
                            DlssGPresetOptions.AddRange(NVAPIHelper.Instance.DlssGPresetOptions);
                            if (game.DlssGPreset is null)
                            {
                                // If it was never set, ensure it goes to default.
                                SelectedDlssGPreset = DlssGPresetOptions.FirstOrDefault(x => x.Value == 0);
                            }
                            else
                            {
                                SelectedDlssGPreset = DlssGPresetOptions.FirstOrDefault(x => x.Value == game.DlssGPreset);
                            }
                        }
                    }
                }
            }
        }

        if (CanSelectDlssPreset == false)
        {
            var disabledPresetOption = new PresetOption(ResourceHelper.GetString("General_NotSupported"), 0);
            DlssPresetOptions.Add(disabledPresetOption);
            SelectedDlssPreset = disabledPresetOption;
        }

        if (CanSelectDlssDPreset == false)
        {
            var disabledPresetOption = new PresetOption(ResourceHelper.GetString("General_NotSupported"), 0);
            DlssDPresetOptions.Add(disabledPresetOption);
            SelectedDlssDPreset = disabledPresetOption;
        }

        if (CanSelectDlssGPreset == false)
        {
            var disabledPresetOption = new PresetOption(ResourceHelper.GetString("General_NotSupported"), 0);
            DlssGPresetOptions.Add(disabledPresetOption);
            SelectedDlssGPreset = disabledPresetOption;
        }
    }

    partial void OnSelectedDlssPresetChanging(PresetOption? value)
    {
        _previousDlssPreset = SelectedDlssPreset;
    }

    partial void OnSelectedDlssDPresetChanging(PresetOption? value)
    {
        _previousDlssDPreset = SelectedDlssDPreset;
    }

    partial void OnSelectedDlssGPresetChanging(PresetOption? value)
    {
        _previousDlssGPreset = SelectedDlssGPreset;
    }


    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SelectedDlssPreset))
        {
            if (CanSelectDlssPreset == true && SelectedDlssPreset is not null && SelectedDlssPreset.Value != Game.DlssPreset)
            {
                var result = NVAPIHelper.Instance.SetGameDLSSPreset(Game, SelectedDlssPreset.Value);
                if (result.Success == false)
                {
                    if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
                    {
                        gameControl.DispatcherQueue.TryEnqueue(() =>
                        {
                            SelectedDlssPreset = _previousDlssPreset;
                        });
                        _ = NVAPIHelper.Instance.DisplayNVAPIErrorAsync(gameControl.XamlRoot);
                    }
                }
            }
        }
        else if (e.PropertyName == nameof(SelectedDlssDPreset))
        {
            if (CanSelectDlssDPreset == true && SelectedDlssDPreset is not null && SelectedDlssDPreset.Value != Game.DlssDPreset)
            {
                var result = NVAPIHelper.Instance.SetGameDLSSDPreset(Game, SelectedDlssDPreset.Value);
                if (result.Success == false)
                {
                    if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
                    {
                        gameControl.DispatcherQueue.TryEnqueue(() =>
                        {
                            SelectedDlssDPreset = _previousDlssDPreset;
                        });
                        _ = NVAPIHelper.Instance.DisplayNVAPIErrorAsync(gameControl.XamlRoot);
                    }
                }
            }
        }
        else if (e.PropertyName == nameof(SelectedDlssGPreset))
        {
            if (CanSelectDlssGPreset == true && SelectedDlssGPreset is not null && SelectedDlssGPreset.Value != Game.DlssGPreset)
            {
                var result = NVAPIHelper.Instance.SetGameDLSSGPreset(Game, SelectedDlssGPreset.Value);
                if (result.Success == false)
                {
                    if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
                    {
                        gameControl.DispatcherQueue.TryEnqueue(() =>
                        {
                            SelectedDlssGPreset = _previousDlssGPreset;
                        });
                        _ = NVAPIHelper.Instance.DisplayNVAPIErrorAsync(gameControl.XamlRoot);
                    }
                }
            }
        }
    }

    [RelayCommand]
    async Task OpenInstallPathAsync()
    {
        try
        {
            if (Directory.Exists(Game.InstallPath))
            {
                Process.Start("explorer.exe", Game.InstallPath);
            }
            else
            {
                throw new Exception(ResourceHelper.GetFormattedResourceTemplate("GamePage_CouldNotFindGameInstallPathTemplate", Game.InstallPath));
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);

            if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
            {
                var dialog = new EasyContentDialog(gameControl.XamlRoot)
                {
                    Title = ResourceHelper.GetString("General_Error"),
                    CloseButtonText = ResourceHelper.GetString("General_Okay"),
                    Content = err.Message,
                };
                await dialog.ShowAsync();
            }
        }
    }

    [RelayCommand]
    Task EnableLatestDlssAsync() => DeployPresetAsync(RuntimeIntegrationPreset.LatestDlssOnly, "Enable latest DLSS");

    [RelayCommand]
    Task EnableFrameGenerationAsync() => DeployPresetAsync(RuntimeIntegrationPreset.DlssWithFrameGeneration, "Enable DLSS + Frame Generation");
    [RelayCommand]
    Task EnableNativeDlssGAsync() => DeployPresetAsync(RuntimeIntegrationPreset.NativeDlssG, "Enable Native DLSS 3 Hardware Frame Generation");

    [RelayCommand]
    async Task ActivateTurboBoostAsync()
    {
        try
        {
            var iniPath = GetDeployedIniPath();
            if (iniPath is null || !File.Exists(iniPath))
            {
                var success = await DeployPresetAsync(RuntimeIntegrationPreset.TurboBoost, "⚡ Activate XTR Turbo Boost");
                if (!success)
                    return;

                return;
            }

            var root = Path.GetDirectoryName(iniPath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            var settings = new List<(string Section, string Key, string Value)>();
            settings.Add(("Upscalers", "Dx12Upscaler", "dlss"));
            settings.Add(("Upscalers", "Dx11Upscaler", "dlss"));
            settings.Add(("Upscalers", "VulkanUpscaler", "dlss"));

            settings.Add(("DLSS", "RenderPresetOverride", "true"));
            settings.Add(("DLSS", "RenderPresetForAll", "5"));

            bool isForza = DeploymentPlanner.IsForzaOrVirtualTextureTitle(Game.Title);
            if (!isForza)
            {
                settings.Add(("Anisotropy", "AnisotropyOverride", "16"));
                settings.Add(("Anisotropy", "ModifyComparison", "true"));
                settings.Add(("Anisotropy", "ModifyMinMax", "true"));
                settings.Add(("Anisotropy", "SkipPointFilter", "true"));

                settings.Add(("Mipmap", "MipmapBiasOverride", "-0.5"));
                settings.Add(("Mipmap", "MipmapBiasFixedOverride", "true"));
            }

            settings.Add(("Sharpness", "OverrideSharpness", "true"));
            settings.Add(("Sharpness", "Sharpness", "0.35"));

            settings.Add(("Spoofing", "StreamlineSpoofing", "true"));
            settings.Add(("Spoofing", "SpoofHAGS", "true"));

            settings.Add(("Menu", "OverlayMenu", "true"));
            settings.Add(("Menu", "ShowFps", "true"));
            settings.Add(("Menu", "FpsOverlayType", "2"));
            settings.Add(("Menu", "DisableSplash", "true"));
            settings.Add(("Menu", "ShortcutKey", "-1"));

            if (string.Equals(ActiveFrameGenMode, "Native DLSS 3 Hardware FG", StringComparison.OrdinalIgnoreCase) ||
                (Game.CurrentDLSS_G is not null && !string.Equals(ActiveFrameGenMode, "Universal FG Bridge (FSR 3.1)", StringComparison.OrdinalIgnoreCase)))
            {
                settings.Add(("FrameGen", "Enabled", "false"));
                settings.Add(("FrameGen", "FGInput", "nofg"));
                settings.Add(("FrameGen", "FGOutput", "nofg"));
                ActiveFrameGenMode = "Native DLSS 3 Hardware FG";
            }
            else
            {
                settings.Add(("FrameGen", "Enabled", "true"));
                settings.Add(("FrameGen", "FGInput", "dlssg"));
                settings.Add(("FrameGen", "FGOutput", "fsrfg"));
                settings.Add(("FrameGen", "DrawUIOverFG", "true"));
                settings.Add(("FrameGen", "DisableUI", "false"));
                settings.Add(("FrameGen", "DisableHudless", "false"));
                settings.Add(("FrameGen", "ForceBorderless", "true"));

                settings.Add(("OptiFG", "HUDFix", isForza ? "false" : "true"));
                settings.Add(("OptiFG", "HUDFixExtended", isForza ? "false" : "true"));
                settings.Add(("OptiFG", "DisableHUDFix", isForza ? "true" : "false"));
                settings.Add(("OptiFG", "MakeDepthCopy", isForza ? "false" : "true"));
                settings.Add(("OptiFG", "MakeMVCopy", isForza ? "false" : "true"));
                settings.Add(("OptiFG", "HUDFixImmediate", isForza ? "false" : "true"));
                settings.Add(("OptiFG", "AlwaysTrackHeaps", isForza ? "false" : "true"));
                settings.Add(("OptiFG", "HUDFixRelaxedResolutionCheck", isForza ? "false" : "true"));
                settings.Add(("InitFlags", "DisableReactiveMask", "false"));
                settings.Add(("FSR", "UseReactiveMaskForTransparency", "true"));
                settings.Add(("FSR", "DlssReactiveMaskBias", "0.45"));
                settings.Add(("FSR", "ReactiveScale", "2.0"));
                settings.Add(("FSR", "AccAddPerFrame", "0.05"));
                settings.Add(("FSR", "MinDisOccAcc", "-0.5"));

                settings.Add(("FSRFG", "FramePacingTuning", "true"));
                settings.Add(("FSRFG", "FPTSafetyMarginInMs", "0.01"));
                settings.Add(("FSRFG", "FPTVarianceFactor", "0.3"));
                settings.Add(("FSRFG", "UseMutexForSwapchain", "true"));
                ActiveFrameGenMode = "Universal FG Bridge (FSR 3.1)";
            }

            await RuntimeConfigService.UpdateSettingsAsync(root, settings);

            if (Game.RuntimeProfile is not null)
            {
                Game.RuntimeProfile.Options["Preset"] = "TurboBoost";
                Game.RuntimeProfile.FrameGenerationEnabled = (ActiveFrameGenMode == "Native DLSS 3 Hardware FG") ? "false" : "true";
                await Game.SaveRuntimeProfileAsync(Game.RuntimeProfile);
            }

            InitializeDeployedSettings();
            RefreshDiagnostics();
            await ShowRuntimeDialogAsync("⚡ XTR Turbo Boost Activated", "Engine optimizations successfully applied:\n\n• DLSS Neural Model: Preset E (Ghosting Shield)\n• Texture Filtering: 16x Anisotropic + Mipmap LOD Bias (-0.5)\n• Low-Latency Mutex Frame Pacing\n• AI Edge Sharpening: RCAS 35%\n• In-Game Real-Time Telemetry: Active");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to activate Turbo Boost");
        }
    }

    [RelayCommand]
    Task QuickSwapDlss37Async() => QuickSwapDlssCoreAsync("3.7");

    [RelayCommand]
    Task QuickSwapDlss35Async() => QuickSwapDlssCoreAsync("3.5");

    [RelayCommand]
    Task QuickSwapDlss25Async() => QuickSwapDlssCoreAsync("2.5");

    [RelayCommand]
    Task OpenDlssPickerAsync() => ChangeRecordAsync(GameAssetType.DLSS);

    private async Task QuickSwapDlssCoreAsync(string targetPrefix)
    {
        try
        {
            var match = DLLManager.Instance.DLSSRecords
                .Where(r => r.Version.StartsWith(targetPrefix) && r.LocalRecord?.IsDownloaded == true)
                .OrderByDescending(r => r.VersionNumber)
                .FirstOrDefault();

            if (match is not null)
            {
                var updateResult = await Game.UpdateDllAsync(match);
                if (updateResult.Success)
                {
                    OnPropertyChanged(nameof(RuntimeHealthSummaryText));
                    OnPropertyChanged(nameof(PrimaryActionExplanationText));
                    await ShowRuntimeDialogAsync("DLSS Swapped", $"Swapped game to DLSS {match.Version} successfully.");
                    return;
                }
            }

            await ChangeRecordAsync(GameAssetType.DLSS);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to quick swap DLSS to {Target}", targetPrefix);
        }
    }

    private void InitializeDeployedSettings()
    {
        try
        {
            var iniPath = GetDeployedIniPath();
            if (iniPath is null || !File.Exists(iniPath))
                return;

            var content = File.ReadAllText(iniPath);
            _isLoadingSettings = true;

            var hudFix = RuntimeConfigService.ReadIniValue(content, "OptiFG", "HUDFix");
            if (!string.IsNullOrEmpty(hudFix))
            {
                AntiGhostingShieldEnabled = !string.Equals(hudFix, "false", StringComparison.OrdinalIgnoreCase);
            }

            var showFps = RuntimeConfigService.ReadIniValue(content, "Menu", "ShowFps");
            if (!string.IsNullOrEmpty(showFps))
            {
                InGameHudOverlayEnabled = string.Equals(showFps, "true", StringComparison.OrdinalIgnoreCase);
            }

            var shortcutKey = RuntimeConfigService.ReadIniValue(content, "Menu", "ShortcutKey");
            if (!string.IsNullOrEmpty(shortcutKey))
            {
                InGameDevMenuEnabled = !string.Equals(shortcutKey, "-1", StringComparison.OrdinalIgnoreCase);
            }

            var spoofing = RuntimeConfigService.ReadIniValue(content, "Spoofing", "StreamlineSpoofing");
            if (!string.IsNullOrEmpty(spoofing))
            {
                StreamlineSpoofingEnabled = !string.Equals(spoofing, "false", StringComparison.OrdinalIgnoreCase);
            }

            var fgOutput = RuntimeConfigService.ReadIniValue(content, "FrameGen", "FGOutput");
            var fgEnabled = RuntimeConfigService.ReadIniValue(content, "FrameGen", "Enabled");
            if (string.Equals(fgEnabled, "false", StringComparison.OrdinalIgnoreCase))
            {
                ActiveFrameGenMode = "Upscaling Only (No FG)";
            }
            else if (string.Equals(fgOutput, "nofg", StringComparison.OrdinalIgnoreCase) || string.Equals(fgOutput, "dlssg", StringComparison.OrdinalIgnoreCase))
            {
                ActiveFrameGenMode = "Native DLSS 3 Hardware FG";
            }
            else if (string.Equals(fgOutput, "nukems", StringComparison.OrdinalIgnoreCase))
            {
                ActiveFrameGenMode = "Nukem9 Streamline Bridge (FSR 3.1)";
            }
            else if (string.Equals(fgOutput, "xefg", StringComparison.OrdinalIgnoreCase))
            {
                ActiveFrameGenMode = "Intel XeFG Universal Bridge";
            }
            else
            {
                ActiveFrameGenMode = "Universal FG Bridge (FSR 3.1)";
            }

            // Read DLSS Preset Override
            var presetOverride = RuntimeConfigService.ReadIniValue(content, "DLSS", "RenderPresetOverride");
            var presetCode = RuntimeConfigService.ReadIniValue(content, "DLSS", "RenderPresetForAll");
            if (string.Equals(presetOverride, "true", StringComparison.OrdinalIgnoreCase))
            {
                SelectedEngineNeuralPreset = presetCode switch
                {
                    "5" => "Preset E (Anti-Ghosting Champion)",
                    "6" => "Preset F (Fast Motion & Reduced Shimmer)",
                    "3" => "Preset C (Low Ghosting / High Stability)",
                    "4" => "Preset D (Classic DLSS 2.5 Profile)",
                    "10" => "Preset J (Transformer Model)",
                    _ => "Default (Game Engine Default)"
                };
            }
            else
            {
                SelectedEngineNeuralPreset = "Default (Game Engine Default)";
            }

            // Read Sharpness
            var sharpnessStr = RuntimeConfigService.ReadIniValue(content, "Sharpness", "Sharpness");
            if (double.TryParse(sharpnessStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sharpnessVal))
            {
                SharpnessPercentage = Math.Round(Math.Clamp(sharpnessVal * 100.0, 0.0, 100.0));
            }

            // Read Overlay Position
            var posStr = RuntimeConfigService.ReadIniValue(content, "Menu", "FpsOverlayPos");
            SelectedOverlayPosition = posStr switch
            {
                "1" => "Top Right",
                "2" => "Bottom Left",
                "3" => "Bottom Right",
                _ => "Top Left"
            };

            // Read Framerate Limit
            var limitStr = RuntimeConfigService.ReadIniValue(content, "Framerate", "FramerateLimit");
            if (double.TryParse(limitStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var limitVal) && limitVal > 0)
            {
                SelectedFramerateLimit = $"{(int)limitVal} FPS";
            }
            else
            {
                SelectedFramerateLimit = "Off (Unlimited)";
            }

            // Read Turbo Boost state
            var aniso = RuntimeConfigService.ReadIniValue(content, "Anisotropy", "AnisotropyOverride");
            var renderPreset = RuntimeConfigService.ReadIniValue(content, "DLSS", "RenderPresetForAll");
            IsTurboBoostActive = string.Equals(aniso, "16", StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(renderPreset, "5", StringComparison.OrdinalIgnoreCase);

            if (Game.RuntimeProfile?.Options is { } options)
            {
                if (IsTurboBoostActive)
                {
                    options["Preset"] = "TurboBoost";
                }
                else if (options.TryGetPropertyValue("Preset", out var presetNode) &&
                         string.Equals(presetNode?.ToString(), "TurboBoost", StringComparison.OrdinalIgnoreCase))
                {
                    options.Remove("Preset");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize deployed runtime settings");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    [RelayCommand]
    async Task SwitchFrameGenModeAsync(string mode)
    {
        try
        {
            var root = Game.RuntimeManifest?.InstallRoot ?? Game.InstallPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            var settings = new List<(string Section, string Key, string Value)>();
            if (mode.Contains("Native", StringComparison.OrdinalIgnoreCase))
            {
                settings.Add(("FrameGen", "Enabled", "false"));
                settings.Add(("FrameGen", "FGInput", "nofg"));
                settings.Add(("FrameGen", "FGOutput", "nofg"));
                settings.Add(("Upscalers", "Dx12Upscaler", "dlss"));
                settings.Add(("Spoofing", "StreamlineSpoofing", "true"));
                settings.Add(("Spoofing", "SpoofHAGS", "true"));
                ActiveFrameGenMode = "Native DLSS 3 Hardware FG";
            }
            else if (mode.Contains("Nukem", StringComparison.OrdinalIgnoreCase))
            {
                settings.Add(("FrameGen", "Enabled", "true"));
                settings.Add(("FrameGen", "FGInput", "dlssg"));
                settings.Add(("FrameGen", "FGOutput", "nukems"));
                settings.Add(("Upscalers", "Dx12Upscaler", "dlss"));
                settings.Add(("Spoofing", "StreamlineSpoofing", "true"));
                settings.Add(("Spoofing", "SpoofHAGS", "true"));
                ActiveFrameGenMode = "Nukem9 Streamline Bridge (FSR 3.1)";
            }
            else if (mode.Contains("XeFG", StringComparison.OrdinalIgnoreCase) || mode.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                settings.Add(("FrameGen", "Enabled", "true"));
                settings.Add(("FrameGen", "FGInput", "dlssg"));
                settings.Add(("FrameGen", "FGOutput", "xefg"));
                settings.Add(("Upscalers", "Dx12Upscaler", "dlss"));
                settings.Add(("Spoofing", "StreamlineSpoofing", "true"));
                settings.Add(("Spoofing", "SpoofHAGS", "true"));
                ActiveFrameGenMode = "Intel XeFG Universal Bridge";
            }
            else if (mode.Contains("Universal", StringComparison.OrdinalIgnoreCase) || mode.Contains("FSR", StringComparison.OrdinalIgnoreCase))
            {
                settings.Add(("FrameGen", "Enabled", "true"));
                settings.Add(("FrameGen", "FGInput", "dlssg"));
                settings.Add(("FrameGen", "FGOutput", "fsrfg"));
                settings.Add(("Upscalers", "Dx12Upscaler", "dlss"));
                settings.Add(("Spoofing", "StreamlineSpoofing", "true"));
                settings.Add(("Spoofing", "SpoofHAGS", "true"));
                ActiveFrameGenMode = "Universal FG Bridge (FSR 3.1)";
            }
            else
            {
                settings.Add(("FrameGen", "Enabled", "false"));
                settings.Add(("FrameGen", "FGOutput", "nofg"));
                ActiveFrameGenMode = "Upscaling Only (No FG)";
            }

            await RuntimeConfigService.UpdateSettingsAsync(root, settings);
            RefreshDiagnostics();
            await ShowRuntimeDialogAsync("Frame Generation Mode Updated", $"Active mode is now: {ActiveFrameGenMode}. Restart the game to apply changes.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to switch frame gen mode to {Mode}", mode);
        }
    }

    private static readonly SemaphoreSlim ConfigLock = new(1, 1);

    private void ApplyAntiGhostingSetting(bool enabled)
    {
        var settings = new List<(string Section, string Key, string Value)>();
        bool isForza = DeploymentPlanner.IsForzaOrVirtualTextureTitle(Game.Title);
        if (enabled)
        {
            // [InitFlags]
            settings.Add(("InitFlags", "DisableReactiveMask", "false"));

            // [FrameGen]
            settings.Add(("FrameGen", "DrawUIOverFG", "true"));
            settings.Add(("FrameGen", "DisableUI", "false"));
            settings.Add(("FrameGen", "DisableHudless", "false"));

            // [FSR]
            settings.Add(("FSR", "UseReactiveMaskForTransparency", "true"));
            settings.Add(("FSR", "DlssReactiveMaskBias", "0.45"));
            settings.Add(("FSR", "ReactiveScale", "2.0"));
            settings.Add(("FSR", "AccAddPerFrame", "0.05"));
            settings.Add(("FSR", "MinDisOccAcc", "-0.5"));

            // [OptiFG]
            settings.Add(("OptiFG", "HUDFix", isForza ? "false" : "true"));
            settings.Add(("OptiFG", "HUDFixExtended", isForza ? "false" : "true"));
            settings.Add(("OptiFG", "DisableHUDFix", isForza ? "true" : "false"));
            settings.Add(("OptiFG", "MakeDepthCopy", isForza ? "false" : "true"));
            settings.Add(("OptiFG", "MakeMVCopy", isForza ? "false" : "true"));
            settings.Add(("OptiFG", "HUDFixImmediate", isForza ? "false" : "true"));
            settings.Add(("OptiFG", "AlwaysTrackHeaps", isForza ? "false" : "true"));
            settings.Add(("OptiFG", "HUDFixRelaxedResolutionCheck", isForza ? "false" : "true"));
        }
        else
        {
            // [InitFlags]
            settings.Add(("InitFlags", "DisableReactiveMask", "true"));

            // [FrameGen]
            settings.Add(("FrameGen", "DrawUIOverFG", "false"));
            settings.Add(("FrameGen", "DisableUI", "true"));
            settings.Add(("FrameGen", "DisableHudless", "false"));

            // [FSR]
            settings.Add(("FSR", "UseReactiveMaskForTransparency", "false"));
            settings.Add(("FSR", "DlssReactiveMaskBias", "0.45"));
            settings.Add(("FSR", "ReactiveScale", "1.0"));
            settings.Add(("FSR", "AccAddPerFrame", "0.333"));
            settings.Add(("FSR", "MinDisOccAcc", "-0.333"));

            // [OptiFG]
            settings.Add(("OptiFG", "HUDFix", "false"));
            settings.Add(("OptiFG", "HUDFixExtended", "false"));
            settings.Add(("OptiFG", "DisableHUDFix", "true"));
            settings.Add(("OptiFG", "MakeDepthCopy", "false"));
            settings.Add(("OptiFG", "MakeMVCopy", "false"));
            settings.Add(("OptiFG", "HUDFixImmediate", "false"));
            settings.Add(("OptiFG", "AlwaysTrackHeaps", "false"));
            settings.Add(("OptiFG", "HUDFixRelaxedResolutionCheck", "false"));
        }

        UpdateOptiScalerConfigSettings(settings);
    }

    private void ApplyHudOverlaySetting(bool enabled)
    {
        if (enabled)
        {
            UpdateOptiScalerConfigSettings(new[]
            {
                ("Menu", "ShowFps", "true"),
                ("Menu", "FpsOverlayType", "2"),
                ("Menu", "OverlayMenu", "true"),
                ("Menu", "FpsShortcutKey", "0x21"),
                ("Menu", "FpsCycleShortcutKey", "0x22"),
                ("Menu", "FGShortcutKey", "0x23"),
                ("Menu", "FpsOverlayHorizontal", "true")
            });
        }
        else
        {
            UpdateOptiScalerConfigSetting("Menu", "ShowFps", "false");
        }
    }

    private void ApplyDevMenuSetting(bool enabled)
    {
        UpdateOptiScalerConfigSetting("Menu", "ShortcutKey", enabled ? "0x2D" : "-1");
    }

    private void ApplyStreamlineSpoofingSetting(bool enabled)
    {
        UpdateOptiScalerConfigSetting("Spoofing", "StreamlineSpoofing", enabled ? "true" : "false");
    }

    private void ApplyEngineNeuralPresetSetting(string preset)
    {
        var (overrideEnabled, presetVal) = preset switch
        {
            var p when p.Contains("Preset E", StringComparison.OrdinalIgnoreCase) => ("true", "5"),
            var p when p.Contains("Preset F", StringComparison.OrdinalIgnoreCase) => ("true", "6"),
            var p when p.Contains("Preset C", StringComparison.OrdinalIgnoreCase) => ("true", "3"),
            var p when p.Contains("Preset D", StringComparison.OrdinalIgnoreCase) => ("true", "4"),
            var p when p.Contains("Preset J", StringComparison.OrdinalIgnoreCase) => ("true", "10"),
            _ => ("false", "0")
        };

        UpdateOptiScalerConfigSettings(new[]
        {
            ("DLSS", "RenderPresetOverride", overrideEnabled),
            ("DLSS", "RenderPresetForAll", presetVal)
        });
    }

    private void ApplySharpnessSetting(double percentage)
    {
        var sharpnessVal = Math.Clamp(percentage / 100.0, 0.0, 1.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        UpdateOptiScalerConfigSettings(new[]
        {
            ("Sharpness", "OverrideSharpness", "true"),
            ("Sharpness", "Sharpness", sharpnessVal)
        });
    }

    private void ApplyOverlayPositionSetting(string position)
    {
        var posVal = position switch
        {
            "Top Right" => "1",
            "Bottom Left" => "2",
            "Bottom Right" => "3",
            _ => "0"
        };
        UpdateOptiScalerConfigSetting("Menu", "FpsOverlayPos", posVal);
    }

    private void ApplyFramerateLimitSetting(string limit)
    {
        var limitVal = limit switch
        {
            "60 FPS" => "60.0",
            "120 FPS" => "120.0",
            "144 FPS" => "144.0",
            "165 FPS" => "165.0",
            "240 FPS" => "240.0",
            _ => "0.0"
        };
        UpdateOptiScalerConfigSetting("Framerate", "FramerateLimit", limitVal);
    }

    private void UpdateOptiScalerConfigSetting(string section, string key, string value)
    {
        UpdateOptiScalerConfigSettings(new[] { (section, key, value) });
    }

    private void UpdateOptiScalerConfigSettings(IEnumerable<(string Section, string Key, string Value)> settings)
    {
        _ = Task.Run(async () =>
        {
            await ConfigLock.WaitAsync();
            try
            {
                var iniPath = GetDeployedIniPath();
                if (iniPath is null || !File.Exists(iniPath)) return;

                var installRoot = Path.GetDirectoryName(iniPath);
                if (string.IsNullOrEmpty(installRoot) || !Directory.Exists(installRoot)) return;

                await RuntimeConfigService.UpdateSettingsAsync(installRoot, settings);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to write OptiScaler config settings");
            }
            finally
            {
                ConfigLock.Release();
            }
        });
    }

    private string? GetDeployedIniPath()
    {
        if (Game.RuntimeManifest?.InstallRoot is { } root && Directory.Exists(root))
        {
            var p = Path.Combine(root, "OptiScaler.ini");
            if (File.Exists(p)) return p;
        }
        if (!string.IsNullOrEmpty(Game.InstallPath) && Directory.Exists(Game.InstallPath))
        {
            var p = Path.Combine(Game.InstallPath, "OptiScaler.ini");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [RelayCommand]
    void RefreshDiagnostics()
    {
        OnPropertyChanged(nameof(RuntimeHealthSummaryText));
        OnPropertyChanged(nameof(PrimaryActionExplanationText));
        OnPropertyChanged(nameof(FrameGenerationStatusText));
        OnPropertyChanged(nameof(RuntimeStatusText));
    }

    private async Task<bool> DeployPresetAsync(RuntimeIntegrationPreset preset, string dialogTitle)
    {
        if (!gameControlWeakReference.TryGetTarget(out var gameControl))
            return false;

        var packagePath = RuntimeInstallService.GetBundledPackagePath();
        if (packagePath is null)
        {
            await ShowRuntimeDialogAsync("Bundled runtime unavailable", "This XTR build does not include the baseline OptiScaler runtime. Choose a custom package under Advanced.");
            return false;
        }

        var pack = await RuntimePack.LoadFromDirectoryAsync(packagePath);
        if (pack is null)
        {
            await ShowRuntimeDialogAsync("Runtime load error", "Could not load the bundled runtime package.");
            return false;
        }

        var plan = await DeploymentPlanner.CreatePlanAsync(
            Game.ID,
            Game.Title,
            Game.InstallPath,
            pack,
            preset,
            SelectedRuntimeProxy);

        if (!plan.CanExecute)
        {
            await ShowRuntimeDialogAsync(plan.Headline, plan.Summary);
            return false;
        }

        var planPanel = new StackPanel { Spacing = 12 };
        var summaryText = new TextBlock
        {
            Text = plan.Summary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        planPanel.Children.Add(summaryText);

        var preflightInfo = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = (Microsoft.UI.Xaml.Media.Brush)App.CurrentApp.Resources["XtrElevatedBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)App.CurrentApp.Resources["XtrStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = $"Preflight: {plan.PreflightResult.Headline} | {plan.PlannedCopies.Count} files to deploy | {plan.PlannedBackups.Count} backups planned\nProxy: {plan.SelectedProxy} ({plan.PreflightResult.DetectedApi})",
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)App.CurrentApp.Resources["XtrSecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            }
        };
        planPanel.Children.Add(preflightInfo);

        var detailsExpander = new Expander
        {
            Header = "Review dry-run plan details",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock
            {
                Text = string.Join(Environment.NewLine, plan.PlannedCopies.Select(c => $"| {c.RelativePath}{(c.RequiresBackup ? " (backs up original)" : "")}")),
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)App.CurrentApp.Resources["XtrSecondaryTextBrush"],
                TextWrapping = TextWrapping.Wrap
            }
        };
        planPanel.Children.Add(detailsExpander);

        var dialog = new EasyContentDialog(gameControl.XamlRoot)
        {
            Title = dialogTitle,
            PrimaryButtonText = "Install runtime",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = planPanel
        };

        var dialogResult = await dialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary)
        {
            return false;
        }

        try
        {
            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
            await Game.SaveRuntimeManifestAsync(manifest);
            var profile = await RuntimeIntegrationProfileStore.LoadAsync(Game.ID);
            if (profile is not null)
            {
                await Game.SaveRuntimeProfileAsync(profile);
            }
            InitializeDeployedSettings();
            RefreshDiagnostics();
            await ShowRuntimeDialogAsync("Runtime installed", $"{pack.DisplayName} is active for {Game.Title}. Reversible backups recorded.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            await ShowRuntimeDialogAsync("Install failed", ex.Message);
            return false;
        }
    }
    [RelayCommand]
    async Task InstallRuntimeAsync()
    {
        var packagePath = RuntimeInstallService.GetBundledPackagePath();
        if (packagePath is null)
        {
            await ShowRuntimeDialogAsync("Bundled runtime unavailable", "This XTR build does not include the OptiScaler runtime. Choose a custom extracted package instead.");
            return;
        }

        await InstallRuntimePackageAsync(packagePath);
    }

    [RelayCommand]
    async Task InstallCustomRuntimeAsync()
    {
        if (!gameControlWeakReference.TryGetTarget(out var gameControl))
            return;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);
        var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var packagePath = FileSystemHelper.OpenFolder(windowHandle, downloadsPath, "Select package");
        if (string.IsNullOrWhiteSpace(packagePath))
            return;

        await InstallRuntimePackageAsync(packagePath);
    }

    private async Task InstallRuntimePackageAsync(string packagePath)
    {
        if (!gameControlWeakReference.TryGetTarget(out var gameControl))
            return;

        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);
            var targetPath = FileSystemHelper.OpenFolder(windowHandle, Game.InstallPath, "Choose game executable folder");
            if (string.IsNullOrWhiteSpace(targetPath))
                return;

            var manifest = await RuntimeInstallService.InstallAsync(
                Game.ID,
                targetPath,
                packagePath,
                SelectedRuntimeProxy);

            try
            {
                var profile = new RuntimeIntegrationProfile
                {
                    GameId = Game.ID,
                    RuntimeId = manifest.RuntimeId,
                    RuntimeVersion = manifest.RuntimeVersion,
                    ProxyFileName = manifest.ProxyFileName ?? SelectedRuntimeProxy,
                    SupportState = "experimental",
                    SupportReason = "Runtime installed; validate this game and adapter combination before relying on frame generation.",
                };
                await RuntimeConfigService.ApplyProfileAsync(manifest.InstallRoot, profile);
                await Game.SaveRuntimeManifestAsync(manifest);
                await Game.SaveRuntimeProfileAsync(profile);
            }
            catch
            {
                await RuntimeInstallService.RestoreAsync(Game.ID, manifest.InstallRoot);
                App.CurrentApp.RunOnUIThread(() => Game.RuntimeManifest = null);
                throw;
            }

            await ShowRuntimeDialogAsync("Runtime installed", $"OptiScaler {manifest.RuntimeVersion} is installed for this game. XTR recorded backups for every file it replaced.");
        }
        catch (Exception error)
        {
            Logger.Error(error);
            await ShowRuntimeDialogAsync("Runtime install failed", error.Message);
        }
    }

    [RelayCommand]
    async Task RestoreRuntimeAsync()
    {
        try
        {
            var installRoot = Game.RuntimeManifest?.InstallRoot
                ?? throw new InvalidOperationException("XTR has no runtime install location recorded for this game.");
            await RuntimeInstallService.RestoreAsync(Game.ID, installRoot);
            App.CurrentApp.RunOnUIThread(() => Game.RuntimeManifest = null);
            await Game.RemoveRuntimeProfileAsync();

            _isLoadingSettings = true;
            IsTurboBoostActive = false;
            AntiGhostingShieldEnabled = false;
            InGameHudOverlayEnabled = false;
            StreamlineSpoofingEnabled = false;
            SelectedEngineNeuralPreset = "Default (Game Engine Default)";
            ActiveFrameGenMode = "Upscaling Only (No FG)";
            _isLoadingSettings = false;

            RefreshDiagnostics();
            await ShowRuntimeDialogAsync("Runtime restored", "XTR removed its runtime files and restored the originals it had backed up.");
        }
        catch (Exception error)
        {
            Logger.Error(error);
            await ShowRuntimeDialogAsync("Restore stopped", error.Message);
        }
    }

    private async Task ShowRuntimeDialogAsync(string title, string message)
    {
        if (!gameControlWeakReference.TryGetTarget(out var gameControl))
            return;

        await App.CurrentApp.RunOnUIThreadAsync(async () =>
        {
            var dialog = new EasyContentDialog(gameControl.XamlRoot)
            {
                Title = title,
                CloseButtonText = ResourceHelper.GetString("General_Okay"),
                Content = message,
            };
            await dialog.ShowAsync();
        });
    }

    [RelayCommand]
    async Task LaunchAsync()
    {
        if (GameManager.Instance.CanLaunchGame(Game))
        {
            await GameManager.Instance.LaunchGameAsync(Game);
        }
        else
        {
            if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
            {
                var dialog = new EasyContentDialog(gameControl.XamlRoot)
                {
                    Title = ResourceHelper.GetString("General_Error"),
                    CloseButtonText = ResourceHelper.GetString("General_Okay"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = ResourceHelper.GetFormattedResourceTemplate("GamePage_CantLaunchFromLibraryTemplate", Game.GameLibrary),
                };
                await dialog.ShowAsync();
            }
        }
    }

    [RelayCommand]
    async Task EditNotesAsync()
    {
        if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
        {
            var textBox = new TextBox()
            {
                MinHeight = 400,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
            };
            // This needs to be set after AcceptsReturn otherwise it will strip out the \r
            textBox.Text = Game.Notes;

            var dialog = new EasyContentDialog(gameControl.XamlRoot)
            {
                Title = $"{ResourceHelper.GetString("GamePage_Notes")} - {Game.Title}",
                PrimaryButtonText = ResourceHelper.GetString("General_Save"),
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = textBox,
            };
            dialog.Resources["ContentDialogMinWidth"] = 700;
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                Game.Notes = textBox.Text ?? string.Empty;
                await Game.SaveToDatabaseAsync();
            }
        }
    }

    [RelayCommand]
    async Task ViewHistoryAsync()
    {
        if (gameControlWeakReference.TryGetTarget(out var control))
        {
            var dialog = new EasyContentDialog(control.XamlRoot)
            {
                Title = $"{ResourceHelper.GetFormattedResourceTemplate("GamePage_History")} - {Game.Title}",
                PrimaryButtonText = ResourceHelper.GetString("General_Close"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new GameHistoryControl(Game),
            };
            dialog.Resources["ContentDialogMinWidth"] = 800;

            await dialog.ShowAsync();
        }
    }

    [RelayCommand]
    async Task AddCoverImageAsync()
    {
        if (Game.CoverImage == Game.ExpectedCustomCoverImage)
        {
            await Game.PromptToRemoveCustomCover();
            return;
        }

        Game.PromptToBrowseCustomCover();
    }

    [RelayCommand]
    void Close()
    {
        if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
        {
            gameControl.Hide();
        }
    }

    [RelayCommand]
    async Task RemoveAsync()
    {
        if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
        {
            // This should never happen
            if (IsManuallyAdded == false)
            {
                var cantDeleteDialog = new EasyContentDialog(gameControl.XamlRoot)
                {
                    Title = ResourceHelper.GetString("General_Error"),
                    CloseButtonText = ResourceHelper.GetString("General_Okay"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = ResourceHelper.GetString("GamePage_ManuallyAdded_CantBeRemoved"),
                };
                await cantDeleteDialog.ShowAsync();
                return;
            }



            // This needs to be set after AcceptsReturn otherwise it will strip out the \r
            var dialog = new EasyContentDialog(gameControl.XamlRoot)
            {
                Title = $"{ResourceHelper.GetString("General_Remove")} {Game.Title}?",
                PrimaryButtonText = ResourceHelper.GetString("General_Remove"),
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = ResourceHelper.GetFormattedResourceTemplate("GamePage_ManuallyAdded_RemoveGameTemplate", Game.Title),
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await Game.DeleteAsync();
                GameManager.Instance.RemoveGame(Game);
                gameControl.Hide();
            }
        }
    }

    [RelayCommand]
    async Task FavouriteAsync()
    {
        Game.IsFavourite = !Game.IsFavourite;
        await Game.SaveToDatabaseAsync();
    }

    [RelayCommand]
    async Task ChangeRecordAsync(GameAssetType gameAssetType)
    {
        if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
        {
            var dialog = new EasyContentDialog(gameControl.XamlRoot)
            {
                Title = ResourceHelper.GetFormattedResourceTemplate("GamePage_SelectDllTemplateTitle", DLLManager.Instance.GetAssetTypeName(gameAssetType)),
                PrimaryButtonText = ResourceHelper.GetString("General_Swap"),
                IsPrimaryButtonEnabled = false,
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };

            var dllPickerControl = new DLLPickerControl(gameControl, dialog, Game, gameAssetType);
            dialog.Content = dllPickerControl;
            await dialog.ShowAsync();
        }
    }

    [RelayCommand]
    async Task SaveTitleAsync()
    {
        Game.Title = GameTitle;
        await Game.SaveToDatabaseAsync();
        OnPropertyChanged(nameof(GameTitleHasChanged));
    }

    [RelayCommand]
    async Task MultipleDLLsFoundAsync(GameAssetType gameAssetType)
    {
        if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
        {
            var dialog = new EasyContentDialog(gameControl.XamlRoot)
            {
                Title = ResourceHelper.GetFormattedResourceTemplate("GamePage_MultipleDllsFoundTemplate", DLLManager.Instance.GetAssetTypeName(gameAssetType)),
                PrimaryButtonText = ResourceHelper.GetString("General_Okay"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new MultipleDLLsFoundControl(Game, gameAssetType),
            };

            await dialog.ShowAsync();
        }
    }

    [RelayCommand]
    async Task ReadyToPlayStateMoreInformationAsync()
    {
        await Launcher.LaunchUriAsync(new Uri("https://github.com/anmol150704/XTR-Unlocker#troubleshooting"));
    }

    [RelayCommand]
    async Task DLSSPresetInfoAsync()
    {
        if (gameControlWeakReference.TryGetTarget(out GameControl? gameControl))
        {
            var dialog = new EasyContentDialog(gameControl.XamlRoot)
            {
                Title = ResourceHelper.GetString("GamePage_DLSSPresetInfo_Title"),
                PrimaryButtonText = ResourceHelper.GetString("General_Okay"),
                SecondaryButtonText = ResourceHelper.GetString("GamePage_DLSSPresetInfo_OnScreenIndicator"),
                DefaultButton = ContentDialogButton.Primary,
                Content = ResourceHelper.GetString("GamePage_DLSSPresetInfo_Message"),
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                await Launcher.LaunchUriAsync(new Uri("https://github.com/beeradmoore/dlss-swapper/wiki/DLSS-Developer-Options#on-screen-indicator"));
            }
        }
    }

    TaskCompletionSource? _reloadGameTaskCompletionSource;

    [RelayCommand]
    async Task ReloadGameAsync()
    {
        if (_reloadGameTaskCompletionSource is not null)
        {
            _reloadGameTaskCompletionSource.SetCanceled();
        }

        if (gameControlWeakReference.TryGetTarget(out var control))
        {
            _reloadGameTaskCompletionSource = new TaskCompletionSource();

            Game.PropertyChanged += Game_PropertyChanged;
            Game.NeedsProcessing = true;
            Game.ProcessGame(forceNeedsProcessing: true);

            var dialogStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var dialog = new EasyContentDialog(App.CurrentApp.MainWindow.Content.XamlRoot)
            {
                Title = ResourceHelper.GetString("GamesPage_ReloadingGame"),
                Content = new ProgressRing()
                {
                    IsIndeterminate = true,
                },
                PrimaryButtonText = ResourceHelper.GetString("General_Cancel")
            };
            var dialogTask = dialog.ShowAsync().AsTask();

            await Task.WhenAny(dialogTask, _reloadGameTaskCompletionSource.Task);

            Game.PropertyChanged -= Game_PropertyChanged;


            if (dialogTask.IsCompleted)
            {
                // User clicked cancel, close the current dialog.
                Close();
            }
            else
            {
                var loadingDuration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - dialogStart;

                if (loadingDuration < 1000)
                {
                    // Force loading dialog to exist for at least 1 second
                    await Task.Delay(1000 - (int)loadingDuration);
                }

                Close();

                if (dialogTask.IsCompleted == true)
                {
                    return;
                }

                // Game finished reloading so re-launch the GameControl.
                _reloadGameTaskCompletionSource = null;
                dialog.Hide();
                var gameControl = new GameControl(Game);
                _ = gameControl.ShowAsync();
            }
        }
    }

    private void Game_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Game.RuntimeManifest))
        {
            OnPropertyChanged(nameof(RuntimeComponentsText));
            OnPropertyChanged(nameof(RuntimeStatusText));
            OnPropertyChanged(nameof(FrameGenerationStatusText));
            OnPropertyChanged(nameof(PrimaryActionText));
            OnPropertyChanged(nameof(PrimaryActionExplanationText));
        }

        if (e.PropertyName == nameof(Game.Processing))
        {
            if (Game.Processing == true)
            {
                _reloadGameTaskCompletionSource?.SetResult();
            }
        }
    }

    [RelayCommand]
    async Task ShowHideGameAsync()
    {
        if (Game.IsHidden is null)
        {
            Game.IsHidden = true;
        }
        else
        {
            Game.IsHidden = !Game.IsHidden;
        }
        await Game.SaveToDatabaseAsync();
    }

    [RelayCommand]
    async Task NVAPIErrorAsync()
    {
        if (gameControlWeakReference.TryGetTarget(out var control))
        {
            await NVAPIHelper.Instance.DisplayNVAPIErrorAsync(control.XamlRoot);
        }
    }
}
