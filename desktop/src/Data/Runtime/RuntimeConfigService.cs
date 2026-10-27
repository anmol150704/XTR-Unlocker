using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DLSS_Swapper.Data.Runtime;

/// <summary>
/// Applies the small, stable subset of OptiScaler.ini that XTR owns. Unknown keys and
/// comments remain untouched so users can keep runtime-specific tuning in the file.
/// </summary>
internal static class RuntimeConfigService
{
    private static readonly IReadOnlyDictionary<string, string> UpscalerKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DX11"] = "Dx11Upscaler",
            ["DX12"] = "Dx12Upscaler",
            ["Vulkan"] = "VulkanUpscaler",
        };

    internal static async Task ApplyModificationsAsync(
        string installRoot,
        IEnumerable<PlannedConfigEntry> modifications,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(modifications);

        var path = Path.Combine(Path.GetFullPath(installRoot), "OptiScaler.ini");
        if (!File.Exists(path))
            throw new FileNotFoundException("The installed runtime configuration was not found.", path);

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        content = ReplaceIniValues(content, modifications);

        var temporaryPath = path + $".{Guid.NewGuid():N}.xtrtmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static async Task ApplyProfileAsync(
        string installRoot,
        RuntimeIntegrationProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(profile);

        var path = Path.Combine(Path.GetFullPath(installRoot), "OptiScaler.ini");
        if (!File.Exists(path))
            throw new FileNotFoundException("The installed runtime configuration was not found.", path);

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var profileMods = new List<PlannedConfigEntry>();
        if (!string.IsNullOrWhiteSpace(profile.Api) &&
            UpscalerKeys.TryGetValue(profile.Api, out var upscalerKey) &&
            !string.IsNullOrWhiteSpace(profile.SelectedUpscaler))
        {
            profileMods.Add(new PlannedConfigEntry { Section = "Upscalers", Key = upscalerKey, Value = profile.SelectedUpscaler });
        }

        if (!string.IsNullOrWhiteSpace(profile.FrameGenerationEnabled))
            profileMods.Add(new PlannedConfigEntry { Section = "FrameGen", Key = "Enabled", Value = profile.FrameGenerationEnabled });

        if (!string.IsNullOrWhiteSpace(profile.FrameGenerationInput))
            profileMods.Add(new PlannedConfigEntry { Section = "FrameGen", Key = "FGInput", Value = profile.FrameGenerationInput });
        if (!string.IsNullOrWhiteSpace(profile.FrameGenerationOutput))
            profileMods.Add(new PlannedConfigEntry { Section = "FrameGen", Key = "FGOutput", Value = profile.FrameGenerationOutput });

        content = ReplaceIniValues(content, profileMods);

        var temporaryPath = path + $".{Guid.NewGuid():N}.xtrtmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static async Task UpdateSettingAsync(
        string installRoot,
        string section,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        await UpdateSettingsAsync(installRoot, new[] { (section, key, value) }, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task UpdateSettingsAsync(
        string installRoot,
        IEnumerable<(string Section, string Key, string Value)> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentNullException.ThrowIfNull(settings);

        var path = Path.Combine(Path.GetFullPath(installRoot), "OptiScaler.ini");
        if (!File.Exists(path))
            return;

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var plannedSettings = new List<PlannedConfigEntry>();
        foreach (var (section, key, value) in settings)
        {
            if (!string.IsNullOrWhiteSpace(section) && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                plannedSettings.Add(new PlannedConfigEntry { Section = section, Key = key, Value = value });
            }
        }
        content = ReplaceIniValues(content, plannedSettings);

        var temporaryPath = path + $".{Guid.NewGuid():N}.xtrtmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static string? ReadIniValue(string content, string section, string key)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
            return null;

        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inSection = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                if (inSection)
                    return null;

                if (string.Equals(trimmed[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                }
                continue;
            }

            if (!inSection || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0 || !string.Equals(trimmed[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                continue;

            return trimmed[(separator + 1)..].Trim();
        }

        return null;
    }

    internal static string ReplaceIniValue(string content, string section, string key, string value)
    {
        return ReplaceIniValues(content, new[] { new PlannedConfigEntry { Section = section, Key = key, Value = value } });
    }

    internal static string ReplaceIniValues(string content, IEnumerable<PlannedConfigEntry> modifications)
    {
        var pendingBySection = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in modifications)
        {
            if (string.IsNullOrWhiteSpace(mod.Section) || string.IsNullOrWhiteSpace(mod.Key) || mod.Value is null)
                continue;

            if (!pendingBySection.TryGetValue(mod.Section, out var keyDict))
            {
                keyDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                pendingBySection[mod.Section] = keyDict;
            }
            keyDict[mod.Key] = mod.Value;
        }

        if (pendingBySection.Count == 0)
            return content;

        var lines = new List<string>(content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        string? currentSection = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                var newSectionName = trimmed[1..^1].Trim();

                // If leaving an existing section, insert any keys that were not found in that section
                if (currentSection != null && pendingBySection.TryGetValue(currentSection, out var remainingKeys) && remainingKeys.Count > 0)
                {
                    foreach (var kvp in remainingKeys)
                    {
                        lines.Insert(index, $"{kvp.Key}={kvp.Value}");
                        index++;
                    }
                    remainingKeys.Clear();
                }

                currentSection = newSectionName;
                continue;
            }

            if (currentSection == null || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            if (pendingBySection.TryGetValue(currentSection, out var sectionDict) && sectionDict.TryGetValue(key, out var newValue))
            {
                var prefixLength = lines[index].IndexOf(trimmed, StringComparison.Ordinal);
                var prefix = prefixLength >= 0 ? lines[index][..prefixLength] : string.Empty;
                lines[index] = $"{prefix}{key}={newValue}";
                sectionDict.Remove(key);
            }
        }

        // Handle remaining keys for the last section
        if (currentSection != null && pendingBySection.TryGetValue(currentSection, out var lastSectionKeys) && lastSectionKeys.Count > 0)
        {
            foreach (var kvp in lastSectionKeys)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
            lastSectionKeys.Clear();
        }

        // Append new sections that did not exist in the file
        foreach (var (sec, keys) in pendingBySection)
        {
            if (keys.Count == 0) continue;

            lines.Add(string.Empty);
            lines.Add($"[{sec}]");
            foreach (var kvp in keys)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}