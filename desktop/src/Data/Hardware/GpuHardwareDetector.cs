using System.Threading.Tasks;
using System;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace DLSS_Swapper.Data.Hardware;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Other
}

public class GpuHardwareInfo
{
    public string Name { get; set; } = "Generic Graphics Adapter";
    public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
    public string Architecture { get; set; } = "Standard Graphics Architecture";
    public string DriverVersion { get; set; } = "Unknown";
    public ulong VramBytes { get; set; }
    public double VramGigabytes => Math.Round(VramBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public bool IsHagsEnabled { get; set; }
    public bool SupportsNativeDlssG { get; set; }
    public bool SupportsUniversalBridge { get; set; } = true;
    public uint DedicatedVramMb => (uint)(VramBytes / (1024 * 1024));
    public bool IsAdaOrBlackwell => (Architecture?.Contains("Ada", StringComparison.OrdinalIgnoreCase) ?? false) || (Architecture?.Contains("Blackwell", StringComparison.OrdinalIgnoreCase) ?? false);
    public bool SupportsFxFramenGenBridge => SupportsUniversalBridge;
    public string RecommendedFgMode { get; set; } = "Universal FG Bridge (FSR 3.1)";
    public string RecommendedNeuralPreset { get; set; } = "Preset E (Anti-Ghosting Champion)";

    public string SummaryText =>
        $"{Name} • {Architecture} • {(IsHagsEnabled ? "HAGS Enabled" : "HAGS Disabled")} • {RecommendedFgMode}";
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class GpuHardwareDetector
{
    private static GpuHardwareInfo? _cachedInfo;

    public static Task<GpuHardwareInfo> GetHardwareInfoAsync(bool refresh = false)
    {
        if (_cachedInfo != null && !refresh)
        {
            return Task.FromResult(_cachedInfo);
        }

        return Task.Run(() => GetHardwareInfo(refresh));
    }

    public static GpuHardwareInfo GetHardwareInfo(bool refresh = false)
    {
        if (_cachedInfo != null && !refresh)
        {
            return _cachedInfo;
        }

        var info = new GpuHardwareInfo();

        try
        {
            // Query Win32_VideoController for display adapters
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
            using var results = searcher.Get();

            GpuHardwareInfo? bestGpu = null;

            foreach (var item in results)
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                var driver = item["DriverVersion"]?.ToString() ?? "Unknown";
                ulong ram = 0;

                if (item["AdapterRAM"] != null)
                {
                    _ = ulong.TryParse(item["AdapterRAM"].ToString(), out ram);
                }

                // Filter out software / dummy adapters
                if (string.IsNullOrWhiteSpace(name) ||
                    name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("IDDCX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var candidate = new GpuHardwareInfo
                {
                    Name = name,
                    DriverVersion = driver,
                    VramBytes = ram
                };

                ClassifyGpu(candidate);

                // Prefer discrete NVIDIA or AMD GPU over integrated Intel UHD / AMD Radeon Graphics
                if (bestGpu == null)
                {
                    bestGpu = candidate;
                }
                else if (candidate.Vendor == GpuVendor.Nvidia && bestGpu.Vendor != GpuVendor.Nvidia)
                {
                    bestGpu = candidate;
                }
                else if (candidate.Vendor == GpuVendor.Amd && bestGpu.Vendor == GpuVendor.Intel)
                {
                    bestGpu = candidate;
                }
                else if (candidate.VramBytes > bestGpu.VramBytes && candidate.Vendor == bestGpu.Vendor)
                {
                    bestGpu = candidate;
                }
            }

            if (bestGpu != null)
            {
                info = bestGpu;
            }
        }
        catch
        {
            // Fallback heuristics if WMI is restricted
            info.Name = "Discrete Performance GPU";
            info.Vendor = GpuVendor.Nvidia;
            info.Architecture = "Turing / Ampere Architecture";
        }

        // Query Windows Registry for Hardware-Accelerated GPU Scheduling (HAGS)
        info.IsHagsEnabled = CheckHagsEnabled();

        _cachedInfo = info;
        return info;
    }

    public static void ClassifyGpu(GpuHardwareInfo info)
    {
        var nameUpper = info.Name.ToUpperInvariant();

        if (nameUpper.Contains("NVIDIA") || nameUpper.Contains("GEFORCE") || nameUpper.Contains("RTX") || nameUpper.Contains("GTX"))
        {
            info.Vendor = GpuVendor.Nvidia;

            if (nameUpper.Contains("RTX 40") || nameUpper.Contains("ADA"))
            {
                info.Architecture = "Ada Lovelace (RTX 40 Series)";
                info.SupportsNativeDlssG = true;
                info.RecommendedFgMode = "Native DLSS 3 Hardware FG";
            }
            else if (nameUpper.Contains("RTX 30") || nameUpper.Contains("AMPERE"))
            {
                info.Architecture = "Ampere (RTX 30 Series)";
                info.SupportsNativeDlssG = false;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1) with Streamline Spoofing";
            }
            else if (nameUpper.Contains("RTX 20") || nameUpper.Contains("TURING"))
            {
                info.Architecture = "Turing (RTX 20 Series)";
                info.SupportsNativeDlssG = false;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1) with Streamline Spoofing";
            }
            else if (nameUpper.Contains("GTX 16"))
            {
                info.Architecture = "Turing (GTX 16 Series)";
                info.SupportsNativeDlssG = false;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            }
            else if (nameUpper.Contains("GTX 10"))
            {
                info.Architecture = "Pascal (GTX 10 Series)";
                info.SupportsNativeDlssG = false;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            }
            else
            {
                info.Architecture = "NVIDIA Tensor Core Architecture";
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            }

            info.RecommendedNeuralPreset = "Preset E (Anti-Ghosting Champion)";
        }
        else if (nameUpper.Contains("AMD") || nameUpper.Contains("RADEON"))
        {
            info.Vendor = GpuVendor.Amd;

            if (nameUpper.Contains("RX 7") || nameUpper.Contains("RDNA 3"))
            {
                info.Architecture = "AMD RDNA 3";
            }
            else if (nameUpper.Contains("RX 6") || nameUpper.Contains("RDNA 2"))
            {
                info.Architecture = "AMD RDNA 2";
            }
            else
            {
                info.Architecture = "AMD Radeon Graphics";
            }

            info.SupportsNativeDlssG = false;
            info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            info.RecommendedNeuralPreset = "Preset E (Anti-Ghosting Champion)";
        }
        else if (nameUpper.Contains("INTEL") || nameUpper.Contains("ARC"))
        {
            info.Vendor = GpuVendor.Intel;

            if (nameUpper.Contains("ARC"))
            {
                info.Architecture = "Intel Arc (Alchemist / Battlemage)";
            }
            else
            {
                info.Architecture = "Intel Iris / UHD Graphics";
            }

            info.SupportsNativeDlssG = false;
            info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            info.RecommendedNeuralPreset = "Preset E (Anti-Ghosting Champion)";
        }
        else
        {
            info.Vendor = GpuVendor.Other;
            info.Architecture = "Standard Graphics Architecture";
            info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
        }
    }

    private static bool CheckHagsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            if (key != null)
            {
                var val = key.GetValue("HwSchMode");
                if (val is int intVal && intVal == 2)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore registry permission errors
        }

        return false;
    }
}
