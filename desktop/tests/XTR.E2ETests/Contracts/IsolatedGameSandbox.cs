using System;
using System.IO;
using System.Security.Cryptography;
using DLSS_Swapper;
using DLSS_Swapper.Data.Hardware;

namespace XTR.E2ETests.Contracts;

/// <summary>
/// Per-test disposable sandbox creating an isolated temporary game root and, when requested,
/// isolating <see cref="Storage.StoragePathOverride"/> and <see cref="Storage.TempPathOverride"/>.
/// Any test class using storage isolation MUST be annotated with [DoNotParallelize].
/// </summary>
public sealed class IsolatedGameSandbox : IDisposable
{
    private readonly bool _isolateStorage;
    private readonly string? _previousStorageOverride;
    private readonly string? _previousTempOverride;
    private bool _disposed;

    public string SandboxRoot { get; }
    public string GameRoot { get; }
    public string StorageRoot { get; }
    public string TempRoot { get; }

    public IsolatedGameSandbox(string prefix = "xtr_e2e", bool isolateStorage = true, string? customGameSubfolderName = null)
    {
        _isolateStorage = isolateStorage;
        SandboxRoot = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        GameRoot = string.IsNullOrWhiteSpace(customGameSubfolderName)
            ? Path.Combine(SandboxRoot, "game")
            : Path.Combine(SandboxRoot, customGameSubfolderName);
        StorageRoot = Path.Combine(SandboxRoot, "storage");
        TempRoot = Path.Combine(SandboxRoot, "temp");

        Directory.CreateDirectory(GameRoot);

        if (_isolateStorage)
        {
            Directory.CreateDirectory(StorageRoot);
            Directory.CreateDirectory(TempRoot);
            _previousStorageOverride = Storage.StoragePathOverride;
            _previousTempOverride = Storage.TempPathOverride;
            Storage.StoragePathOverride = StorageRoot;
            Storage.TempPathOverride = TempRoot;
        }
    }

    public string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(GameRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public string CreateBytes(string relativePath, byte[] content)
    {
        var fullPath = Path.Combine(GameRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    public bool FileExists(string relativePath)
    {
        var fullPath = Path.Combine(GameRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath);
    }

    public string GetFullPath(string relativePath) =>
        Path.Combine(GameRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public string CopyBundledPackToTemp(string folderName = "pack")
    {
        var destRoot = Path.Combine(SandboxRoot, folderName);
        CopyDirectoryRecursive(TestPaths.BundledPackagePath, destRoot);
        return destRoot;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    public static GpuHardwareInfo CreateRtx4070() => new()
    {
        Name = "NVIDIA GeForce RTX 4070",
        Vendor = GpuVendor.Nvidia,
        Architecture = "Ada Lovelace (RTX 40 Series)",
        VramBytes = 12288UL * 1024 * 1024,
        DriverVersion = "560.94",
        IsHagsEnabled = true,
        SupportsNativeDlssG = true,
        SupportsFp8Dlss45 = true,
        RecommendedDlssPresetId = 13
    };

    public static GpuHardwareInfo CreateRtx5090() => new()
    {
        Name = "NVIDIA GeForce RTX 5090",
        Vendor = GpuVendor.Nvidia,
        Architecture = "Blackwell (RTX 50 Series)",
        VramBytes = 32768UL * 1024 * 1024,
        DriverVersion = "570.00",
        IsHagsEnabled = true,
        SupportsNativeDlssG = true,
        SupportsMultiFrameGen = true,
        SupportsFp8Dlss45 = true,
        RecommendedDlssPresetId = 13
    };

    public static GpuHardwareInfo CreateRtx3080() => new()
    {
        Name = "NVIDIA GeForce RTX 3080",
        Vendor = GpuVendor.Nvidia,
        Architecture = "Ampere (RTX 30 Series)",
        VramBytes = 10240UL * 1024 * 1024,
        DriverVersion = "555.85",
        IsHagsEnabled = true,
        SupportsNativeDlssG = false,
        RecommendedDlssPresetId = 11
    };

    public static GpuHardwareInfo CreateRtx2050Mobile4Gb() => new()
    {
        Name = "NVIDIA GeForce RTX 2050",
        Vendor = GpuVendor.Nvidia,
        Architecture = "Ampere (GA107 Mobile - RTX 2050)",
        VramBytes = 4096UL * 1024 * 1024,
        DriverVersion = "555.85",
        IsHagsEnabled = true,
        SupportsNativeDlssG = false,
        RecommendedDlssPresetId = 11
    };

    public static GpuHardwareInfo CreateAmdRx7800Xt() => new()
    {
        Name = "AMD Radeon RX 7800 XT",
        Vendor = GpuVendor.Amd,
        Architecture = "AMD RDNA 3",
        VramBytes = 16384UL * 1024 * 1024,
        DriverVersion = "24.7.1",
        IsHagsEnabled = true,
        SupportsNativeDlssG = false,
        RecommendedDlssPresetId = 0
    };

    public static GpuHardwareInfo CreateIntelArcB580() => new()
    {
        Name = "Intel(R) Arc(TM) B580 Graphics",
        Vendor = GpuVendor.Intel,
        Architecture = "Intel Arc (Battlemage)",
        VramBytes = 12288UL * 1024 * 1024,
        DriverVersion = "32.0.101.6253",
        IsHagsEnabled = true,
        SupportsNativeDlssG = false,
        RecommendedDlssPresetId = 0
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isolateStorage)
        {
            Storage.StoragePathOverride = _previousStorageOverride;
            Storage.TempPathOverride = _previousTempOverride;
        }

        try
        {
            if (Directory.Exists(SandboxRoot))
            {
                // Clear read-only attributes if any test set them
                foreach (var file in Directory.EnumerateFiles(SandboxRoot, "*", SearchOption.AllDirectories))
                {
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }

                Directory.Delete(SandboxRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
