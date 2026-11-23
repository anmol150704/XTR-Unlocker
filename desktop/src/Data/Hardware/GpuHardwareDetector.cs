using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
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
    public uint VendorId { get; set; }
    public uint DeviceId { get; set; }
    public ulong VramBytes { get; set; }
    public double VramGigabytes => Math.Round(VramBytes / (1024.0 * 1024.0 * 1024.0), 1);
    public uint DedicatedVramMb => (uint)(VramBytes / (1024 * 1024));
    public ulong VramBudgetMb { get; set; }
    public ulong VramUsageMb { get; set; }
    public bool IsHagsEnabled { get; set; }
    public bool SupportsNativeDlssG { get; set; }
    public bool SupportsMultiFrameGen { get; set; }
    public bool SupportsFp8Dlss45 { get; set; }
    public int RecommendedDlssPresetId { get; set; } = 11;
    public bool SupportsUniversalBridge { get; set; } = true;
    public bool SupportsFfxFrameGenBridge => SupportsUniversalBridge;
    public bool SupportsFxFramenGenBridge => SupportsUniversalBridge;

    public bool IsAdaOrBlackwell => (Architecture?.Contains("Ada", StringComparison.OrdinalIgnoreCase) ?? false) || (Architecture?.Contains("Blackwell", StringComparison.OrdinalIgnoreCase) ?? false);
    public string RecommendedFgMode { get; set; } = "Universal FG Bridge (FSR 3.1)";
    public string RecommendedNeuralPreset { get; set; } = "Preset E (Anti-Ghosting Champion)";

    public string SummaryText =>
        $"{Name} • {Architecture} • {(IsHagsEnabled ? "HAGS Enabled" : "HAGS Disabled")} • {RecommendedFgMode}";
}

[SupportedOSPlatform("windows")]
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
            // First attempt DXGI 1.4 enumeration for 64-bit VRAM precision and accurate Vendor/Device IDs
            if (TryQueryDxgi(info))
            {
                // DXGI query succeeded
            }
            else
            {
                // Fallback to WMI if DXGI is unavailable
                QueryWmi(info);
            }
        }
        catch
        {
            // Heuristic fallback if both fail
            info.Name = "Discrete Performance GPU";
            info.Vendor = GpuVendor.Nvidia;
            info.Architecture = "Ampere (GA107 Mobile - RTX 2050)";
        }

        // Query Windows Registry for Hardware-Accelerated GPU Scheduling (HAGS)
        info.IsHagsEnabled = CheckHagsEnabled();

        // Overlay live sensor data if available
        try
        {
            var snapshot = GpuSensorReader.ReadSnapshot();
            if (snapshot.VramBudgetBytes > 0)
            {
                info.VramBudgetMb = snapshot.VramBudgetBytes / (1024 * 1024);
                info.VramUsageMb = snapshot.VramUsageBytes / (1024 * 1024);
            }
        }
        catch
        {
            // Non-critical sensor overlay failure
        }

        _cachedInfo = info;
        return info;
    }

    private static void QueryWmi(GpuHardwareInfo info)
    {
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
            info.Name = bestGpu.Name;
            info.DriverVersion = bestGpu.DriverVersion;
            info.VramBytes = bestGpu.VramBytes;
            info.Vendor = bestGpu.Vendor;
            info.Architecture = bestGpu.Architecture;
            info.SupportsNativeDlssG = bestGpu.SupportsNativeDlssG;
            info.SupportsMultiFrameGen = bestGpu.SupportsMultiFrameGen;
            info.SupportsFp8Dlss45 = bestGpu.SupportsFp8Dlss45;
            info.RecommendedDlssPresetId = bestGpu.RecommendedDlssPresetId;
            info.RecommendedFgMode = bestGpu.RecommendedFgMode;
            info.RecommendedNeuralPreset = bestGpu.RecommendedNeuralPreset;
        }
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1([MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppFactory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint LowPart;
        public int HighPart;
        public uint Flags;
    }

    private static bool TryQueryDxgi(GpuHardwareInfo info)
    {
        IntPtr pFactory = IntPtr.Zero;
        try
        {
            // IID_IDXGIFactory1: 770aae78-f26f-4dba-a829-253c83d1b387
            var factory1Guid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(factory1Guid, out pFactory) != 0 || pFactory == IntPtr.Zero)
            {
                return false;
            }

            // IDXGIFactory1 vtable:
            // QueryInterface (0), AddRef (1), Release (2), SetPrivateData (3), SetPrivateDataInterface (4), GetPrivateData (5), GetParent (6)
            // EnumAdapters (7), MakeWindowAssociation (8), GetWindowAssociation (9), CreateSwapChain (10), CreateSoftwareAdapter (11)
            // EnumAdapters1 (12), IsCurrent (13)
            IntPtr vtable = Marshal.ReadIntPtr(pFactory);
            IntPtr enumAdapters1Ptr = Marshal.ReadIntPtr(vtable, 12 * IntPtr.Size);

            var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(enumAdapters1Ptr);

            GpuHardwareInfo? bestGpu = null;
            uint index = 0;

            while (true)
            {
                if (enumAdapters1(pFactory, index, out IntPtr pAdapter) != 0 || pAdapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    // IDXGIAdapter1 vtable:
                    // QueryInterface (0), AddRef (1), Release (2), ...
                    // GetDesc1 (10)
                    IntPtr adapterVtable = Marshal.ReadIntPtr(pAdapter);
                    IntPtr getDesc1Ptr = Marshal.ReadIntPtr(adapterVtable, 10 * IntPtr.Size);
                    var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(getDesc1Ptr);

                    if (getDesc1(pAdapter, out DXGI_ADAPTER_DESC1 desc) == 0)
                    {
                        // DXGI_ADAPTER_FLAG_SOFTWARE = 2
                        if ((desc.Flags & 2) == 0 && !string.IsNullOrWhiteSpace(desc.Description))
                        {
                            var candidate = new GpuHardwareInfo
                            {
                                Name = desc.Description,
                                VendorId = desc.VendorId,
                                DeviceId = desc.DeviceId,
                                VramBytes = (ulong)desc.DedicatedVideoMemory.ToUInt64()
                            };

                            ClassifyGpu(candidate);

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
                    }
                }
                finally
                {
                    Marshal.Release(pAdapter);
                }

                index++;
            }

            if (bestGpu != null)
            {
                info.Name = bestGpu.Name;
                info.VendorId = bestGpu.VendorId;
                info.DeviceId = bestGpu.DeviceId;
                info.VramBytes = bestGpu.VramBytes;
                info.Vendor = bestGpu.Vendor;
                info.Architecture = bestGpu.Architecture;
                info.SupportsNativeDlssG = bestGpu.SupportsNativeDlssG;
                info.SupportsMultiFrameGen = bestGpu.SupportsMultiFrameGen;
                info.SupportsFp8Dlss45 = bestGpu.SupportsFp8Dlss45;
                info.RecommendedDlssPresetId = bestGpu.RecommendedDlssPresetId;
                info.RecommendedFgMode = bestGpu.RecommendedFgMode;
                info.RecommendedNeuralPreset = bestGpu.RecommendedNeuralPreset;
                return true;
            }
        }
        catch
        {
            // DXGI enumeration error fallback
        }
        finally
        {
            if (pFactory != IntPtr.Zero)
            {
                Marshal.Release(pFactory);
            }
        }

        return false;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DXGI_ADAPTER_DESC1 desc);

    public static void ClassifyGpu(GpuHardwareInfo info)
    {
        var nameUpper = info.Name.ToUpperInvariant();

        if (info.VendorId == 0x10DE || nameUpper.Contains("NVIDIA") || nameUpper.Contains("GEFORCE") || nameUpper.Contains("RTX") || nameUpper.Contains("GTX"))
        {
            info.Vendor = GpuVendor.Nvidia;

            if (nameUpper.Contains("RTX 50") || (info.DeviceId >= 0x2B00 && info.DeviceId <= 0x2CFF))
            {
                info.Architecture = "Blackwell (RTX 50 Series)";
                info.SupportsNativeDlssG = true;
                info.SupportsMultiFrameGen = true;
                info.SupportsFp8Dlss45 = true;
                info.RecommendedDlssPresetId = 13; // Preset M
                info.RecommendedFgMode = "Native DLSS 4 Hardware Multi-FG";
            }
            else if (nameUpper.Contains("RTX 40") || nameUpper.Contains("ADA"))
            {
                info.Architecture = "Ada Lovelace (RTX 40 Series)";
                info.SupportsNativeDlssG = true;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = true;
                info.RecommendedDlssPresetId = 13; // Preset M
                info.RecommendedFgMode = "Native DLSS 3 Hardware FG";
            }
            else if (nameUpper.Contains("RTX 2050"))
            {
                // Explicit Ampere GA107 Mobile classification (checked before generic RTX 20!)
                info.Architecture = "Ampere (GA107 Mobile - RTX 2050)";
                info.SupportsNativeDlssG = false;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = false;
                info.RecommendedDlssPresetId = 11; // Preset K
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1) with Streamline Spoofing";
            }
            else if (nameUpper.Contains("RTX 30") || nameUpper.Contains("AMPERE") || nameUpper.Contains("RTX A") || nameUpper.Contains("A100"))
            {
                info.Architecture = (nameUpper.Contains("RTX A") || nameUpper.Contains("A100"))
                    ? "Ampere (Workstation Series)"
                    : "Ampere (RTX 30 Series)";
                info.SupportsNativeDlssG = false;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = false;
                info.RecommendedDlssPresetId = 11; // Preset K
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1) with Streamline Spoofing";
            }
            else if (nameUpper.Contains("RTX 20") || nameUpper.Contains("TURING"))
            {
                info.Architecture = "Turing (RTX 20 Series)";
                info.SupportsNativeDlssG = false;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = false;
                info.RecommendedDlssPresetId = 11; // Preset K
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1) with Streamline Spoofing";
            }
            else if (nameUpper.Contains("GTX 16"))
            {
                info.Architecture = "Turing (GTX 16 Series)";
                info.SupportsNativeDlssG = false;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = false;
                info.RecommendedDlssPresetId = 0;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            }
            else if (nameUpper.Contains("GTX 10"))
            {
                info.Architecture = "Pascal (GTX 10 Series)";
                info.SupportsNativeDlssG = false;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = false;
                info.RecommendedDlssPresetId = 0;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            }
            else
            {
                info.Architecture = "NVIDIA Tensor Core Architecture";
                info.SupportsNativeDlssG = false;
                info.SupportsMultiFrameGen = false;
                info.SupportsFp8Dlss45 = false;
                info.RecommendedDlssPresetId = 11;
                info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1) with Streamline Spoofing";
            }

            info.RecommendedNeuralPreset = "Preset E (Anti-Ghosting Champion)";
        }
        else if (info.VendorId == 0x1002 || nameUpper.Contains("AMD") || nameUpper.Contains("RADEON"))
        {
            info.Vendor = GpuVendor.Amd;

            if (nameUpper.Contains("RX 9") || nameUpper.Contains("RDNA 4"))
            {
                info.Architecture = "AMD RDNA 4";
            }
            else if (nameUpper.Contains("RX 7") || nameUpper.Contains("RDNA 3"))
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
            info.RecommendedDlssPresetId = 0;
            info.RecommendedFgMode = "Universal FG Bridge (FSR 3.1)";
            info.RecommendedNeuralPreset = "Preset E (Anti-Ghosting Champion)";
        }
        else if (info.VendorId == 0x8086 || nameUpper.Contains("INTEL") || nameUpper.Contains("ARC"))
        {
            info.Vendor = GpuVendor.Intel;

            if (nameUpper.Contains("ARC B") || nameUpper.Contains("BATTLEMAGE"))
            {
                info.Architecture = "Intel Arc (Battlemage)";
            }
            else if (nameUpper.Contains("ARC"))
            {
                info.Architecture = "Intel Arc (Alchemist)";
            }
            else
            {
                info.Architecture = "Intel Iris / UHD Graphics";
            }

            info.SupportsNativeDlssG = false;
            info.RecommendedDlssPresetId = 0;
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
