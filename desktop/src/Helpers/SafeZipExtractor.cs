using System;
using System.IO;
using System.IO.Compression;

namespace DLSS_Swapper.Helpers;

public static class SafeZipExtractor
{
    public const long MaxEntrySizeBytes = 500 * 1024 * 1024; // 500 MB
    public const long MaxTotalSizeBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    public const int MaxEntryCount = 200;
    public const int MaxCompressionRatio = 100;

    public static void ExtractToDirectorySafely(ZipArchive archive, string destinationDirectory, bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var canonicalDest = Path.GetFullPath(destinationDirectory);
        if (!canonicalDest.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalDest += Path.DirectorySeparatorChar;
        }

        Directory.CreateDirectory(canonicalDest);

        if (archive.Entries.Count > MaxEntryCount)
        {
            throw new InvalidDataException($"Zip archive contains too many entries: {archive.Entries.Count} (max allowed: {MaxEntryCount}).");
        }

        long totalExtractedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(canonicalDest, entry.FullName));
            if (!targetPath.StartsWith(canonicalDest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Zip Slip vulnerability detected: entry '{entry.FullName}' escapes target directory '{canonicalDest}'.");
            }

            // Directory entry
            if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            ExtractEntrySafely(entry, targetPath, overwrite, ref totalExtractedBytes);
        }
    }

    public static void ExtractEntrySafely(ZipArchiveEntry entry, string destinationFilePath, bool overwrite = true)
    {
        long dummy = 0;
        ExtractEntrySafely(entry, destinationFilePath, overwrite, ref dummy);
    }

    private static void ExtractEntrySafely(ZipArchiveEntry entry, string destinationFilePath, bool overwrite, ref long totalExtractedBytes)
    {
        if (File.Exists(destinationFilePath) && !overwrite)
        {
            return;
        }

        using var entryStream = entry.Open();
        using var outStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long entryExtractedBytes = 0;
        int read;

        while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            entryExtractedBytes += read;
            totalExtractedBytes += read;

            if (entryExtractedBytes > MaxEntrySizeBytes)
            {
                outStream.Dispose();
                File.Delete(destinationFilePath);
                throw new InvalidDataException($"Zip bomb detected: entry '{entry.FullName}' exceeds max single-file limit of {MaxEntrySizeBytes} bytes.");
            }

            if (totalExtractedBytes > MaxTotalSizeBytes)
            {
                outStream.Dispose();
                File.Delete(destinationFilePath);
                throw new InvalidDataException($"Zip bomb detected: total extracted bytes exceeded max archive limit of {MaxTotalSizeBytes} bytes.");
            }

            if (entry.CompressedLength > 0 && (entryExtractedBytes / entry.CompressedLength) > MaxCompressionRatio)
            {
                outStream.Dispose();
                File.Delete(destinationFilePath);
                throw new InvalidDataException($"Zip bomb detected: compression ratio for '{entry.FullName}' exceeded {MaxCompressionRatio}:1 limit.");
            }

            outStream.Write(buffer, 0, read);
        }
    }
}
