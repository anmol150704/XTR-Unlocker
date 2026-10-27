using System;
using System.Collections.Generic;
using System.Linq;

namespace DLSS_Swapper.Data.Runtime;

public enum CompatibilityLevel
{
    VerifiedCompatible,
    CommunityTested,
    Experimental,
    MultiplayerBlocked
}

public sealed class CompatibilityRecord
{
    public string Title { get; set; } = string.Empty;
    public CompatibilityLevel Level { get; set; } = CompatibilityLevel.VerifiedCompatible;
    public string RecommendedProxy { get; set; } = "dxgi.dll";
    public string RecommendedApi { get; set; } = "DX12";
    public bool SupportsNativeFrameGen { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public static class CompatibilityDatabase
{
    private static readonly Dictionary<string, CompatibilityRecord> Records = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cyberpunk 2077"] = new()
        {
            Title = "Cyberpunk 2077",
            Level = CompatibilityLevel.VerifiedCompatible,
            RecommendedProxy = "dxgi.dll",
            RecommendedApi = "DX12",
            SupportsNativeFrameGen = true,
            Notes = "Full native Streamline and DLSS Frame Generation support on DX12."
        },
        ["Forza Horizon 5"] = new()
        {
            Title = "Forza Horizon 5",
            Level = CompatibilityLevel.VerifiedCompatible,
            RecommendedProxy = "version.dll",
            RecommendedApi = "DX12",
            SupportsNativeFrameGen = true,
            Notes = "ForzaTech virtual texture streaming. Requires version.dll proxy and native Streamline passthrough."
        },
        ["Forza Horizon 6"] = new()
        {
            Title = "Forza Horizon 6",
            Level = CompatibilityLevel.VerifiedCompatible,
            RecommendedProxy = "version.dll",
            RecommendedApi = "DX12",
            SupportsNativeFrameGen = true,
            Notes = "Next-gen ForzaTech virtual texture streaming. Requires version.dll proxy and zero-overhead native Streamline passthrough."
        },
        ["The Witcher 3: Wild Hunt"] = new()
        {
            Title = "The Witcher 3: Wild Hunt",
            Level = CompatibilityLevel.VerifiedCompatible,
            RecommendedProxy = "dxgi.dll",
            RecommendedApi = "DX12",
            SupportsNativeFrameGen = true,
            Notes = "DX12 executable (witcher3.exe) supports DLSS Frame Generation."
        },
        ["Marvel's Spider-Man Remastered"] = new()
        {
            Title = "Marvel's Spider-Man Remastered",
            Level = CompatibilityLevel.VerifiedCompatible,
            RecommendedProxy = "dxgi.dll",
            RecommendedApi = "DX12",
            SupportsNativeFrameGen = true,
            Notes = "Native DLSS 3 Frame Generation supported."
        },
        ["Apex Legends"] = new()
        {
            Title = "Apex Legends",
            Level = CompatibilityLevel.MultiplayerBlocked,
            RecommendedProxy = "none",
            RecommendedApi = "DX11",
            SupportsNativeFrameGen = false,
            Notes = "Multiplayer game protected by Easy Anti-Cheat. DLL proxy injection blocked for safety."
        },
        ["VALORANT"] = new()
        {
            Title = "VALORANT",
            Level = CompatibilityLevel.MultiplayerBlocked,
            RecommendedProxy = "none",
            RecommendedApi = "DX11",
            SupportsNativeFrameGen = false,
            Notes = "Kernel-level Vanguard anti-cheat active. Proxy DLL injection blocked for account safety."
        },
        ["Destiny 2"] = new()
        {
            Title = "Destiny 2",
            Level = CompatibilityLevel.MultiplayerBlocked,
            RecommendedProxy = "none",
            RecommendedApi = "DX11",
            SupportsNativeFrameGen = false,
            Notes = "BattlEye anti-cheat active. Proxy DLL injection blocked for safety."
        }
    };

    public static CompatibilityRecord? LookupGame(string gameTitle)
    {
        if (string.IsNullOrWhiteSpace(gameTitle)) return null;

        if (Records.TryGetValue(gameTitle, out var exact))
            return exact;

        // Partial match
        var matchingKey = Records.Keys.FirstOrDefault(k => gameTitle.Contains(k, StringComparison.OrdinalIgnoreCase));
        return matchingKey is not null ? Records[matchingKey] : null;
    }
}
