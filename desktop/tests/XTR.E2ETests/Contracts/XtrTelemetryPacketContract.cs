using System;
using System.Runtime.InteropServices;
using System.Text;

namespace XTR.E2ETests.Contracts;

public enum XtrFrameGenMode : uint
{
    Off = 0,
    NativeDLSS = 1,
    FSR31_Bridge = 2,
    XeFG = 3
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct XtrTelemetryPacket
{
    public const uint ExpectedMagic = 0x58545231; // "XTR1" in little-endian
    public const int PacketSize = 112;
    public const string SharedMemoryName = @"Local\XtrTelemetrySharedMem";

    public uint Magic;              // Offset 0 (4 bytes): 0x58545231 ("XTR1")
    public uint SequenceNumber;     // Offset 4 (4 bytes): Monotonic counter for tearing check
    public uint ProcessId;          // Offset 8 (4 bytes): Target game PID
    public fixed byte GpuName[64];  // Offset 12 (64 bytes): Null-terminated UTF-8 GPU name
    public float GpuTemperatureC;   // Offset 76 (4 bytes): IEEE 754 float
    public float GpuUtilization;    // Offset 80 (4 bytes): IEEE 754 float (0.0f - 100.0f)
    public uint VramUsedMb;         // Offset 84 (4 bytes): Megabytes allocated
    public uint VramTotalMb;        // Offset 88 (4 bytes): Megabytes total physical
    public uint FrameGenMode;       // Offset 92 (4 bytes): 0=Off, 1=NativeDLSS, 2=FSR31_Bridge, 3=XeFG
    public uint Multiplier;         // Offset 96 (4 bytes): 2x, 3x, 4x
    public uint AntiGhostingShield; // Offset 100 (4 bytes): 1=Active, 0=Inactive
    public float InGameFps;         // Offset 104 (4 bytes): Telemetry hook output
    public float InGameFrametimeMs; // Offset 108 (4 bytes): Telemetry hook output

    public readonly bool IsMagicValid => Magic == ExpectedMagic;

    public void SetGpuName(string name)
    {
        fixed (byte* ptr = GpuName)
        {
            for (int i = 0; i < 64; i++)
            {
                ptr[i] = 0;
            }

            if (string.IsNullOrEmpty(name)) return;

            byte[] bytes = Encoding.UTF8.GetBytes(name);
            int copyLen = Math.Min(bytes.Length, 63); // Reserve last byte for null
            for (int i = 0; i < copyLen; i++)
            {
                ptr[i] = bytes[i];
            }
            ptr[copyLen] = 0;
        }
    }

    public readonly string GetGpuName()
    {
        fixed (byte* ptr = GpuName)
        {
            int len = 0;
            while (len < 64 && ptr[len] != 0)
            {
                len++;
            }
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(ptr, len);
        }
    }

    public static byte[] Serialize(ref XtrTelemetryPacket packet)
    {
        byte[] buffer = new byte[PacketSize];
        fixed (XtrTelemetryPacket* src = &packet)
        {
            Marshal.Copy((IntPtr)src, buffer, 0, PacketSize);
        }
        return buffer;
    }

    public static XtrTelemetryPacket Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < PacketSize)
            throw new ArgumentException($"Buffer length {bytes.Length} is less than packet size {PacketSize}.");

        fixed (byte* src = bytes)
        {
            return *(XtrTelemetryPacket*)src;
        }
    }
}
