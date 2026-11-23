using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DLSS_Swapper.Data;
using DLSS_Swapper.Data.DLSS;
using DLSS_Swapper.Data.NVIDIA;
using DLSS_Swapper.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DLSS_Swapper.Helpers;

record NVAPIResult<T>
{
    public bool Success { get; init; }
    public T Result { get; init; }
    public NvStatus? Status { get; init; }

    public NVAPIResult(bool success, T result, NvStatus? status = null)
    {
        Success = success;
        Result = result;
        Status = status;
    }
}

public class NVIDIAApiException : Exception
{
    public override string Message => NvApiDrsNative.GetErrorMessage(Status);

    public NvStatus Status { get; }

    internal NVIDIAApiException(NvStatus status)
    {
        Status = status;
    }
}

internal partial class NVAPIHelper : ObservableObject
{
    const uint NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID = NvApiDrsNative.NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID;
    const uint NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID = NvApiDrsNative.NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID;
    const uint NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID = NvApiDrsNative.NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID;

    [ObservableProperty]
    public partial bool IsSupported { get; set; }

    [ObservableProperty]
    public partial bool PermissionIssue { get; set; }

    public IReadOnlyList<PresetOption> DlssPresetOptions { get; init; }
    public IReadOnlyList<PresetOption> DlssDPresetOptions { get; init; }
    public IReadOnlyList<PresetOption> DlssGPresetOptions { get; init; }

    public static NVAPIHelper Instance { get; private set; } = new NVAPIHelper();

    NvStatus? _lastErrorStatus;

    private NVAPIHelper()
    {
        // Load DLSS presets
        try
        {
            var dlssPresetsJsonPath = @"Assets\dlss_presets.json";
            if (File.Exists(dlssPresetsJsonPath))
            {
                var dlssPresetOptions = JsonSerializer.Deserialize(File.ReadAllText(dlssPresetsJsonPath), SourceGenerationContext.Default.ListPresetOption)?.Where(x => x.Used)?.ToList();
                if (dlssPresetOptions is not null && dlssPresetOptions.Count > 0)
                {
                    for (var i = 0; i < dlssPresetOptions.Count; ++i)
                    {
                        dlssPresetOptions[i].UpdateNameFromTranslation();
                    }
                    DlssPresetOptions = dlssPresetOptions.AsReadOnly();
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, "Could not load DLSS presets from json.");
        }

        if (DlssPresetOptions is null)
        {
            DlssPresetOptions = [
                new PresetOption(ResourceHelper.GetString("DLSS_Preset_Default"), 0x00000000),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "A"), 0x00000001),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "B"), 0x00000002),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "C"), 0x00000003),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "D"), 0x00000004),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "E"), 0x00000005),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "F"), 0x00000006),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "G"), 0x00000007),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "J"), 0x0000000A),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "K"), 0x0000000B),
                new PresetOption(ResourceHelper.GetString("DLSS_Preset_AlwaysUseLatest"), 0x00FFFFFE)
            ];
        }

        // Load DLSS D presets
        try
        {
            var dlssDPresetsJsonPath = @"Assets\dlss_d_presets.json";
            if (File.Exists(dlssDPresetsJsonPath))
            {
                var dlssDPresetOptions = JsonSerializer.Deserialize(File.ReadAllText(dlssDPresetsJsonPath), SourceGenerationContext.Default.ListPresetOption)?.Where(x => x.Used)?.ToList();
                if (dlssDPresetOptions is not null && dlssDPresetOptions.Count > 0)
                {
                    for (var i = 0; i < dlssDPresetOptions.Count; ++i)
                    {
                        dlssDPresetOptions[i].UpdateNameFromTranslation();
                    }
                    DlssDPresetOptions = dlssDPresetOptions.AsReadOnly();
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, "Could not load DLSS D presets from json.");
        }

        if (DlssDPresetOptions is null)
        {
            DlssDPresetOptions = [
                new PresetOption(ResourceHelper.GetString("DLSS_Preset_Default"), 0x00000000),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "A"), 0x00000001),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "B"), 0x00000002),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "C"), 0x00000003),
                new PresetOption(ResourceHelper.GetString("DLSS_Preset_AlwaysUseLatest"), 0x00FFFFFE)
            ];
        }

        // Load DLSS G presets
        try
        {
            var dlssGPresetsJsonPath = @"Assets\dlss_g_presets.json";
            if (File.Exists(dlssGPresetsJsonPath))
            {
                var dlssGPresetOptions = JsonSerializer.Deserialize(File.ReadAllText(dlssGPresetsJsonPath), SourceGenerationContext.Default.ListPresetOption)?.Where(x => x.Used)?.ToList();
                if (dlssGPresetOptions is not null && dlssGPresetOptions.Count > 0)
                {
                    for (var i = 0; i < dlssGPresetOptions.Count; ++i)
                    {
                        dlssGPresetOptions[i].UpdateNameFromTranslation();
                    }
                    DlssGPresetOptions = dlssGPresetOptions.AsReadOnly();
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, "Could not load DLSS G presets from json.");
        }

        if (DlssGPresetOptions is null)
        {
            DlssGPresetOptions = [
                new PresetOption(ResourceHelper.GetString("DLSS_Preset_Default"), 0x00000000),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "A"), 0x00000001),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "B"), 0x00000002),
                new PresetOption(ResourceHelper.GetFormattedResourceTemplate("DLSS_Preset_Letter", "C"), 0x00000003),
                new PresetOption(ResourceHelper.GetString("DLSS_Preset_AlwaysUseLatest"), 0x00FFFFFE)
            ];
        }

        try
        {
            IsSupported = NvApiDrsNative.IsAvailable;
        }
        catch (Exception err)
        {
            Logger.Error(err, "If you don't have an NVIDIA card this is expected and can be ignored.");
            IsSupported = false;
        }

        LanguageManager.Instance.OnLanguageChanged += LanguageManager_OnLanguageChanged;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(PermissionIssue))
        {
            if (PermissionIssue)
            {
                IsSupported = false;
            }
        }
    }

    void LanguageManager_OnLanguageChanged()
    {
        foreach (var opt in DlssPresetOptions) opt.UpdateNameFromTranslation();
        foreach (var opt in DlssDPresetOptions) opt.UpdateNameFromTranslation();
        foreach (var opt in DlssGPresetOptions) opt.UpdateNameFromTranslation();
    }

    public NativeDrsProfile? FindGameProfile(Game game)
    {
        if (game.DriverSettingsProfile is not null)
        {
            return game.DriverSettingsProfile;
        }

        if (!IsSupported) return null;

        var hProfile = NvApiDrsNative.FindProfileByName(game.Title);
        if (hProfile != IntPtr.Zero)
        {
            var profile = new NativeDrsProfile { Handle = hProfile, Name = game.Title };
            game.DriverSettingsProfile = profile;
            return profile;
        }

        if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath))
        {
            try
            {
                var executables = Directory.GetFiles(game.InstallPath, "*.exe", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f));

                foreach (var exe in executables)
                {
                    hProfile = NvApiDrsNative.FindProfileByApplication(exe!);
                    if (hProfile != IntPtr.Zero)
                    {
                        var profile = new NativeDrsProfile { Handle = hProfile, Name = game.Title, Applications = [exe!] };
                        game.DriverSettingsProfile = profile;
                        return profile;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    public NVAPIResult<uint> GetGlobalDLSSPreset()
    {
        if (!IsSupported) return new NVAPIResult<uint>(false, 0);

        try
        {
            var hBase = NvApiDrsNative.GetBaseProfileHandle();
            if (hBase == IntPtr.Zero) return new NVAPIResult<uint>(false, 0);

            var (success, val, status) = NvApiDrsNative.GetDwordSetting(hBase, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return new NVAPIResult<uint>(success, val, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not get setting for GetGlobalDLSSPreset.");
            return new NVAPIResult<uint>(false, 0);
        }
    }

    public NVAPIResult<uint> GetGlobalDLSSDPreset()
    {
        if (!IsSupported) return new NVAPIResult<uint>(false, 0);

        try
        {
            var hBase = NvApiDrsNative.GetBaseProfileHandle();
            if (hBase == IntPtr.Zero) return new NVAPIResult<uint>(false, 0);

            var (success, val, status) = NvApiDrsNative.GetDwordSetting(hBase, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return new NVAPIResult<uint>(success, val, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not get setting for GetGlobalDLSSDPreset.");
            return new NVAPIResult<uint>(false, 0);
        }
    }

    public NVAPIResult<uint> GetGlobalDLSSGPreset()
    {
        if (!IsSupported) return new NVAPIResult<uint>(false, 0);

        try
        {
            var hBase = NvApiDrsNative.GetBaseProfileHandle();
            if (hBase == IntPtr.Zero) return new NVAPIResult<uint>(false, 0);

            var (success, val, status) = NvApiDrsNative.GetDwordSetting(hBase, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return new NVAPIResult<uint>(success, val, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not get setting for GetGlobalDLSSGPreset.");
            return new NVAPIResult<uint>(false, 0);
        }
    }

    public NVAPIResult<bool> SetGlobalDLSSPreset(uint preset)
    {
        if (!IsSupported) return new NVAPIResult<bool>(false, false);

        try
        {
            var hBase = NvApiDrsNative.GetBaseProfileHandle();
            if (hBase == IntPtr.Zero) return new NVAPIResult<bool>(false, false);

            var (success, status) = NvApiDrsNative.SetDwordSetting(hBase, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
            if (status == NvStatus.INVALID_USER_PRIVILEGE) PermissionIssue = true;
            if (success) NvApiDrsNative.SaveSettings();
            else _lastErrorStatus = status;

            return new NVAPIResult<bool>(success, success, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not set setting for SetGlobalDLSSPreset with preset {preset}.");
            return new NVAPIResult<bool>(false, false);
        }
    }

    public NVAPIResult<bool> SetGlobalDLSSDPreset(uint preset)
    {
        if (!IsSupported) return new NVAPIResult<bool>(false, false);

        try
        {
            var hBase = NvApiDrsNative.GetBaseProfileHandle();
            if (hBase == IntPtr.Zero) return new NVAPIResult<bool>(false, false);

            var (success, status) = NvApiDrsNative.SetDwordSetting(hBase, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
            if (status == NvStatus.INVALID_USER_PRIVILEGE) PermissionIssue = true;
            if (success) NvApiDrsNative.SaveSettings();
            else _lastErrorStatus = status;

            return new NVAPIResult<bool>(success, success, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not set setting for SetGlobalDLSSDPreset with preset {preset}.");
            return new NVAPIResult<bool>(false, false);
        }
    }

    public NVAPIResult<bool> SetGlobalDLSSGPreset(uint preset)
    {
        if (!IsSupported) return new NVAPIResult<bool>(false, false);

        try
        {
            var hBase = NvApiDrsNative.GetBaseProfileHandle();
            if (hBase == IntPtr.Zero) return new NVAPIResult<bool>(false, false);

            var (success, status) = NvApiDrsNative.SetDwordSetting(hBase, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
            if (status == NvStatus.INVALID_USER_PRIVILEGE) PermissionIssue = true;
            if (success) NvApiDrsNative.SaveSettings();
            else _lastErrorStatus = status;

            return new NVAPIResult<bool>(success, success, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not set setting for SetGlobalDLSSGPreset with preset {preset}.");
            return new NVAPIResult<bool>(false, false);
        }
    }

    public NVAPIResult<uint> GetGameDLSSPreset(Game game)
    {
        if (!IsSupported) return new NVAPIResult<uint>(false, 0);

        try
        {
            var profile = FindGameProfile(game);
            if (profile == null) return new NVAPIResult<uint>(false, 0);

            var (success, val, status) = NvApiDrsNative.GetDwordSetting(profile.Handle, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return new NVAPIResult<uint>(success, val, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not get setting for GetGameDLSSPreset for game {game.Title}.");
            return new NVAPIResult<uint>(false, 0);
        }
    }

    public NVAPIResult<uint> GetGameDLSSDPreset(Game game)
    {
        if (!IsSupported) return new NVAPIResult<uint>(false, 0);

        try
        {
            var profile = FindGameProfile(game);
            if (profile == null) return new NVAPIResult<uint>(false, 0);

            var (success, val, status) = NvApiDrsNative.GetDwordSetting(profile.Handle, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return new NVAPIResult<uint>(success, val, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not get setting for GetGameDLSSDPreset for game {game.Title}.");
            return new NVAPIResult<uint>(false, 0);
        }
    }

    public NVAPIResult<uint> GetGameDLSSGPreset(Game game)
    {
        if (!IsSupported) return new NVAPIResult<uint>(false, 0);

        try
        {
            var profile = FindGameProfile(game);
            if (profile == null) return new NVAPIResult<uint>(false, 0);

            var (success, val, status) = NvApiDrsNative.GetDwordSetting(profile.Handle, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return new NVAPIResult<uint>(success, val, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not get setting for GetGameDLSSGPreset for game {game.Title}.");
            return new NVAPIResult<uint>(false, 0);
        }
    }

    public NVAPIResult<bool> SetGameDLSSPreset(Game game, uint preset)
    {
        if (!IsSupported) return new NVAPIResult<bool>(false, false);

        try
        {
            var profile = FindGameProfile(game);
            if (profile == null) return new NVAPIResult<bool>(false, false);

            var (success, status) = NvApiDrsNative.SetDwordSetting(profile.Handle, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
            if (status == NvStatus.INVALID_USER_PRIVILEGE) PermissionIssue = true;
            if (success)
            {
                NvApiDrsNative.SaveSettings();
                game.DlssPreset = preset;
            }
            else
            {
                _lastErrorStatus = status;
            }

            return new NVAPIResult<bool>(success, success, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not set setting for SetGameDLSSPreset for game {game.Title}.");
            return new NVAPIResult<bool>(false, false);
        }
    }

    public NVAPIResult<bool> SetGameDLSSDPreset(Game game, uint preset)
    {
        if (!IsSupported) return new NVAPIResult<bool>(false, false);

        try
        {
            var profile = FindGameProfile(game);
            if (profile == null) return new NVAPIResult<bool>(false, false);

            var (success, status) = NvApiDrsNative.SetDwordSetting(profile.Handle, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
            if (status == NvStatus.INVALID_USER_PRIVILEGE) PermissionIssue = true;
            if (success)
            {
                NvApiDrsNative.SaveSettings();
                game.DlssDPreset = preset;
            }
            else
            {
                _lastErrorStatus = status;
            }

            return new NVAPIResult<bool>(success, success, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not set setting for SetGameDLSSDPreset for game {game.Title}.");
            return new NVAPIResult<bool>(false, false);
        }
    }

    public NVAPIResult<bool> SetGameDLSSGPreset(Game game, uint preset)
    {
        if (!IsSupported) return new NVAPIResult<bool>(false, false);

        try
        {
            var profile = FindGameProfile(game);
            if (profile == null) return new NVAPIResult<bool>(false, false);

            var (success, status) = NvApiDrsNative.SetDwordSetting(profile.Handle, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
            if (status == NvStatus.INVALID_USER_PRIVILEGE) PermissionIssue = true;
            if (success)
            {
                NvApiDrsNative.SaveSettings();
                game.DlssGPreset = preset;
            }
            else
            {
                _lastErrorStatus = status;
            }

            return new NVAPIResult<bool>(success, success, status);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not set setting for SetGameDLSSGPreset for game {game.Title}.");
            return new NVAPIResult<bool>(false, false);
        }
    }

    public async Task DisplayNVAPIErrorAsync(XamlRoot xamlRoot)
    {
        var dialog = new EasyContentDialog(xamlRoot)
        {
            Title = ResourceHelper.GetString("GamePage_NVAPIError_Title"),
            PrimaryButtonText = ResourceHelper.GetString("General_Okay"),
            SecondaryButtonText = ResourceHelper.GetString("General_OpenLogsDirectory"),
            DefaultButton = ContentDialogButton.Primary,
            Content = ResourceHelper.GetString("GamePage_NVAPIError_Message"),
        };

        if (_lastErrorStatus is not null)
        {
            dialog.Title = $"{dialog.Title} - {_lastErrorStatus}";
        }

        if (Environment.IsPrivilegedProcess)
        {
            dialog.Content = ResourceHelper.GetString("GamePage_NVAPIError_Admin_Message");
        }

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            FileSystemHelper.OpenFolderInExplorer(Logger.LogDirectory);
        }
    }

    public List<NGXModel> GetNGXModels()
    {
        var ngxModelsPath = Path.Combine(Environment.ExpandEnvironmentVariables("%ProgramData%"), "NVIDIA", "NGX", "models");
        if (!Directory.Exists(ngxModelsPath))
        {
            return [];
        }

        var ngxModels = new List<NGXModel>();

        var dlssPath = Path.Combine(ngxModelsPath, "dlss", "versions");
        var dlssdPath = Path.Combine(ngxModelsPath, "dlssd", "versions");
        var dlssgPath = Path.Combine(ngxModelsPath, "dlssg", "versions");

        List<NGXModel> LocalNgxModelSearch(string path, GameAssetType gameAssetType, string[] validProductNames)
        {
            var tempList = new List<NGXModel>();

            if (Directory.Exists(path))
            {
                var binFiles = Directory.GetFiles(path, "*.bin", SearchOption.AllDirectories);
                foreach (var binFile in binFiles)
                {
                    var fileInfo = new FileInfo(binFile);
                    var fileVersionInfo = FileVersionInfo.GetVersionInfo(binFile);
                    var isTrusted = WinTrust.VerifyEmbeddedSignature(binFile);

                    var isValid = true;
                    if (!isTrusted)
                    {
                        isValid = false;
                    }

                    if (!validProductNames.Contains(fileVersionInfo.ProductName))
                    {
                        isValid = false;
                    }

                    if (isValid || Settings.Instance.AllowUntrusted)
                    {
                        tempList.Add(new NGXModel(binFile, new Version(fileVersionInfo.FileMajorPart, fileVersionInfo.FileMinorPart, fileVersionInfo.FileBuildPart, fileVersionInfo.FilePrivatePart), gameAssetType, fileInfo.Length, string.Empty));
                    }
                }
            }

            return tempList;
        }

        ngxModels.AddRange(LocalNgxModelSearch(dlssPath, GameAssetType.DLSS, [
            "NVIDIA Deep Learning SuperSampling",
            "NGX DL SuperSampling"
        ]));
        ngxModels.AddRange(LocalNgxModelSearch(dlssdPath, GameAssetType.DLSS_D, [
            "NVIDIA DLSS Ray Reconstruction"
        ]));
        ngxModels.AddRange(LocalNgxModelSearch(dlssgPath, GameAssetType.DLSS_G, [
            "NVIDIA DLSS-G MFGLW"
        ]));

        return ngxModels;
    }

    private static long FactorOf1MB(long fileSize, int numParts)
    {
        var fileSizePerPart = fileSize / (double)numParts;
        var modulo1MB = fileSizePerPart % 1048576;
        return (long)(fileSizePerPart + 1048576 - modulo1MB);
    }

    public bool ValidateNVIDIAOtaHash(Stream stream, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        stream.Position = 0;

        if (hash.Contains('-'))
        {
            var hashStringParts = hash.Trim('"').Split('-');
            if (hashStringParts.Length != 2)
            {
                Logger.Error("Could not find number of hash parts.");
                return false;
            }

            if (!int.TryParse(hashStringParts[1], out var parts))
            {
                return false;
            }

            var fileSize = stream.Length;

            var partSizesToTry = new long[] {
                8388608,
                15728640,
                FactorOf1MB(fileSize, parts)
            };

            foreach (var partSize in partSizesToTry)
            {
                var partHashes = new List<byte[]>(parts);
                var buffer = new byte[partSize];

                stream.Position = 0;

                for (var i = 0; i < parts; ++i)
                {
                    Array.Clear(buffer);
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    using var md5 = MD5.Create();
                    var computedHash = md5.ComputeHash(buffer, 0, bytesRead);
                    partHashes.Add(computedHash);
                }

                int totalBytes = partHashes.Count * 16;
                var allHashes = new byte[totalBytes];
                int offset = 0;
                foreach (var partHash in partHashes)
                {
                    Buffer.BlockCopy(partHash, 0, allHashes, offset, partHash.Length);
                    offset += partHash.Length;
                }

                using (var md5 = MD5.Create())
                {
                    var finalHash = md5.ComputeHash(allHashes);
                    var hashStringWithQuotes = $"\"{Convert.ToHexStringLower(finalHash)}-{partHashes.Count}\"";
                    if (string.Equals(hashStringWithQuotes, hash))
                    {
                        return true;
                    }
                }
            }
        }
        else
        {
            stream.Position = 0;
            using var md5 = MD5.Create();
            var computedHash = md5.ComputeHash(stream);
            var hashStringWithQuotes = $"\"{Convert.ToHexStringLower(computedHash).ToLowerInvariant()}\"";
            return string.Equals(hashStringWithQuotes, hash);
        }

        return false;
    }
}
