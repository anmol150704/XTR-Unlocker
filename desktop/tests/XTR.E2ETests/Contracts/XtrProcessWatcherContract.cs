using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace XTR.E2ETests.Contracts;

public sealed class XtrProcessWatcherContract : IDisposable
{
    public const int MaxDetectionLatencyMs = 500;
    public const ProcessPriorityClass TargetPriority = ProcessPriorityClass.High;

    private static readonly HashSet<string> s_ignoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "svchost.exe",
        "taskmgr.exe",
        "services.exe",
        "lsass.exe",
        "smss.exe",
        "csrss.exe",
        "winlogon.exe"
    };

    private readonly HashSet<string> _watchedExecutables = new(StringComparer.OrdinalIgnoreCase);
    private bool _isMinimizedToTray;
    private bool _disposed;

    public event Action<int, string>? OnGameLaunched;
    public event Action<int, string>? OnGameExited;
    public event Action<bool>? OnTrayStateChanged;

    public bool IsMinimizedToTray => _isMinimizedToTray;
    public IReadOnlyCollection<string> WatchedExecutables => _watchedExecutables;

    public void RegisterWatchedExecutable(string exeName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(exeName)) return;
        _watchedExecutables.Add(Path.GetFileName(exeName));
    }

    public static bool IsSystemProcess(string exeName)
    {
        string filename = Path.GetFileName(exeName);
        return s_ignoredProcesses.Contains(filename);
    }

    public bool ShouldMonitor(string exeName)
    {
        string filename = Path.GetFileName(exeName);
        if (IsSystemProcess(filename)) return false;
        return _watchedExecutables.Contains(filename);
    }

    public bool SimulateGameLaunch(int pid, string exeName, long detectionLatencyMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ShouldMonitor(exeName))
            return false;

        if (detectionLatencyMs > MaxDetectionLatencyMs)
            return false;

        // Auto minimize to tray
        _isMinimizedToTray = true;
        OnTrayStateChanged?.Invoke(_isMinimizedToTray);

        OnGameLaunched?.Invoke(pid, exeName);
        return true;
    }

    public bool SimulateGameExit(int pid, string exeName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ShouldMonitor(exeName))
            return false;

        _isMinimizedToTray = false;
        OnTrayStateChanged?.Invoke(_isMinimizedToTray);

        OnGameExited?.Invoke(pid, exeName);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _watchedExecutables.Clear();
        _isMinimizedToTray = false;
        _disposed = true;
    }
}
