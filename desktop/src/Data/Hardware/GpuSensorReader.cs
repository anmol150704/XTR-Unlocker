using System;
using System.Runtime.InteropServices;

namespace DLSS_Swapper.Data.Hardware;

public sealed class GpuSensorSnapshot
{
    public uint TemperatureC { get; set; }
    public uint GpuUtilizationPercent { get; set; }
    public uint MemoryUtilizationPercent { get; set; }
    public uint GraphicsClockMhz { get; set; }
    public ulong VramUsageBytes { get; set; }
    public ulong VramBudgetBytes { get; set; }
    public bool IsNvmlAvailable { get; set; }
}

public static class GpuSensorReader
{
    private static IntPtr s_nvmlModule;
    private static bool s_nvmlInitialized;
    private static IntPtr s_nvmlDevice;
    private static readonly object s_lock = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetTemperatureDelegate(IntPtr device, int sensorType, out uint temp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetUtilizationRatesDelegate(IntPtr device, out NvmlUtilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetClockInfoDelegate(IntPtr device, int clockType, out uint clockMhz);

    private static NvmlInitDelegate? s_nvmlInit;
    private static NvmlShutdownDelegate? s_nvmlShutdown;
    private static NvmlDeviceGetHandleByIndexDelegate? s_nvmlDeviceGetHandleByIndex;
    private static NvmlDeviceGetTemperatureDelegate? s_nvmlDeviceGetTemperature;
    private static NvmlDeviceGetUtilizationRatesDelegate? s_nvmlDeviceGetUtilizationRates;
    private static NvmlDeviceGetClockInfoDelegate? s_nvmlDeviceGetClockInfo;

    static GpuSensorReader()
    {
        TryInitializeNvml();
    }

    private static void TryInitializeNvml()
    {
        lock (s_lock)
        {
            if (s_nvmlInitialized) return;

            try
            {
                if (NativeLibrary.TryLoad("nvml.dll", out s_nvmlModule))
                {
                    if (NativeLibrary.TryGetExport(s_nvmlModule, "nvmlInit_v2", out var pInit) ||
                        NativeLibrary.TryGetExport(s_nvmlModule, "nvmlInit", out pInit))
                    {
                        s_nvmlInit = Marshal.GetDelegateForFunctionPointer<NvmlInitDelegate>(pInit);
                    }

                    if (NativeLibrary.TryGetExport(s_nvmlModule, "nvmlShutdown", out var pShutdown))
                    {
                        s_nvmlShutdown = Marshal.GetDelegateForFunctionPointer<NvmlShutdownDelegate>(pShutdown);
                    }

                    if (NativeLibrary.TryGetExport(s_nvmlModule, "nvmlDeviceGetHandleByIndex_v2", out var pGetHandle) ||
                        NativeLibrary.TryGetExport(s_nvmlModule, "nvmlDeviceGetHandleByIndex", out pGetHandle))
                    {
                        s_nvmlDeviceGetHandleByIndex = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetHandleByIndexDelegate>(pGetHandle);
                    }

                    if (NativeLibrary.TryGetExport(s_nvmlModule, "nvmlDeviceGetTemperature", out var pGetTemp))
                    {
                        s_nvmlDeviceGetTemperature = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetTemperatureDelegate>(pGetTemp);
                    }

                    if (NativeLibrary.TryGetExport(s_nvmlModule, "nvmlDeviceGetUtilizationRates", out var pGetUtil))
                    {
                        s_nvmlDeviceGetUtilizationRates = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetUtilizationRatesDelegate>(pGetUtil);
                    }

                    if (NativeLibrary.TryGetExport(s_nvmlModule, "nvmlDeviceGetClockInfo", out var pGetClock))
                    {
                        s_nvmlDeviceGetClockInfo = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetClockInfoDelegate>(pGetClock);
                    }

                    if (s_nvmlInit != null && s_nvmlInit() == 0)
                    {
                        if (s_nvmlDeviceGetHandleByIndex != null && s_nvmlDeviceGetHandleByIndex(0, out s_nvmlDevice) == 0)
                        {
                            s_nvmlInitialized = true;
                        }
                    }
                }
            }
            catch
            {
                s_nvmlInitialized = false;
            }
        }
    }

    public static GpuSensorSnapshot ReadSnapshot()
    {
        var snapshot = new GpuSensorSnapshot();

        lock (s_lock)
        {
            if (s_nvmlInitialized && s_nvmlDevice != IntPtr.Zero)
            {
                snapshot.IsNvmlAvailable = true;

                if (s_nvmlDeviceGetTemperature != null && s_nvmlDeviceGetTemperature(s_nvmlDevice, 0, out var temp) == 0)
                {
                    snapshot.TemperatureC = temp;
                }

                if (s_nvmlDeviceGetUtilizationRates != null && s_nvmlDeviceGetUtilizationRates(s_nvmlDevice, out var util) == 0)
                {
                    snapshot.GpuUtilizationPercent = util.Gpu;
                    snapshot.MemoryUtilizationPercent = util.Memory;
                }

                if (s_nvmlDeviceGetClockInfo != null && s_nvmlDeviceGetClockInfo(s_nvmlDevice, 0, out var clockMhz) == 0)
                {
                    snapshot.GraphicsClockMhz = clockMhz;
                }
            }
        }

        return snapshot;
    }
}
