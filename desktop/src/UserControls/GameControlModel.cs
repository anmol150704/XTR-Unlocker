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
using System.Collections.ObjectModel;
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

    [ObservableProperty]
    public partial bool CanSelectDlssPreset { get; set; }

    [ObservableProperty]
    public partial bool CanSelectDlssDPreset { get; set; }

    [ObservableProperty]
    public partial bool CanSelectDlssGPreset { get; set; }

    public ObservableCollection<PresetOption> DlssPresetOptions { get; } = new ObservableCollection<PresetOption>();

    public ObservableCollection<PresetOption> DlssDPresetOptions { get; } = new ObservableCollection<PresetOption>();

    public ObservableCollection<PresetOption> DlssGPresetOptions { get; } = new ObservableCollection<PresetOption>();

    private bool _isLoadingPresets;

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
        "Preset J (Transformer Model)",
        "Preset K (DLSS 4 / 4.5 Ultra-Low Latency Transformer)",
        "Preset M (DLSS 4.5 FP8 Optical Transformer)"
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
    [NotifyPropertyChangedFor(nameof(FrameGenerationStatusText))]
    public partial string ActiveFrameGenMode { get; set; } = "Not Active (Runtime Not Installed)";
    [ObservableProperty]
    public partial bool AntiGhostingShieldEnabled { get; set; } = true;

    partial void OnAntiGhostingShieldEnabledChanged(bool value)
    {
        if (_isLoadingSettings) return;
        ApplyAntiGhostingSetting(value);
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

    public string RuntimeStatusText
    {
        get
        {
            if (Game.RuntimeManifest is null)
                return "Ready";

            var health = GetCurrentRuntimeHealth();
            return health?.IsRuntimeOperational == true ? "Restore available" : "Incomplete";
        }
    }

    public string DlssEngineSummaryText => $"Game engine using {Game.DlssGenerationText}";

    public string FrameGenerationStatusText
    {
        get
        {
            var hasNativeOrStreamlineFg = Game.CurrentDLSS_G is not null || Game.HasStreamlineDlssG;
            var health = GetCurrentRuntimeHealth();
            if (Game.RuntimeManifest is not null)
            {
                if (health is null || !health.ProxyFilePresent || !health.IniFilePresent || health.State == RuntimeHealthState.ErrorDetected)
                {
                    return hasNativeOrStreamlineFg
                        ? "Native DLSS-G detected (runtime incomplete)"
                        : "Runtime incomplete - bridge inactive";
                }

                if (ActiveFrameGenMode.Contains("Upscaling Only", StringComparison.OrdinalIgnoreCase))
                    return "Frame generation disabled (upscaling only)";

                if (ActiveFrameGenMode.Contains("Native DLSS 3", StringComparison.OrdinalIgnoreCase))
                {
                    return health.State == RuntimeHealthState.HealthyActive
                        ? "Native DLSS-G active"
                        : "Native DLSS-G passthrough ready";
                }

                return health.State switch
                {
                    RuntimeHealthState.HealthyActive => "Frame generation bridge active",
                    RuntimeHealthState.ProxyBypassed => "Proxy present - FG hook not yet confirmed",
                    _ => "Bridge installed - launch game to activate"
                };
            }

            return hasNativeOrStreamlineFg
                ? "Native DLSS-G detected"
                : "Available via runtime";
        }
    }

    public string PrimaryActionText => Game.RuntimeManifest is not null
        ? "Restore original files"
        : "Enable latest DLSS";

    public string RuntimeHealthSummaryText
    {
        get
        {
            var health = GetCurrentRuntimeHealth();
            return health?.Summary ?? "Ready for deployment";
        }
    }

    public string PrimaryActionExplanationText
    {
        get
        {
            var recommendedProxy = ResolveRecommendedProxy(Game);
            if (Game.RuntimeManifest is null)
            {
                return string.Equals(recommendedProxy, "version.dll", StringComparison.OrdinalIgnoreCase)
                    ? $"Installs the latest compatible DLSS runtime using {recommendedProxy} (recommended for {Game.Title})."
                    : "Installs the latest compatible DLSS runtime and verified configuration.";
            }

            var health = GetCurrentRuntimeHealth();
            if (health is null || !health.IsRuntimeOperational)
            {
                var baseMessage = "Tracked runtime files are missing or damaged. Click Restore Original Game Files to clean up stale state.";
                if (!string.Equals(Game.RuntimeManifest.ProxyFileName, recommendedProxy, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{baseMessage} Recommended proxy for this title: {recommendedProxy}.";
                }
                return baseMessage;
            }

            return health.State switch
            {
                RuntimeHealthState.InstalledNotYetLaunched => "Runtime is installed and ready. Launch the game to verify hooks, or restore original files anytime.",
                RuntimeHealthState.ProxyBypassed => "Proxy is present on disk, but OptiScaler interception was not confirmed in the last log.",
                _ => "Runtime verified active. Use Restore Original Game Files to revert cleanly."
            };
        }
    }

    private string ResolveRecommendedProxy(Game game)
    {
        var compat = CompatibilityDatabase.LookupGame(game.Title) ?? CompatibilityDatabase.LookupGame(game.ID);
        var candidate = compat?.RecommendedProxy;
        if (string.IsNullOrWhiteSpace(candidate) && DeploymentPlanner.IsForzaOrVirtualTextureTitle(game.Title))
        {
            candidate = "version.dll";
        }

        if (!string.IsNullOrWhiteSpace(candidate) &&
            RuntimeProxyNames.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            return RuntimeProxyNames.First(p => string.Equals(p, candidate, StringComparison.OrdinalIgnoreCase));
        }

        return "dxgi.dll";
    }

    private RuntimeHealthReport? GetCurrentRuntimeHealth()
    {
        if (Game.RuntimeManifest is null || string.IsNullOrWhiteSpace(Game.RuntimeManifest.InstallRoot))
        {
            return null;
        }

        var proxyName = string.IsNullOrWhiteSpace(Game.RuntimeManifest.ProxyFileName)
            ? SelectedRuntimeProxy
            : Game.RuntimeManifest.ProxyFileName;

        return RuntimeHealthService.CheckHealth(Game.RuntimeManifest.InstallRoot, proxyName);
    }

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
            SelectedRuntimeProxy = ResolveRecommendedProxy(game);
        }

        InitializeDeployedSettings();
        RefreshDlssPresetSupport();
    }

    private void RefreshDlssPresetSupport()
    {
        _isLoadingPresets = true;
        try
        {
            CanSelectDlssPreset = false;
            CanSelectDlssDPreset = false;
            CanSelectDlssGPreset = false;
            DlssPresetOptions.Clear();
            DlssDPresetOptions.Clear();
            DlssGPresetOptions.Clear();

            var hasDlss = Game.CurrentDLSS is not null || Game.HasStreamlineDlss;
            var hasDlssD = Game.CurrentDLSS_D is not null || Game.HasStreamlineDlssD;
            var hasDlssG = Game.CurrentDLSS_G is not null || Game.HasStreamlineDlssG;

            if (NVAPIHelper.Instance.IsSupported && hasDlss)
            {
                var gameProfile = NVAPIHelper.Instance.FindGameProfile(Game);
                if (gameProfile is not null)
                {
                    var gameDLSSPresetResult = NVAPIHelper.Instance.GetGameDLSSPreset(Game);
                    if (gameDLSSPresetResult.Success)
                    {
                        CanSelectDlssPreset = true;
                        Game.DlssPreset = gameDLSSPresetResult.Result;
                        foreach (var opt in NVAPIHelper.Instance.DlssPresetOptions)
                        {
                            DlssPresetOptions.Add(opt);
                        }
                        SelectedDlssPreset = Game.DlssPreset is null
                            ? DlssPresetOptions.FirstOrDefault(x => x.Value == 0)
                            : DlssPresetOptions.FirstOrDefault(x => x.Value == Game.DlssPreset);

                        if (hasDlssD)
                        {
                            var gameDLSSDPresetResult = NVAPIHelper.Instance.GetGameDLSSDPreset(Game);
                            if (gameDLSSDPresetResult.Success)
                            {
                                CanSelectDlssDPreset = true;
                                Game.DlssDPreset = gameDLSSDPresetResult.Result;
                                foreach (var opt in NVAPIHelper.Instance.DlssDPresetOptions)
                                {
                                    DlssDPresetOptions.Add(opt);
                                }
                                SelectedDlssDPreset = Game.DlssDPreset is null
                                    ? DlssDPresetOptions.FirstOrDefault(x => x.Value == 0)
                                    : DlssDPresetOptions.FirstOrDefault(x => x.Value == Game.DlssDPreset);
                            }
                        }

                        if (hasDlssG)
                        {
                            var gameDLSSGPresetResult = NVAPIHelper.Instance.GetGameDLSSGPreset(Game);
                            if (gameDLSSGPresetResult.Success)
                            {
                                CanSelectDlssGPreset = true;
                                Game.DlssGPreset = gameDLSSGPresetResult.Result;
                                foreach (var opt in NVAPIHelper.Instance.DlssGPresetOptions)
                                {
                                    DlssGPresetOptions.Add(opt);
                                }
                                SelectedDlssGPreset = Game.DlssGPreset is null
                                    ? DlssGPresetOptions.FirstOrDefault(x => x.Value == 0)
                                    : DlssGPresetOptions.FirstOrDefault(x => x.Value == Game.DlssGPreset);
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
        finally
        {
            _isLoadingPresets = false;
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

        if (_isLoadingPresets)
        {
            return;
        }

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
                var success = await DeployPresetAsync(RuntimeIntegrationPreset.TurboBoost, "âš¡ Activate XTR Turbo Boost");
                if (!success)
                    return;

                return;
            }

            var root = Path.GetDirectoryName(iniPath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            var packagePath = RuntimeInstallService.GetBundledPackagePath();
            if (packagePath is null)
                return;

            var pack = await RuntimePack.LoadFromDirectoryAsync(packagePath);
            if (pack is null)
                return;

            var plan = await DeploymentPlanner.CreatePlanAsync(
                Game.ID,
                Game.Title,
                root,
                pack,
                RuntimeIntegrationPreset.TurboBoost);

            var settings = plan.PlannedConfigModifications
                .Select(m => (m.Section, m.Key, m.Value))
                .ToList();

            if (plan.PlannedConfigModifications.Any(m => m.Section == "FrameGen" && m.Key == "Enabled" && m.Value == "true"))
            {
                ActiveFrameGenMode = "Universal FG Bridge (FSR 3.1)";
            }
            else
            {
                ActiveFrameGenMode = "Native DLSS 3 Hardware FG";
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
            await ShowRuntimeDialogAsync("âš¡ XTR Turbo Boost Activated", "Engine optimizations successfully applied:\n\nâ€¢ DLSS Neural Model: Hardware-Tiered (Preset K/M)\nâ€¢ Texture Filtering: 16x Anisotropic + Mipmap LOD Bias (-0.5)\nâ€¢ Low-Latency Mutex Frame Pacing\nâ€¢ AI Edge Sharpening: RCAS 35%\nâ€¢ In-Game Real-Time Telemetry: Active");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to activate Turbo Boost");
        }
    }

    [RelayCommand]
    Task QuickSwapDlss45Async() => QuickSwapDlssCoreAsync("310.6", "310.5", "310.");

    [RelayCommand]
    Task QuickSwapDlss40Async() => QuickSwapDlssCoreAsync("310.2", "310.1", "310.0");

    [RelayCommand]
    Task QuickSwapDlss37Async() => QuickSwapDlssCoreAsync("310.", "3.8", "3.7");

    [RelayCommand]
    Task QuickSwapDlss35Async() => QuickSwapDlssCoreAsync("3.5");

    [RelayCommand]
    Task QuickSwapDlss25Async() => QuickSwapDlssCoreAsync("2.5");

    [RelayCommand]
    Task OpenDlssPickerAsync() => ChangeRecordAsync(GameAssetType.DLSS);

    private async Task QuickSwapDlssCoreAsync(params string[] targetPrefixes)
    {
        try
        {
            if (DlssVersionHelper.IsDlss1xVersion(Game.CurrentDLSS?.Version) &&
                !targetPrefixes.Any(p => p.TrimStart('v', 'V').StartsWith("1.", StringComparison.OrdinalIgnoreCase)))
            {
                await ShowRuntimeDialogAsync(
                    "Incompatible DLSS Generation",
                    "This game uses DLSS 1.x which cannot be swapped with DLSS 2/3/4 DLLs.");
                return;
            }

            var match = DLLManager.Instance.DLSSRecords
                .Where(r =>
                    (Settings.Instance.AllowDebugDlls || !r.IsDevFile) &&
                    DlssVersionHelper.MatchesAnyVersionPrefix(r.Version, targetPrefixes) &&
                    r.LocalRecord?.IsDownloaded == true)
                .OrderByDescending(r => r.VersionNumber)
                .FirstOrDefault();

            if (match is not null)
            {
                var updateResult = await Game.UpdateDllAsync(match);
                if (updateResult.Success)
                {
                    RefreshDiagnostics();
                    RefreshDlssPresetSupport();
                    await ShowRuntimeDialogAsync("DLSS Swapped", $"Swapped game to DLSS {match.DisplayName} successfully.");
                    return;
                }

                await ShowRuntimeDialogAsync("DLSS Swap Failed", updateResult.Message);
                return;
            }

            await ChangeRecordAsync(GameAssetType.DLSS);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to quick swap DLSS to {string.Join(",", targetPrefixes)}");
            await ShowRuntimeDialogAsync("DLSS Swap Failed", ex.Message);
        }
    }

    private void InitializeDeployedSettings()
    {
        try
        {
            var health = GetCurrentRuntimeHealth();
            var iniPath = GetDeployedIniPath();
            if (iniPath is null || !File.Exists(iniPath) || (Game.RuntimeManifest is not null && health is { IsRuntimeOperational: false }))
            {
                _isLoadingSettings = true;
                IsTurboBoostActive = false;
                AntiGhostingShieldEnabled = false;
                StreamlineSpoofingEnabled = false;
                ActiveFrameGenMode = Game.RuntimeManifest is not null
                    ? "Runtime Incomplete (Inactive)"
                    : (Game.CurrentDLSS_G is not null || Game.HasStreamlineDlssG)
                        ? "Native DLSS 3 Hardware FG"
                        : "Not Active (Runtime Not Installed)";
                _isLoadingSettings = false;
                RefreshDiagnostics();
                return;
            }

            var content = File.ReadAllText(iniPath);
            _isLoadingSettings = true;

            var hudFix = RuntimeConfigService.ReadIniValue(content, "OptiFG", "HUDFix");
            if (!string.IsNullOrEmpty(hudFix))
            {
                AntiGhostingShieldEnabled = !string.Equals(hudFix, "false", StringComparison.OrdinalIgnoreCase);
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
            var fgInput = RuntimeConfigService.ReadIniValue(content, "FrameGen", "FGInput");
            var fgEnabled = RuntimeConfigService.ReadIniValue(content, "FrameGen", "Enabled");
            if (string.Equals(fgEnabled, "false", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(fgOutput, "nofg", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(fgInput, "nofg", StringComparison.OrdinalIgnoreCase))
                {
                    ActiveFrameGenMode = "Native DLSS 3 Hardware FG";
                }
                else
                {
                    ActiveFrameGenMode = "Upscaling Only (No FG)";
                }
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
                    "13" => "Preset M (DLSS 4.5 FP8 Optical Transformer)",
                    "11" => "Preset K (DLSS 4 / 4.5 Ultra-Low Latency Transformer)",
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

            // Read Turbo Boost state (respect ForzaTech profiles that intentionally omit AnisotropyOverride)
            var aniso = RuntimeConfigService.ReadIniValue(content, "Anisotropy", "AnisotropyOverride");
            var renderPreset = RuntimeConfigService.ReadIniValue(content, "DLSS", "RenderPresetForAll");
            var isProfileTurbo = Game.RuntimeProfile?.Options?.TryGetPropertyValue("Preset", out var existingPresetNode) == true &&
                                 string.Equals(existingPresetNode?.ToString(), "TurboBoost", StringComparison.OrdinalIgnoreCase);
            IsTurboBoostActive = isProfileTurbo ||
                                 (string.Equals(aniso, "16", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(renderPreset, "5", StringComparison.OrdinalIgnoreCase));

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
            var p when p.Contains("Preset M", StringComparison.OrdinalIgnoreCase) => ("true", "13"),
            var p when p.Contains("Preset K", StringComparison.OrdinalIgnoreCase) => ("true", "11"),
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
        Game.RefreshRuntimeHealth();
        OnPropertyChanged(nameof(RuntimeHealthSummaryText));
        OnPropertyChanged(nameof(PrimaryActionExplanationText));
        OnPropertyChanged(nameof(FrameGenerationStatusText));
        OnPropertyChanged(nameof(RuntimeStatusText));
        OnPropertyChanged(nameof(ActiveFrameGenMode));
        OnPropertyChanged(nameof(IsTurboBoostActive));
        OnPropertyChanged(nameof(DlssEngineSummaryText));
        OnPropertyChanged(nameof(RuntimeComponentsText));
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

        bool isUpgrade = Game.RuntimeManifest is not null;
        var dialog = new EasyContentDialog(gameControl.XamlRoot)
        {
            Title = isUpgrade ? $"Upgrade runtime: {pack.DisplayName}" : dialogTitle,
            PrimaryButtonText = isUpgrade ? "Upgrade runtime" : "Install runtime",
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
            var manifest = isUpgrade
                ? await RuntimeInstallService.UpgradePlanAsync(plan)
                : await RuntimeInstallService.InstallPlanAsync(plan);
            await Game.SaveRuntimeManifestAsync(manifest);
            var profile = await RuntimeIntegrationProfileStore.LoadAsync(Game.ID);
            if (profile is not null)
            {
                await Game.SaveRuntimeProfileAsync(profile);
            }
            InitializeDeployedSettings();
            RefreshDiagnostics();
            await ShowRuntimeDialogAsync(isUpgrade ? "Runtime upgraded" : "Runtime installed", $"{pack.DisplayName} is active for {Game.Title}. Reversible backups recorded.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            await ShowRuntimeDialogAsync(isUpgrade ? "Upgrade failed" : "Install failed", ex.Message);
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
            StreamlineSpoofingEnabled = false;
            SelectedEngineNeuralPreset = "Default (Game Engine Default)";
            SelectedRuntimeProxy = ResolveRecommendedProxy(Game);
            ActiveFrameGenMode = (Game.CurrentDLSS_G is not null || Game.HasStreamlineDlssG)
                ? "Native DLSS 3 Hardware FG"
                : "Not Active (Runtime Not Installed)";
            _isLoadingSettings = false;

            Game.RefreshRuntimeHealth();
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
            var tcs = _reloadGameTaskCompletionSource;

            void OnReloadPropertyChanged(object? s, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(Game.Processing) && Game.Processing)
                {
                    tcs.TrySetResult();
                }
            }

            Game.PropertyChanged += OnReloadPropertyChanged;
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

            Game.PropertyChanged -= OnReloadPropertyChanged;


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
        if (e.PropertyName is nameof(Game.RuntimeManifest) or nameof(Game.RuntimeProfile))
        {
            InitializeDeployedSettings();
            OnPropertyChanged(nameof(RuntimeComponentsText));
            OnPropertyChanged(nameof(RuntimeStatusText));
            OnPropertyChanged(nameof(FrameGenerationStatusText));
            OnPropertyChanged(nameof(PrimaryActionText));
            OnPropertyChanged(nameof(PrimaryActionExplanationText));
            OnPropertyChanged(nameof(RuntimeHealthSummaryText));
            OnPropertyChanged(nameof(IsTurboBoostActive));
        }

        if (e.PropertyName is nameof(Game.CurrentDLSS) or nameof(Game.HasStreamlineDlss))
        {
            OnPropertyChanged(nameof(DlssEngineSummaryText));
            OnPropertyChanged(nameof(RuntimeHealthSummaryText));
            OnPropertyChanged(nameof(PrimaryActionExplanationText));
            RefreshDlssPresetSupport();
        }

        if (e.PropertyName is nameof(Game.CurrentDLSS_D) or nameof(Game.HasStreamlineDlssD))
        {
            RefreshDlssPresetSupport();
        }

        if (e.PropertyName is nameof(Game.CurrentDLSS_G) or nameof(Game.HasStreamlineDlssG))
        {
            if (Game.RuntimeManifest is null)
            {
                ActiveFrameGenMode = (Game.CurrentDLSS_G is not null || Game.HasStreamlineDlssG)
                    ? "Native DLSS 3 Hardware FG"
                    : "Not Active (Runtime Not Installed)";
            }
            OnPropertyChanged(nameof(FrameGenerationStatusText));
            RefreshDlssPresetSupport();
        }

        if (e.PropertyName == nameof(Game.Processing))
        {
            if (Game.Processing == true)
            {
                _reloadGameTaskCompletionSource?.TrySetResult();
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
