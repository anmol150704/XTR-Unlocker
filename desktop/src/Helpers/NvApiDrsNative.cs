using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DLSS_Swapper.Helpers;

public enum NvStatus : int
{
    OK = 0,
    ERROR = -1,
    LIBRARY_NOT_FOUND = -2,
    NO_IMPLEMENTATION = -3,
    API_NOT_INITIALIZED = -7,
    INVALID_ARGUMENT = -5,
    INVALID_USER_PRIVILEGE = -9,
    PROFILE_NOT_FOUND = -157,
    SETTING_NOT_FOUND = -159,
    SETTING_SIZE_TOO_LARGE = -160,
    TOO_MANY_ENTRIES = -161,
    NOT_FOUND = -162,
    PROFILE_EXISTS = -163
}

public sealed class NativeDrsProfile
{
    public IntPtr Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Applications { get; set; } = [];
}

/// <summary>
/// Native high-performance P/Invoke wrapper for official NVIDIA Driver Settings (DRS) API in nvapi64.dll.
/// Provides zero-injection driver overrides for DLSS SR, RR, and Frame Generation presets.
/// </summary>
public static class NvApiDrsNative
{
    public const uint NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF3;
    public const uint NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF7;
    public const uint NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF1;
    public const uint NGX_DLSS_SR_OVERRIDE_ID = 0x10E41E01;
    public const uint NGX_DLSS_RR_OVERRIDE_ID = 0x10E41E02;
    public const uint NGX_DLSS_FG_OVERRIDE_ID = 0x10E41E03;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr nvapi_QueryInterface(uint id);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hLibModule);

    // Function delegates
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_Initialize_Delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_Unload_Delegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_GetErrorMessage_Delegate(NvStatus status, [MarshalAs(UnmanagedType.LPStr)] StringBuilder szDesc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_CreateSession_Delegate(out IntPtr phSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_DestroySession_Delegate(IntPtr hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_LoadSettings_Delegate(IntPtr hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_SaveSettings_Delegate(IntPtr hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_GetBaseProfile_Delegate(IntPtr hSession, out IntPtr phProfile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_FindProfileByName_Delegate(IntPtr hSession, [MarshalAs(UnmanagedType.LPWStr)] string wszProfileName, out IntPtr phProfile);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_FindApplicationByName_Delegate(IntPtr hSession, [MarshalAs(UnmanagedType.LPWStr)] string wszAppName, out IntPtr phProfile, IntPtr pApp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_GetSetting_Delegate(IntPtr hSession, IntPtr hProfile, uint settingId, ref NVDRS_SETTING pSetting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_DRS_SetSetting_Delegate(IntPtr hSession, IntPtr hProfile, ref NVDRS_SETTING pSetting);

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    public struct NVDRS_SETTING
    {
        public uint version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string settingName;
        public uint settingId;
        public uint settingType;     // 0 = DWORD, 1 = BINARY, 2 = STRING, 3 = WSTRING
        public uint settingLocation; // 0 = Current, 1 = Global, 2 = Base
        public uint isCurrentPredefined;
        public uint isPredefinedValid;
        public uint u32CurrentValue;
        public uint u32PredefinedValue;
    }

    private static readonly uint NVDRS_SETTING_VER = (uint)(Marshal.SizeOf<NVDRS_SETTING>() | (1 << 16));

    // Resolved function pointers
    private static NvAPI_Initialize_Delegate? _init;
    private static NvAPI_Unload_Delegate? _unload;
    private static NvAPI_GetErrorMessage_Delegate? _getErrorMessage;
    private static NvAPI_DRS_CreateSession_Delegate? _createSession;
    private static NvAPI_DRS_DestroySession_Delegate? _destroySession;
    private static NvAPI_DRS_LoadSettings_Delegate? _loadSettings;
    private static NvAPI_DRS_SaveSettings_Delegate? _saveSettings;
    private static NvAPI_DRS_GetBaseProfile_Delegate? _getBaseProfile;
    private static NvAPI_DRS_FindProfileByName_Delegate? _findProfileByName;
    private static NvAPI_DRS_FindApplicationByName_Delegate? _findAppByName;
    private static NvAPI_DRS_GetSetting_Delegate? _getSetting;
    private static NvAPI_DRS_SetSetting_Delegate? _setSetting;

    private static readonly object _syncLock = new();
    private static bool _initialized;
    private static bool _available;
    private static IntPtr _sessionHandle = IntPtr.Zero;

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return _available;
        }
    }

    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_syncLock)
        {
            if (_initialized) return;

            try
            {
                var hModule = LoadLibrary("nvapi64.dll");
                if (hModule == IntPtr.Zero)
                {
                    _available = false;
                    _initialized = true;
                    return;
                }
                FreeLibrary(hModule);

                _init = GetDelegate<NvAPI_Initialize_Delegate>(0x0150E828);
                _unload = GetDelegate<NvAPI_Unload_Delegate>(0xD22BDD7E);
                _getErrorMessage = GetDelegate<NvAPI_GetErrorMessage_Delegate>(0x6C2D0424);
                _createSession = GetDelegate<NvAPI_DRS_CreateSession_Delegate>(0x0694D52E);
                _destroySession = GetDelegate<NvAPI_DRS_DestroySession_Delegate>(0xDAD9CFF8);
                _loadSettings = GetDelegate<NvAPI_DRS_LoadSettings_Delegate>(0x375ECD65);
                _saveSettings = GetDelegate<NvAPI_DRS_SaveSettings_Delegate>(0xFC142711);
                _getBaseProfile = GetDelegate<NvAPI_DRS_GetBaseProfile_Delegate>(0xDA853D79);
                _findProfileByName = GetDelegate<NvAPI_DRS_FindProfileByName_Delegate>(0x7E4A9A0B);
                _findAppByName = GetDelegate<NvAPI_DRS_FindApplicationByName_Delegate>(0xEEE962DB);
                _getSetting = GetDelegate<NvAPI_DRS_GetSetting_Delegate>(0x73BF8338);
                _setSetting = GetDelegate<NvAPI_DRS_SetSetting_Delegate>(0x577DD202);

                if (_init == null || _createSession == null || _loadSettings == null)
                {
                    _available = false;
                    _initialized = true;
                    return;
                }

                var status = _init();
                if (status == NvStatus.OK)
                {
                    status = _createSession(out _sessionHandle);
                    if (status == NvStatus.OK && _sessionHandle != IntPtr.Zero)
                    {
                        _loadSettings(_sessionHandle);
                        _available = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NvApiDrsNative] Init failed: {ex.Message}");
                _available = false;
            }
            finally
            {
                _initialized = true;
            }
        }
    }

    private static T? GetDelegate<T>(uint id) where T : Delegate
    {
        try
        {
            var ptr = nvapi_QueryInterface(id);
            if (ptr != IntPtr.Zero)
            {
                return Marshal.GetDelegateForFunctionPointer<T>(ptr);
            }
        }
        catch { }
        return null;
    }

    public static string GetErrorMessage(NvStatus status)
    {
        if (_getErrorMessage != null)
        {
            var sb = new StringBuilder(512);
            if (_getErrorMessage(status, sb) == NvStatus.OK)
            {
                return sb.ToString();
            }
        }
        return status.ToString();
    }

    public static IntPtr GetBaseProfileHandle()
    {
        EnsureInitialized();
        if (!_available || _getBaseProfile == null || _sessionHandle == IntPtr.Zero)
            return IntPtr.Zero;

        if (_getBaseProfile(_sessionHandle, out var hProfile) == NvStatus.OK)
        {
            return hProfile;
        }
        return IntPtr.Zero;
    }

    public static IntPtr FindProfileByName(string profileName)
    {
        EnsureInitialized();
        if (!_available || _findProfileByName == null || _sessionHandle == IntPtr.Zero)
            return IntPtr.Zero;

        if (_findProfileByName(_sessionHandle, profileName, out var hProfile) == NvStatus.OK)
        {
            return hProfile;
        }
        return IntPtr.Zero;
    }

    public static IntPtr FindProfileByApplication(string exeName)
    {
        EnsureInitialized();
        if (!_available || _findAppByName == null || _sessionHandle == IntPtr.Zero)
            return IntPtr.Zero;

        if (_findAppByName(_sessionHandle, exeName, out var hProfile, IntPtr.Zero) == NvStatus.OK)
        {
            return hProfile;
        }
        return IntPtr.Zero;
    }

    public static (bool success, uint value, NvStatus status) GetDwordSetting(IntPtr hProfile, uint settingId)
    {
        EnsureInitialized();
        if (!_available || _getSetting == null || _sessionHandle == IntPtr.Zero || hProfile == IntPtr.Zero)
            return (false, 0, NvStatus.ERROR);

        var setting = new NVDRS_SETTING
        {
            version = NVDRS_SETTING_VER,
            settingId = settingId
        };

        var status = _getSetting(_sessionHandle, hProfile, settingId, ref setting);
        if (status == NvStatus.OK)
        {
            return (true, setting.u32CurrentValue, status);
        }
        return (false, 0, status);
    }

    public static (bool success, NvStatus status) SetDwordSetting(IntPtr hProfile, uint settingId, uint value)
    {
        EnsureInitialized();
        if (!_available || _setSetting == null || _sessionHandle == IntPtr.Zero || hProfile == IntPtr.Zero)
            return (false, NvStatus.ERROR);

        var setting = new NVDRS_SETTING
        {
            version = NVDRS_SETTING_VER,
            settingId = settingId,
            settingType = 0, // DWORD
            u32CurrentValue = value
        };

        var status = _setSetting(_sessionHandle, hProfile, ref setting);
        return (status == NvStatus.OK, status);
    }

    public static bool SaveSettings()
    {
        EnsureInitialized();
        if (!_available || _saveSettings == null || _sessionHandle == IntPtr.Zero)
            return false;

        return _saveSettings(_sessionHandle) == NvStatus.OK;
    }

    public static void Shutdown()
    {
        lock (_syncLock)
        {
            if (_sessionHandle != IntPtr.Zero)
            {
                _destroySession?.Invoke(_sessionHandle);
                _sessionHandle = IntPtr.Zero;
            }
            if (_available)
            {
                _unload?.Invoke();
                _available = false;
            }
            _initialized = false;
        }
    }
}
