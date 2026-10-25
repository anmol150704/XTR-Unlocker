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
        foreach (var mod in modifications)
        {
            if (!string.IsNullOrWhiteSpace(mod.Section) && !string.IsNullOrWhiteSpace(mod.Key) && !string.IsNullOrWhiteSpace(mod.Value))
            {
                content = ReplaceIniValue(content, mod.Section, mod.Key, mod.Value);
            }
        }

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
        if (!string.IsNullOrWhiteSpace(profile.Api) &&
            UpscalerKeys.TryGetValue(profile.Api, out var upscalerKey) &&
            !string.IsNullOrWhiteSpace(profile.SelectedUpscaler))
        {
            content = ReplaceIniValue(content, "Upscalers", upscalerKey, profile.SelectedUpscaler);
        }

        if (!string.IsNullOrWhiteSpace(profile.FrameGenerationEnabled))
            content = ReplaceIniValue(content, "FrameGen", "Enabled", profile.FrameGenerationEnabled);

        if (!string.IsNullOrWhiteSpace(profile.FrameGenerationInput))
            content = ReplaceIniValue(content, "FrameGen", "FGInput", profile.FrameGenerationInput);
        if (!string.IsNullOrWhiteSpace(profile.FrameGenerationOutput))
            content = ReplaceIniValue(content, "FrameGen", "FGOutput", profile.FrameGenerationOutput);

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
        foreach (var (section, key, value) in settings)
        {
            if (!string.IsNullOrWhiteSpace(section) && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                content = ReplaceIniValue(content, section, key, value);
            }
        }

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
        var lines = new List<string>(content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        var inSection = false;
        var replaced = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                if (inSection)
                {
                    // Leaving the target section without having found the key; insert it before the next section
                    lines.Insert(index, $"{key}={value}");
                    replaced = true;
                    break;
                }

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

            var prefixLength = lines[index].IndexOf(trimmed, StringComparison.Ordinal);
            var prefix = prefixLength >= 0 ? lines[index][..prefixLength] : string.Empty;
            lines[index] = $"{prefix}{key}={value}";
            replaced = true;
            break;
        }

        if (!replaced)
        {
            if (inSection)
            {
                lines.Add($"{key}={value}");
            }
            else
            {
                lines.Add(string.Empty);
                lines.Add($"[{section}]");
                lines.Add($"{key}={value}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}