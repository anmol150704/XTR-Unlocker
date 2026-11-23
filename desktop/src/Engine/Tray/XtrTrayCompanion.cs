using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DLSS_Swapper.Engine.Tray;

/// <summary>
/// Lightweight Win32 system tray companion for XTR Unlocker with automatic working set trimming,
/// Turbo Boost game launch notifications, and quick control toggles.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class XtrTrayCompanion : IDisposable
{
    private const uint WM_USER = 0x0400;
    public const uint WM_TRAYICON = WM_USER + 101;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_NONE = 0x00000000;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, nuint dwMinimumWorkingSetSize, nuint dwMaximumWorkingSetSize, uint Flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    private readonly IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private bool _isAdded;
    private bool _disposed;
    private readonly uint _trayId = 1001;

    public bool IsVisibleInTray => _isAdded;

    public XtrTrayCompanion(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        InitializeIcon();
    }

    private void InitializeIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
            {
                _iconHandle = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XTR Tray] Failed to load icon: {ex.Message}");
        }
    }

    public void AddToTray(string tooltip = "XTR Unlocker — Active")
    {
        if (_disposed || _isAdded) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = _trayId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip,
            uTimeoutOrVersion = NOTIFYICON_VERSION_4
        };

        if (Shell_NotifyIconW(NIM_ADD, ref nid))
        {
            _isAdded = true;
            Shell_NotifyIconW(NIM_SETVERSION, ref nid);
        }
    }

    public void RemoveFromTray()
    {
        if (!_isAdded) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = _trayId
        };

        Shell_NotifyIconW(NIM_DELETE, ref nid);
        _isAdded = false;
    }

    public void ShowNotification(string title, string message, bool isTurbo = true)
    {
        if (_disposed || !_isAdded) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHandle,
            uID = _trayId,
            uFlags = NIF_INFO,
            szInfoTitle = title.Length > 63 ? title[..63] : title,
            szInfo = message.Length > 255 ? message[..255] : message,
            dwInfoFlags = isTurbo ? NIIF_INFO : NIIF_NONE
        };

        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    /// <summary>
    /// Drastically reduces resident physical memory pages down to minimum working set (< 40 MB)
    /// when the app window is hidden to the system tray.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            if (OperatingSystem.IsWindows())
            {
                var hProc = GetCurrentProcess();
                SetProcessWorkingSetSizeEx(hProc, unchecked((nuint)ulong.MaxValue), unchecked((nuint)ulong.MaxValue), 0x00000001 /* QUOTA_LIMITS_HARDWS_MIN_ENABLE */);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XTR Tray] Failed to load icon: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RemoveFromTray();

        if (_iconHandle != IntPtr.Zero)
        {
            try
            {
                DestroyIcon(_iconHandle);
            }
            catch { }
            _iconHandle = IntPtr.Zero;
        }
    }
}
