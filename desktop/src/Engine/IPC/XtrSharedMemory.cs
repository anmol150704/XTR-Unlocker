using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace DLSS_Swapper.Engine.IPC;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XtrSharedMemoryHeader
{
    public uint Magic;            // 0x58545231 ("XTR1")
    public uint Version;          // 1
    public uint WriterPid;        // Desktop App PID or Game PID
    public uint SequenceNumber;   // Monotonically increasing counter for lock-free reader synchronization
    public long TimestampQpc;     // QueryPerformanceCounter timestamp
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XtrTelemetryData
{
    public float Fps;
    public float FrameTimeMs;
    public float CpuFrameTimeMs;
    public float GpuFrameTimeMs;
    public float PresentLatencyMs;
    public float DisplayLatencyMs;
    public ulong VramBudgetBytes;
    public ulong VramUsedBytes;
    public float GpuTemperatureC;
    public float GpuPowerW;
    public uint GpuClockMhz;
    public uint TargetFrameRate;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct XtrEngineState
{
    public uint TargetProcessId;
    public byte ActiveUpscaler;          // 0: None, 1: DLSS, 2: FSR, 3: XeSS
    public byte UpscalerMode;            // 0: Quality, 1: Balanced, 2: Performance, 3: UltraPerformance, 4: NativeAA
    public byte ActiveDlssPreset;        // 0..13 (11: Preset K, 13: Preset M, 5: Preset E)
    public byte FrameGenerationActive;   // 0: False, 1: True
    public byte FrameGenMultiplier;      // 2: 2x, 3: 3x, 4: 4x
    public byte InGameHudEnabled;        // 0: False, 1: True
    public byte InGameHudPosition;       // 0: TopLeft, 1: TopRight, 2: BottomLeft, 3: BottomRight
    public uint HudHotkey;               // e.g. VK_INSERT (0x2D)
    public uint TurboBoostActive;        // 0: False, 1: True
    public fixed byte GameTitleBytes[128];

    public string GameTitle
    {
        get
        {
            fixed (byte* p = GameTitleBytes)
            {
                int len = 0;
                while (len < 128 && p[len] != 0) len++;
                return System.Text.Encoding.UTF8.GetString(p, len);
            }
        }
        set
        {
            fixed (byte* p = GameTitleBytes)
            {
                for (int i = 0; i < 128; i++) p[i] = 0;
                if (!string.IsNullOrEmpty(value))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                    int copyLen = Math.Min(bytes.Length, 127);
                    Marshal.Copy(bytes, 0, (IntPtr)p, copyLen);
                }
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XtrSharedMemoryPayload
{
    public XtrSharedMemoryHeader Header;
    public XtrTelemetryData Telemetry;
    public XtrEngineState State;
}

/// <summary>
/// Cross-process high-performance shared memory IPC between the XTR Unlocker desktop app
/// and the native in-game emerald HUD companion (xtr_hud.asi).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class XtrSharedMemory : IDisposable
{
    public const string DefaultSharedMemoryName = "Local\\XtrEngineSharedMemory_v1";
    public const uint ExpectedMagic = 0x58545231; // "XTR1"
    public const uint CurrentVersion = 1;
    public const int SharedMemorySizeBytes = 4096;

    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private uint _sequenceCounter;
    private bool _disposed;

    public string MapName { get; }
    public bool IsOwner { get; }

    public XtrSharedMemory(string mapName = DefaultSharedMemoryName)
    {
        MapName = mapName;
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("XtrSharedMemory is only supported on Windows.");
        }

        try
        {
            _mmf = MemoryMappedFile.CreateOrOpen(
                mapName,
                SharedMemorySizeBytes,
                MemoryMappedFileAccess.ReadWrite);

            _accessor = _mmf.CreateViewAccessor(0, SharedMemorySizeBytes, MemoryMappedFileAccess.ReadWrite);
            IsOwner = true;

            // Initialize header if uninitialized
            _accessor.Read(0, out XtrSharedMemoryHeader existingHeader);
            if (existingHeader.Magic != ExpectedMagic)
            {
                var initialHeader = new XtrSharedMemoryHeader
                {
                    Magic = ExpectedMagic,
                    Version = CurrentVersion,
                    WriterPid = (uint)Environment.ProcessId,
                    SequenceNumber = 0,
                    TimestampQpc = Stopwatch.GetTimestamp()
                };
                _accessor.Write(0, ref initialHeader);
            }
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to open or initialize XTR shared memory IPC map '{mapName}'.", ex);
        }
    }

    public void WritePayload(ref XtrTelemetryData telemetry, ref XtrEngineState state)
    {
        ThrowIfDisposed();

        // Increment sequence to odd (write in progress)
        uint seq = Interlocked.Increment(ref _sequenceCounter);
        if ((seq & 1) == 0) seq = Interlocked.Increment(ref _sequenceCounter);

        var header = new XtrSharedMemoryHeader
        {
            Magic = ExpectedMagic,
            Version = CurrentVersion,
            WriterPid = (uint)Environment.ProcessId,
            SequenceNumber = seq,
            TimestampQpc = Stopwatch.GetTimestamp()
        };

        int headerSize = Marshal.SizeOf<XtrSharedMemoryHeader>();
        int telemetrySize = Marshal.SizeOf<XtrTelemetryData>();

        _accessor.Write(0, ref header);
        _accessor.Write(headerSize, ref telemetry);
        _accessor.Write(headerSize + telemetrySize, ref state);

        // Increment sequence to even (write complete, consistent read ready)
        header.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);
        _accessor.Write(0, ref header);
    }

    public void UpdateTelemetry(ref XtrTelemetryData telemetry)
    {
        ThrowIfDisposed();

        int headerSize = Marshal.SizeOf<XtrSharedMemoryHeader>();
        uint seq = Interlocked.Increment(ref _sequenceCounter);
        if ((seq & 1) == 0) seq = Interlocked.Increment(ref _sequenceCounter);

        var header = new XtrSharedMemoryHeader
        {
            Magic = ExpectedMagic,
            Version = CurrentVersion,
            WriterPid = (uint)Environment.ProcessId,
            SequenceNumber = seq,
            TimestampQpc = Stopwatch.GetTimestamp()
        };

        _accessor.Write(0, ref header);
        _accessor.Write(headerSize, ref telemetry);

        header.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);
        _accessor.Write(0, ref header);
    }

    public void UpdateEngineState(ref XtrEngineState state)
    {
        ThrowIfDisposed();

        int headerSize = Marshal.SizeOf<XtrSharedMemoryHeader>();
        int telemetrySize = Marshal.SizeOf<XtrTelemetryData>();

        uint seq = Interlocked.Increment(ref _sequenceCounter);
        if ((seq & 1) == 0) seq = Interlocked.Increment(ref _sequenceCounter);

        var header = new XtrSharedMemoryHeader
        {
            Magic = ExpectedMagic,
            Version = CurrentVersion,
            WriterPid = (uint)Environment.ProcessId,
            SequenceNumber = seq,
            TimestampQpc = Stopwatch.GetTimestamp()
        };

        _accessor.Write(0, ref header);
        _accessor.Write(headerSize + telemetrySize, ref state);

        header.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);
        _accessor.Write(0, ref header);
    }

    public bool TryReadPayload(out XtrSharedMemoryPayload payload)
    {
        ThrowIfDisposed();
        payload = default;

        int headerSize = Marshal.SizeOf<XtrSharedMemoryHeader>();
        int telemetrySize = Marshal.SizeOf<XtrTelemetryData>();

        // Lock-free reader: check sequence counter before and after reading
        for (int retry = 0; retry < 5; retry++)
        {
            _accessor.Read(0, out XtrSharedMemoryHeader startHeader);
            if (startHeader.Magic != ExpectedMagic || (startHeader.SequenceNumber & 1) != 0)
            {
                // Write in progress or uninitialized, yield briefly
                Thread.Yield();
                continue;
            }

            _accessor.Read(headerSize, out XtrTelemetryData telemetry);
            _accessor.Read(headerSize + telemetrySize, out XtrEngineState state);
            _accessor.Read(0, out XtrSharedMemoryHeader endHeader);

            if (startHeader.SequenceNumber == endHeader.SequenceNumber && endHeader.Magic == ExpectedMagic)
            {
                payload = new XtrSharedMemoryPayload
                {
                    Header = endHeader,
                    Telemetry = telemetry,
                    State = state
                };
                return true;
            }

            Thread.Yield();
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _accessor.Dispose();
            _mmf.Dispose();
        }
        catch { }
    }
}
