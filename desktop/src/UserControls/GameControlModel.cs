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

    [ObservableProperty]
    public partial string SelectedRuntimeProxy { get; set; } = "dxgi.dll";
    [ObservableProperty]
    public partial bool AntiGhostingShieldEnabled { get; set; } = true;

    partial void OnAntiGhostingShieldEnabledChanged(bool value)
    {
        ApplyAntiGhostingSetting(value);
    }

    [ObservableProperty]
    public partial bool InGameHudOverlayEnabled { get; set; } = false;

    partial void OnInGameHudOverlayEnabledChanged(bool value)
    {
        ApplyHudOverlaySetting(value);
    }

    [ObservableProperty]
    public partial bool StreamlineSpoofingEnabled { get; set; } = true;

    partial void OnStreamlineSpoofingEnabledChanged(bool value)
    {
        ApplyStreamlineSpoofingSetting(value);
    }

    public string RuntimeComponentsText => Game.RuntimeManifest?.Components is { Count: > 0 } components
        ? string.Join(" � ", components)
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

    private void ApplyAntiGhostingSetting(bool enabled)
    {
        string val = enabled ? "true" : "false";
        UpdateOptiScalerConfigSetting("HUDFix", val);
        UpdateOptiScalerConfigSetting("HUDFixExtended", val);
        UpdateOptiScalerConfigSetting("MakeDepthCopy", val);
        UpdateOptiScalerConfigSetting("MakeMVCopy", val);
        UpdateOptiScalerConfigSetting("UseReactiveMaskForTransparency", val);
        if (enabled)
        {
            UpdateOptiScalerConfigSetting("DisableHUDFix", "false");
            UpdateOptiScalerConfigSetting("DlssReactiveMaskBias", "0.45");
            UpdateOptiScalerConfigSetting("ReactiveScale", "1.1");
        }
    }

    private void ApplyHudOverlaySetting(bool enabled)
    {
        UpdateOptiScalerConfigSetting("ShowFps", enabled ? "true" : "false");
        if (enabled)
        {
            UpdateOptiScalerConfigSetting("FpsOverlayType", "2");
            UpdateOptiScalerConfigSetting("OverlayMenu", "true");
        }
    }

    private void ApplyStreamlineSpoofingSetting(bool enabled)
    {
        UpdateOptiScalerConfigSetting("StreamlineSpoofing", enabled ? "true" : "false");
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

    private void UpdateOptiScalerConfigSetting(string key, string value)
    {
        try
        {
            var iniPath = GetDeployedIniPath();
            if (iniPath is null || !File.Exists(iniPath)) return;

            var lines = File.ReadAllLines(iniPath).ToList();
            bool updated = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{key}={value}";
                    updated = true;
                    break;
                }
            }
            if (!updated)
            {
                lines.Add($"{key}={value}");
            }
            File.WriteAllLines(iniPath, lines);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to write OptiScaler setting {Key}", key);
        }
    }

    [RelayCommand]
    void RefreshDiagnostics()
    {
        OnPropertyChanged(nameof(RuntimeHealthSummaryText));
        OnPropertyChanged(nameof(PrimaryActionExplanationText));
        OnPropertyChanged(nameof(FrameGenerationStatusText));
        OnPropertyChanged(nameof(RuntimeStatusText));
    }

    private async Task DeployPresetAsync(RuntimeIntegrationPreset preset, string dialogTitle)
    {
        if (!gameControlWeakReference.TryGetTarget(out var gameControl))
            return;

        var packagePath = RuntimeInstallService.GetBundledPackagePath();
        if (packagePath is null)
        {
            await ShowRuntimeDialogAsync("Bundled runtime unavailable", "This XTR build does not include the baseline OptiScaler runtime. Choose a custom package under Advanced.");
            return;
        }

        var pack = await RuntimePack.LoadFromDirectoryAsync(packagePath);
        if (pack is null)
        {
            await ShowRuntimeDialogAsync("Runtime load error", "Could not load the bundled runtime package.");
            return;
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
            return;
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
                Text = $"Preflight: {plan.PreflightResult.Headline} � {plan.PlannedCopies.Count} files to deploy � {plan.PlannedBackups.Count} backups planned\nProxy: {plan.SelectedProxy} ({plan.PreflightResult.DetectedApi})",
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
                Text = string.Join(Environment.NewLine, plan.PlannedCopies.Select(c => $"� {c.RelativePath}{(c.RequiresBackup ? " (backs up original)" : "")}")),
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
            return;
        }

        try
        {
            var manifest = await RuntimeInstallService.InstallPlanAsync(plan);
            await Game.SaveRuntimeManifestAsync(manifest);
            await ShowRuntimeDialogAsync("Runtime installed", $"{pack.DisplayName} is active for {Game.Title}. Reversible backups recorded.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            await ShowRuntimeDialogAsync("Install failed", ex.Message);
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
        await Launcher.LaunchUriAsync(new Uri("https://github.com/beeradmoore/dlss-swapper/wiki/Troubleshooting#game-is-not-in-a-ready-to-play-state"));
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
