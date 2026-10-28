using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace XTR.E2ETests.Contracts;

public sealed class XtrSeqlockHarness : IDisposable
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly byte[] _inMemoryBuffer;
    private readonly bool _useMmf;
    private bool _disposed;

    public XtrSeqlockHarness(string? sharedMemName = null)
    {
        if (!string.IsNullOrEmpty(sharedMemName) && OperatingSystem.IsWindows())
        {
            _useMmf = true;
            _mmf = MemoryMappedFile.CreateOrOpen(sharedMemName, XtrTelemetryPacket.PacketSize);
            _accessor = _mmf.CreateViewAccessor(0, XtrTelemetryPacket.PacketSize);
            _inMemoryBuffer = Array.Empty<byte>();
        }
        else
        {
            _useMmf = false;
            _inMemoryBuffer = new byte[XtrTelemetryPacket.PacketSize];
        }
    }

    public unsafe void WritePacket(ref XtrTelemetryPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_useMmf && _accessor != null)
        {
            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            try
            {
                uint* seqPtr = (uint*)(ptr + 4); // Offset 4: SequenceNumber
                uint currentSeq = *seqPtr;
                uint startSeq = currentSeq + 1; // Odd indicates write in progress
                *seqPtr = startSeq;

                Thread.MemoryBarrier();

                // Copy remainder of packet (and set Magic, SequenceNumber, etc.)
                packet.SequenceNumber = startSeq;
                Unsafe.CopyBlock(ptr, Unsafe.AsPointer(ref packet), (uint)XtrTelemetryPacket.PacketSize);

                Thread.MemoryBarrier();

                uint endSeq = startSeq + 1; // Even indicates write complete
                *seqPtr = endSeq;
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        else
        {
            fixed (byte* ptr = _inMemoryBuffer)
            {
                uint* seqPtr = (uint*)(ptr + 4);
                uint currentSeq = *seqPtr;
                uint startSeq = currentSeq + 1;
                *seqPtr = startSeq;

                Thread.MemoryBarrier();

                packet.SequenceNumber = startSeq;
                Unsafe.CopyBlock(ptr, Unsafe.AsPointer(ref packet), (uint)XtrTelemetryPacket.PacketSize);

                Thread.MemoryBarrier();

                uint endSeq = startSeq + 1;
                *seqPtr = endSeq;
            }
        }
    }

    public unsafe bool TryReadPacket(out XtrTelemetryPacket packet, int maxSpins = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        packet = default;

        int spins = 0;
        while (spins < maxSpins)
        {
            if (_useMmf && _accessor != null)
            {
                byte* ptr = null;
                _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                try
                {
                    uint* seqPtr = (uint*)(ptr + 4);
                    uint seq1 = *seqPtr;

                    // If sequence number is odd, write is in progress; spin wait
                    if ((seq1 & 1) != 0)
                    {
                        spins++;
                        Thread.SpinWait(10);
                        continue;
                    }

                    Thread.MemoryBarrier();

                    XtrTelemetryPacket candidate = *(XtrTelemetryPacket*)ptr;

                    Thread.MemoryBarrier();

                    uint seq2 = *seqPtr;
                    if (seq1 == seq2 && candidate.IsMagicValid)
                    {
                        packet = candidate;
                        return true;
                    }
                }
                finally
                {
                    _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }
            else
            {
                fixed (byte* ptr = _inMemoryBuffer)
                {
                    uint* seqPtr = (uint*)(ptr + 4);
                    uint seq1 = *seqPtr;

                    if ((seq1 & 1) != 0)
                    {
                        spins++;
                        Thread.SpinWait(10);
                        continue;
                    }

                    Thread.MemoryBarrier();

                    XtrTelemetryPacket candidate = *(XtrTelemetryPacket*)ptr;

                    Thread.MemoryBarrier();

                    uint seq2 = *seqPtr;
                    if (seq1 == seq2 && candidate.IsMagicValid)
                    {
                        packet = candidate;
                        return true;
                    }
                }
            }

            spins++;
            Thread.SpinWait(10);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _accessor?.Dispose();
        _mmf?.Dispose();
        _disposed = true;
    }
}
