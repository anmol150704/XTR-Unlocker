using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

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

public sealed class CompatibilityDatabasePayload
{
    public int SchemaVersion { get; set; } = 1;
    public string LastUpdated { get; set; } = string.Empty;
    public List<CompatibilityRecord> Profiles { get; set; } = [];
}

public static class CompatibilityDatabase
{
    private static readonly Dictionary<string, CompatibilityRecord> Records = new(StringComparer.OrdinalIgnoreCase);

    static CompatibilityDatabase()
    {
        InitializeBundled();
        TryLoadFromDisk();
    }

    public static int RecordCount => Records.Count;

    public static CompatibilityRecord? LookupGame(string gameTitle)
    {
        if (string.IsNullOrWhiteSpace(gameTitle)) return null;

        if (Records.TryGetValue(gameTitle, out var exact))
            return exact;

        // Partial / substring match
        var matchingKey = Records.Keys.FirstOrDefault(k =>
            gameTitle.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            k.Contains(gameTitle, StringComparison.OrdinalIgnoreCase));

        return matchingKey is not null ? Records[matchingKey] : null;
    }

    public static bool IsMultiplayerBlocked(string gameTitle)
    {
        var record = LookupGame(gameTitle);
        return record?.Level == CompatibilityLevel.MultiplayerBlocked;
    }

    public static IReadOnlyCollection<CompatibilityRecord> GetAllRecords()
    {
        return Records.Values.ToList().AsReadOnly();
    }

    private static void TryLoadFromDisk()
    {
        try
        {
            var searchPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "profiles", "compatibility-db.json"),
                Path.Combine(Environment.CurrentDirectory, "profiles", "compatibility-db.json")
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var payload = JsonSerializer.Deserialize<CompatibilityDatabasePayload>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (payload?.Profiles is not null && payload.Profiles.Count > 0)
                    {
                        foreach (var profile in payload.Profiles)
                        {
                            if (!string.IsNullOrWhiteSpace(profile.Title))
                            {
                                Records[profile.Title] = profile;
                            }
                        }
                    }
                    break;
                }
            }
        }
        catch
        {
            // Bundled fallback remains active
        }
    }

    private static void InitializeBundled()
    {
        var bundled = new[]
        {
            new CompatibilityRecord { Title = "Cyberpunk 2077", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Full native Streamline and DLSS Frame Generation support on DX12." },
            new CompatibilityRecord { Title = "Forza Horizon 5", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "version.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "ForzaTech virtual texture streaming. Requires version.dll proxy and native Streamline passthrough." },
            new CompatibilityRecord { Title = "Forza Horizon 6", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "version.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Next-gen ForzaTech virtual texture streaming. Requires version.dll proxy and zero-overhead native Streamline passthrough." },
            new CompatibilityRecord { Title = "The Witcher 3: Wild Hunt", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "DX12 executable (witcher3.exe) supports DLSS Frame Generation." },
            new CompatibilityRecord { Title = "Marvel's Spider-Man Remastered", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Native DLSS 3 Frame Generation supported." },
            new CompatibilityRecord { Title = "Marvel's Spider-Man 2", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Native DLSS 3.7+ and FSR 3.1 frame generation supported." },
            new CompatibilityRecord { Title = "Alan Wake 2", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Northlight Engine mesh shaders. Full DLSS 3.5 Ray Reconstruction and Frame Generation." },
            new CompatibilityRecord { Title = "Black Myth: Wukong", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Unreal Engine 5. Full DLSS 3 Frame Generation and Full Ray Tracing support." },
            new CompatibilityRecord { Title = "Starfield", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Creation Engine 2. Native DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Indiana Jones and the Great Circle", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "id Tech engine. Native DLSS 3.7+ and Full Ray Tracing." },
            new CompatibilityRecord { Title = "S.T.A.L.K.E.R. 2: Heart of Chornobyl", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Unreal Engine 5. Full DLSS 3 and FSR 3.1 Frame Generation." },
            new CompatibilityRecord { Title = "Monster Hunter Wilds", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "RE Engine. Native DLSS Frame Generation supported." },
            new CompatibilityRecord { Title = "Baldur's Gate 3", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX11", SupportsNativeFrameGen = false, Notes = "DirectX 11 and Vulkan supported. DLSS 2 Super Resolution." },
            new CompatibilityRecord { Title = "Hogwarts Legacy", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Unreal Engine 4. Native DLSS Frame Generation." },
            new CompatibilityRecord { Title = "Resident Evil 4", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "RE Engine. OptiScaler FSR 3.1 injection unlocks upscaling." },
            new CompatibilityRecord { Title = "Dead Space", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "Frostbite engine. DLSS 2 Super Resolution." },
            new CompatibilityRecord { Title = "Dying Light 2 Stay Human", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "C-Engine. Full DLSS 3 Frame Generation supported." },
            new CompatibilityRecord { Title = "Returnal", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Unreal Engine 4. DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Ghost of Tsushima Director's Cut", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Native DLSS 3 and FSR 3 Frame Generation supported." },
            new CompatibilityRecord { Title = "God of War Ragnarok", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Native DLSS 3.7 Frame Generation supported." },
            new CompatibilityRecord { Title = "Horizon Forbidden West", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Decima Engine. Full DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Dragon's Dogma 2", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "RE Engine. Native DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Diablo IV", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Full DLSS 3 Frame Generation and Ray Tracing." },
            new CompatibilityRecord { Title = "Remnant II", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Unreal Engine 5. Native DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Lords of the Fallen", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Unreal Engine 5. DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Lies of P", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "Unreal Engine 4. Ultra-smooth DLSS 2 implementation." },
            new CompatibilityRecord { Title = "Final Fantasy XVI", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = true, Notes = "Native DLSS 3 Frame Generation." },
            new CompatibilityRecord { Title = "Red Dead Redemption 2", Level = CompatibilityLevel.VerifiedCompatible, RecommendedProxy = "dxgi.dll", RecommendedApi = "Vulkan", SupportsNativeFrameGen = false, Notes = "Vulkan and DX12 supported. DLSS 2 Super Resolution." },
            new CompatibilityRecord { Title = "Elden Ring", Level = CompatibilityLevel.CommunityTested, RecommendedProxy = "dxgi.dll", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "Easy Anti-Cheat must be disabled for offline modding before injecting proxies." },
            new CompatibilityRecord { Title = "Apex Legends", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX11", SupportsNativeFrameGen = false, Notes = "Multiplayer game protected by Easy Anti-Cheat. DLL proxy injection blocked for safety." },
            new CompatibilityRecord { Title = "VALORANT", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX11", SupportsNativeFrameGen = false, Notes = "Kernel-level Vanguard anti-cheat active. Proxy DLL injection blocked for account safety." },
            new CompatibilityRecord { Title = "Destiny 2", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX11", SupportsNativeFrameGen = false, Notes = "BattlEye anti-cheat active. Proxy DLL injection blocked for safety." },
            new CompatibilityRecord { Title = "Tom Clancy's Rainbow Six Siege", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX11", SupportsNativeFrameGen = false, Notes = "BattlEye anti-cheat active. Proxy injection blocked." },
            new CompatibilityRecord { Title = "Fortnite", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "BattlEye / Easy Anti-Cheat active. Proxy injection blocked." },
            new CompatibilityRecord { Title = "Call of Duty: Warzone", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "RICOCHET kernel anti-cheat active. Proxy injection blocked." },
            new CompatibilityRecord { Title = "Counter-Strike 2", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "Vulkan", SupportsNativeFrameGen = false, Notes = "VAC anti-cheat active. Proxy injection blocked for account safety." },
            new CompatibilityRecord { Title = "Helldivers 2", Level = CompatibilityLevel.MultiplayerBlocked, RecommendedProxy = "none", RecommendedApi = "DX12", SupportsNativeFrameGen = false, Notes = "nProtect GameGuard active. Proxy injection blocked." }
        };

        foreach (var r in bundled)
        {
            Records[r.Title] = r;
        }
    }
}
