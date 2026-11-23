using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DLSS_Swapper.Interfaces;
using SkiaSharp;
using SQLite;
using Windows.Win32;

namespace DLSS_Swapper.Data.EAApp;


[Table("ea_app_game")]
internal class EAAppGame : Game
{
    public override GameLibrary GameLibrary => GameLibrary.EAApp;

    public override bool IsReadyToPlay => true;

    [Column("display_icon_path")]
    public string DisplayIconPath { get; set; } = string.Empty;

    public EAAppGame()
    {
    }

    public EAAppGame(string contentId)
    {
        PlatformId = contentId;
        SetID();
    }

    public override bool UpdateFromGame(Game game)
    {
        var didChange = ParentUpdateFromGame(game);

        if (game is EAAppGame eaAppGame)
        {
            if (DisplayIconPath != eaAppGame.DisplayIconPath)
            {
                DisplayIconPath = eaAppGame.DisplayIconPath;
                didChange = true;
            }
        }
        return didChange;
    }

    protected override async Task UpdateCacheImageAsync()
    {
        var coverUrl = EAAppLibrary.Instance.SearchForCover(this);

        if (string.IsNullOrWhiteSpace(coverUrl) == false)
        {
            var didDownload = await DownloadCoverAsync(coverUrl).ConfigureAwait(false);
            if (didDownload)
            {
                return;
            }

            Logger.Error($"Unable to download cover for {this.Title} ({coverUrl})");
        }

        // Fall back to using icon from the game exe.
        if (string.IsNullOrWhiteSpace(DisplayIconPath))
        {
            return;
        }
               
        var extension = Path.GetExtension(DisplayIconPath) ?? string.Empty;
        if (extension?.Equals(".exe", StringComparison.InvariantCultureIgnoreCase) == true)
        {
            using (var memoryStream = new MemoryStream())
            {
                try
                {
                    unsafe
                    {
                        var shinfo = new Windows.Win32.UI.Shell.SHFILEINFOW();

                        var flags = Windows.Win32.UI.Shell.SHGFI_FLAGS.SHGFI_ICON | Windows.Win32.UI.Shell.SHGFI_FLAGS.SHGFI_LARGEICON;

                        PInvoke.SHGetFileInfo(
                            DisplayIconPath,
                            Windows.Win32.Storage.FileSystem.FILE_FLAGS_AND_ATTRIBUTES.SECURITY_ANONYMOUS,
                            ref shinfo, flags);

                        if (shinfo.hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                using var skBitmap = ExtractHIconToBitmap((IntPtr)shinfo.hIcon);
                                if (skBitmap is not null)
                                {
                                    using var image = SKImage.FromBitmap(skBitmap);
                                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                    data?.SaveTo(memoryStream);
                                    memoryStream.Seek(0, SeekOrigin.Begin);
                                }
                            }
                            finally
                            {
                                PInvoke.DestroyIcon(shinfo.hIcon);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Icon not found.");
                        }
                    }

                    if (memoryStream.Length == 0)
                    {
                        Logger.Error($"Failed to extract icon from {DisplayIconPath} in game {Title}: Memory stream is empty.");
                        return;
                    }
                        
                    await ResizeCoverAsync(memoryStream).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to extract icon from {DisplayIconPath}: {ex.Message}");
                }
            }
        }
        else if (extension?.Equals(".bmp", StringComparison.InvariantCultureIgnoreCase) == true ||
                extension?.Equals(".png", StringComparison.InvariantCultureIgnoreCase) == true ||
                extension?.Equals(".jpg", StringComparison.InvariantCultureIgnoreCase) == true ||
                extension?.Equals(".jpeg", StringComparison.InvariantCultureIgnoreCase) == true ||
                extension?.Equals(".webp", StringComparison.InvariantCultureIgnoreCase) == true)
        {
            using (var fileStream = File.OpenRead(DisplayIconPath))
            {
                await ResizeCoverAsync(fileStream).ConfigureAwait(false);
            }
        }
        else
        {
            Logger.Error($"Unknown extension {extension} for DisplayIconPath {DisplayIconPath} in game {Title}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private static SKBitmap? ExtractHIconToBitmap(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        if (!GetIconInfo(hIcon, out var iconInfo))
        {
            return null;
        }

        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero)
            {
                return null;
            }

            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<BITMAP>(), out var bmp) == 0)
            {
                return null;
            }

            var width = bmp.bmWidth;
            var height = bmp.bmHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var skBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                skBitmap.Dispose();
                return null;
            }

            try
            {
                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // top-down
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0, // BI_RGB
                    }
                };

                var linesCopied = GetDIBits(
                    hdc,
                    iconInfo.hbmColor,
                    0,
                    (uint)height,
                    skBitmap.GetPixels(),
                    ref bmi,
                    0); // DIB_RGB_COLORS = 0

                if (linesCopied == 0)
                {
                    skBitmap.Dispose();
                    return null;
                }

                return skBitmap;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
            {
                DeleteObject(iconInfo.hbmColor);
            }
            if (iconInfo.hbmMask != IntPtr.Zero)
            {
                DeleteObject(iconInfo.hbmMask);
            }
        }
    }
}
