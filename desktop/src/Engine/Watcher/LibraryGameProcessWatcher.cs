using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DLSS_Swapper.Data;
using DLSS_Swapper.Data.Runtime;
using DLSS_Swapper.Engine.IPC;
using DLSS_Swapper.Engine.Telemetry;

namespace DLSS_Swapper.Engine.Watcher;

public sealed class GameProcessEventArgs : EventArgs
{
    public Game Game { get; }
    public Process Process { get; }
    public RuntimeInstallManifest? Manifest { get; }
    public RuntimeIntegrationProfile? Profile { get; }
    public TimeSpan Duration { get; }
    public float AverageFps { get; }

    public GameProcessEventArgs(
        Game game,
        Process process,
        RuntimeInstallManifest? manifest = null,
        RuntimeIntegrationProfile? profile = null,
        TimeSpan duration = default,
        float averageFps = 0f)
    {
        Game = game;
        Process = process;
        Manifest = manifest;
        Profile = profile;
        Duration = duration;
        AverageFps = averageFps;
    }
}

/// <summary>
/// Monitors process execution of library games to dynamically engage XTR Turbo Boost,
/// initialize IPC shared memory, and stream real-time PresentMon/ETW telemetry.
/// </summary>
public sealed class LibraryGameProcessWatcher : IDisposable
{
    private readonly object _syncLock = new();
    private readonly ConcurrentDictionary<int, TrackedSession> _activeSessions = new();
    private readonly PresentMonTelemetryEngine _telemetryEngine;
    private readonly XtrSharedMemory _sharedMemory;
    private CancellationTokenSource? _watcherCts;
    private Task? _watcherTask;
    private bool _disposed;

    public event EventHandler<GameProcessEventArgs>? GameStarted;
    public event EventHandler<GameProcessEventArgs>? GameExited;
    public event EventHandler<XtrTelemetryFrame>? TelemetryTicked;

    public bool IsWatching => _watcherTask != null && !_watcherTask.IsCompleted;
    public IReadOnlyCollection<Process> ActiveGameProcesses => _activeSessions.Values.Select(s => s.Process).ToList();

    private sealed class TrackedSession
    {
        public required Game Game { get; init; }
        public required Process Process { get; init; }
        public required DateTimeOffset StartTime { get; init; }
        public RuntimeInstallManifest? Manifest { get; init; }
        public RuntimeIntegrationProfile? Profile { get; init; }
        public float LastFps { get; set; }
    }

    public LibraryGameProcessWatcher(
        PresentMonTelemetryEngine telemetryEngine,
        XtrSharedMemory sharedMemory)
    {
        _telemetryEngine = telemetryEngine ?? throw new ArgumentNullException(nameof(telemetryEngine));
        _sharedMemory = sharedMemory ?? throw new ArgumentNullException(nameof(sharedMemory));

        _telemetryEngine.TelemetryUpdated += OnTelemetryUpdated;
    }

    public void Start(int pollIntervalMs = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_syncLock)
        {
            Stop();
            _watcherCts = new CancellationTokenSource();
            _watcherTask = Task.Run(() => WatcherLoopAsync(pollIntervalMs, _watcherCts.Token));
        }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (_watcherCts != null)
            {
                try
                {
                    _watcherCts.Cancel();
                    _watcherTask?.Wait(TimeSpan.FromMilliseconds(500));
                }
                catch { }
                finally
                {
                    _watcherCts.Dispose();
                    _watcherCts = null;
                    _watcherTask = null;
                }
            }

            foreach (var session in _activeSessions.Values)
            {
                try
                {
                    _telemetryEngine.Stop();
                }
                catch { }
            }
            _activeSessions.Clear();
        }
    }

    private void OnTelemetryUpdated(object? sender, XtrTelemetryFrame frame)
    {
        if (_activeSessions.TryGetValue((int)frame.ProcessId, out var session))
        {
            session.LastFps = frame.Fps;

            // Update shared memory telemetry
            var telemetry = new XtrTelemetryData
            {
                Fps = frame.Fps,
                FrameTimeMs = frame.FrameTimeMs,
                CpuFrameTimeMs = frame.CpuFrameTimeMs,
                GpuFrameTimeMs = frame.GpuFrameTimeMs,
                PresentLatencyMs = frame.PresentLatencyMs,
                DisplayLatencyMs = frame.DisplayLatencyMs,
                VramBudgetBytes = frame.VramBudgetMb * 1024 * 1024,
                VramUsedBytes = frame.VramUsedMb * 1024 * 1024,
                GpuTemperatureC = frame.GpuTemperatureC,
                GpuPowerW = frame.GpuPowerW,
                GpuClockMhz = frame.GpuClockMhz,
                TargetFrameRate = 0
            };

            _sharedMemory.UpdateTelemetry(ref telemetry);
        }

        TelemetryTicked?.Invoke(this, frame);
    }

    private async Task WatcherLoopAsync(int pollIntervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
                ScanProcesses();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
    }

    private void ScanProcesses()
    {
        // 1. Detect process exits
        foreach (var (pid, session) in _activeSessions)
        {
            bool exited = false;
            try
            {
                exited = session.Process.HasExited;
            }
            catch
            {
                exited = true;
            }

            if (exited)
            {
                _activeSessions.TryRemove(pid, out _);
                _telemetryEngine.Stop();

                var duration = DateTimeOffset.UtcNow - session.StartTime;
                GameExited?.Invoke(this, new GameProcessEventArgs(
                    session.Game,
                    session.Process,
                    session.Manifest,
                    session.Profile,
                    duration,
                    session.LastFps));
            }
        }

        // 2. Discover running game library processes
        var games = GameManager.Instance.GetSynchronisedGamesListCopy();
        if (games is null || games.Count == 0) return;

        var runningProcesses = Process.GetProcesses();
        foreach (var proc in runningProcesses)
        {
            if (_activeSessions.ContainsKey(proc.Id))
                continue;

            string? processPath = null;
            try
            {
                processPath = proc.MainModule?.FileName;
            }
            catch
            {
                // Access denied or 32/64 bit mismatch on system processes
                continue;
            }

            if (string.IsNullOrWhiteSpace(processPath))
                continue;

            // Match process against library games
            var matchedGame = games.FirstOrDefault(g =>
                !string.IsNullOrWhiteSpace(g.InstallPath) &&
                processPath.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(g.InstallPath)), StringComparison.OrdinalIgnoreCase));

            if (matchedGame != null)
            {
                OnGameProcessDetected(matchedGame, proc);
            }
        }
    }

    private void OnGameProcessDetected(Game game, Process process)
    {
        var manifest = RuntimeInstallManifestStore.LoadAsync(game.ID).GetAwaiter().GetResult();
        var profile = RuntimeIntegrationProfileStore.LoadAsync(game.ID).GetAwaiter().GetResult();

        var session = new TrackedSession
        {
            Game = game,
            Process = process,
            StartTime = DateTimeOffset.UtcNow,
            Manifest = manifest,
            Profile = profile
        };

        if (_activeSessions.TryAdd(process.Id, session))
        {
            // Sync initial state to IPC shared memory
            byte upscalerCode = profile?.SelectedUpscaler?.ToLowerInvariant() switch
            {
                "dlss" => 1,
                "fsr" or "fsr31" => 2,
                "xess" => 3,
                _ => 1
            };

            byte presetId = 5; // Default Preset E
            if (profile != null && profile.Options.TryGetPropertyValue("RenderPresetForAll", out var presetNode) &&
                byte.TryParse(presetNode?.ToString(), out var parsedPreset))
            {
                presetId = parsedPreset;
            }

            bool fgActive = profile?.FrameGenerationEnabled?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            bool isTurbo = profile != null && profile.Options.TryGetPropertyValue("Preset", out var presetNameNode) &&
                           string.Equals(presetNameNode?.ToString(), "TurboBoost", StringComparison.OrdinalIgnoreCase);

            var engineState = new XtrEngineState
            {
                TargetProcessId = (uint)process.Id,
                GameTitle = game.Title ?? string.Empty,
                ActiveUpscaler = upscalerCode,
                UpscalerMode = 0, // Quality
                ActiveDlssPreset = presetId,
                FrameGenerationActive = (byte)(fgActive ? 1 : 0),
                FrameGenMultiplier = 2,
                InGameHudEnabled = 1,
                InGameHudPosition = 0,
                HudHotkey = 0x2D, // VK_INSERT
                TurboBoostActive = (byte)(isTurbo ? 1 : 0)
            };

            _sharedMemory.UpdateEngineState(ref engineState);

            // Start telemetry collection for this process
            _telemetryEngine.Start((uint)process.Id);

            GameStarted?.Invoke(this, new GameProcessEventArgs(game, process, manifest, profile));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _telemetryEngine.TelemetryUpdated -= OnTelemetryUpdated;
        Stop();
    }
}
