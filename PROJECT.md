# Project: XTR-Unlocker Native Gaming Engine & Studio

## Architecture
- **Engine Core & Runtime**:
  - `desktop/src/Runtime/OptiScaler/OptiScaler.ini`: Baseline configuration for OptiScaler 0.9.4 runtime with emerald branded HUD and anti-ghosting flags.
  - `desktop/src/Runtime/OptiScaler/xtr-runtime.json`: Cryptographic package manifest pinning SHA-256 and byte length of all 18 runtime files.
  - `desktop/src/Data/Runtime/DeploymentPlanner.cs`: Preset generation for `NativeDlssG` (clean hardware bypass) and `DlssWithFrameGeneration` (universal FSR 3.1 frame generation bridge with anti-ghosting shield).
  - `desktop/src/Data/Runtime/RuntimeConfigService.cs`: Section-bounded INI parsing, atomic mutation, and isolated section replacement engine.
- **Shared Memory IPC & Telemetry Engine**:
  - `desktop/src/Engine/Ipc/XtrTelemetryPacket.cs`: 112-byte packed binary struct (`Pack=1`, magic `0x58545231`), Seqlock tearing protection.
  - `desktop/src/Engine/Ipc/XtrTelemetrySharedMemory.cs`: Bidirectional, zero-allocation memory-mapped file `Local\XtrTelemetrySharedMem`.
  - `desktop/src/Engine/Telemetry/XtrEtwTelemetryService.cs`: Non-invasive kernel ETW consumer (`Microsoft-Windows-DXGI` & `Microsoft-Windows-D3D12`), 1% lows and frametime variance.
  - `desktop/src/Engine/Lifecycle/ProcessWatcher.cs`: Background game lifecycle watcher (<500ms detection, High process priority elevation, GPU scheduling, auto-minimize to tray).
- **In-Game Branded HUD Overlay**:
  - `desktop/src/Engine/Hud/XtrHudFormatter.cs`: Emerald branded HUD layout `[ XTR ENGINE ] • FPS: 60 (FG 2X) • 16.6 ms • RTX 2050` with hotkeys (F10 toggle, F9 cycle, F12 FG).
  - `desktop/src/Engine/Hud/HudPatcher.cs`: OptiScaler `.rdata` format string patcher and companion telemetry bridge.
- **Unified Native Fluent Studio (lepoco/wpfui)**:
  - `desktop/src/UI/MainWindow.xaml`: Native Windows 11 Fluent UI with Mica/Acrylic glass backdrops (`DwmSetWindowAttribute`), dark theme, 35–45 MB RAM footprint, 0.0% gaming CPU.
  - Three integrated functional hubs:
    1. Upscaler DLL Switcher (DLSS, DLSS-G, FSR 3.1, XeSS version management).
    2. Frame Gen Enabler (Forza Horizon 5/6, Cyberpunk, generic DX12/Vulkan).
    3. Active Engine Companion (Live dashboard, hardware sensors, session stats).
  - Clean C# architecture with zero legacy DLSS Swapper namespaces or leftover files.
  - Code quality enforced via Roslyn `Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`, and `Meziantou.Analyzer` with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- **Packaging & Delivery**:
  - `scripts/build-windows.ps1`: Release compilation, test verification, single-file SFX installer `dist/XTR-Unlocker-Setup.exe`, portable ZIP distribution.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | `AccAddPerFrame = 0.05` | Disocclusion accumulation flush eliminating 50ms trailing ghost smear | M1 | ORIGINAL_REQUEST R3 |
| 2 | `MinDisOccAcc = -0.50` | Disocclusion delay preventing premature history blending | M1 | ORIGINAL_REQUEST R3 |
| 3 | `DisableReactiveMask = false` | Preserves native reactive masks for windshields, tire smoke, dust particles | M1 | ORIGINAL_REQUEST R3 |
| 4 | `MakeMVCopy = true` & `MakeDepthCopy = true` | Eliminates DX12 async compute race conditions on motion vectors | M1 | ORIGINAL_REQUEST R3 |
| 5 | `DlssReactiveMaskBias = 0.45` | Prevents historical sample persistence on high-velocity edges | M1 | ORIGINAL_REQUEST R3 |
| 6 | `ReactiveScale = 2.0` | Amplifies reactiveness on fast-moving vehicle silhouettes | M1 | ORIGINAL_REQUEST R3 |
| 7 | `RenderPresetForAll = 5` | Forces DLSS Preset E (High-Motion Smear Suppression Champion) | M1 | ORIGINAL_REQUEST R3 |
| 8 | Native Streamline Bridge (Nukem9) | Forza Horizon 5/6 profile with `version.dll` proxy and `AlwaysTrackHeaps=false` | M1 | ORIGINAL_REQUEST R3 |
| 9 | Multi-Backend FG Dispatcher | Dynamic routing to Nukem9 (`nukems`), AMD FSR 3.1 (`fsrfg`), and XeFG (`xefg`) | M1 | ORIGINAL_REQUEST R3 |
| 10 | Section-Aware INI Writing | Strict section isolation (`[InitFlags]`, `[FrameGen]`, `[FSR]`, `[OptiFG]`) with atomic temporary replacement | M1 | rea_discovery_report §2.3 |
| 11 | Fix CA1416 Platform Warnings | Mark Windows-specific hardware detector APIs with `[SupportedOSPlatform("windows")]` | M1 | explorer_survey_1 §1.2 |
| 12 | `XtrTelemetryPacket` Binary Layout | 112-byte packed struct (`Pack=1`, magic `0x58545231`, Seqlock sequence counter, GPU name, VRAM, temp, FG mode, FPS, frametime) | M2 | ORIGINAL_REQUEST R2 |
| 13 | Lock-Free Shared Memory IPC | Named memory map `Local\XtrTelemetrySharedMem` with zero-allocation double-buffering | M2 | ORIGINAL_REQUEST R2 |
| 14 | Seqlock Tearing Protection | Monotonic counter sequence check for 60–1000 Hz high-frequency writes | M2 | rea_discovery_report §1.3 |
| 15 | Non-Invasive ETW Telemetry Engine | Kernel event consumer for `Microsoft-Windows-DXGI` (`{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}`) | M3 | ORIGINAL_REQUEST R2 |
| 16 | D3D12 Queue Event Monitoring | GPU execution and CPU wait tracking via `Microsoft-Windows-D3D12` (`{5D8087DD-3A9B-4F94-AA1E-164706CA557F}`) | M3 | rea_discovery_report §3.2 |
| 17 | 1% & 0.1% Low Percentile Calculation | Ring buffer percentile calculation for frame-time variance and stutter tracking | M3 | ORIGINAL_REQUEST R2 |
| 18 | Background ProcessWatcher | Detects game launch within 500ms via WMI / process polling | M3 | ORIGINAL_REQUEST R2 |
| 19 | Process Priority Elevation | Elevates target game process to `ProcessPriorityClass.High` on launch | M3 | ORIGINAL_REQUEST R2 |
| 20 | GPU Scheduling Optimization | Sets optimal GPU scheduling and power preference for active game | M3 | ORIGINAL_REQUEST R2 |
| 21 | Automatic Minimize-to-Tray | Minimizes Studio to system tray on game launch, pausing UI loop for 0.0% background CPU | M3 | ORIGINAL_REQUEST R1 |
| 22 | Branded In-Game Telemetry HUD | Emerald styled HUD layout `[ XTR ENGINE ] • FPS: 60 (FG 2X) • 16.6 ms • RTX 2050` | M4 | ORIGINAL_REQUEST R2 |
| 23 | In-Game Hotkey Controls | F10 (`0x79`) HUD toggle, F9 (`0x78`) cycle modes, F12 (`0x7B`) FG toggle | M4 | rea_discovery_report §1.4 |
| 24 | HUD Palette & Aesthetics | Emerald green accent (`0.20, 0.78, 0.35`) with dark translucent glass backdrop (`0.08, 0.09, 0.11, 0.92`) | M4 | rea_discovery_report §1.4 |
| 25 | Companion Telemetry Bridge | Connects `Local\XtrTelemetrySharedMem` to in-game overlay | M4 | rea_discovery_report §1.3 |
| 26 | `lepoco/wpfui` Modern Studio | Windows 11 Fluent UI with native Mica/Acrylic backdrops (`DwmSetWindowAttribute`) | M5 | ORIGINAL_REQUEST R1 |
| 27 | Ultra-Low Footprint (<45 MB RAM) | Pure native WPF UI, zero Chromium/Electron/WebView2 baggage, ~200ms cold startup | M5 | ORIGINAL_REQUEST R1 |
| 28 | Upscaler DLL Switcher Hub | Direct version management for DLSS, DLSS-G, FSR 3.1, and XeSS | M5 | ORIGINAL_REQUEST R1 |
| 29 | Frame Gen Enabler Hub | One-click deployment for Forza Horizon 5/6, Cyberpunk 2077, and generic DX12/Vulkan games | M5 | ORIGINAL_REQUEST R1 |
| 30 | Active Engine Companion Hub | Real-time companion view with live telemetry charts, hardware sensors, and session stats | M5 | ORIGINAL_REQUEST R1 |
| 31 | Clean Namespace Architecture | Zero legacy DLSS Swapper namespaces or leftover files | M5 | ORIGINAL_REQUEST R1 |
| 32 | Static Analyzers & Quality Gates | `Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`, `Meziantou.Analyzer` with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | M5 | ORIGINAL_REQUEST R1 |
| 33 | E2E Test Suite Pass (Tiers 1-4) | 100% pass rate across feature coverage, boundaries, pairwise combinations, and real-world scenarios | M6 | Acceptance Criteria |
| 34 | Adversarial Coverage Hardening (Tier 5) | Challenger stress tests on Seqlock concurrency, rapid process lifecycle, and edge cases | M6 | Acceptance Criteria |
| 35 | Forensic Integrity Verification | Forensic Auditor verification ensuring genuine implementations with zero cheating | M6 | Acceptance Criteria |
| 36 | Single-File Installer Packaging | Verified `dist/XTR-Unlocker-Setup.exe` 7-Zip SFX installer and standalone release ZIPs | M7 | Acceptance Criteria |
| 37 | Git Versioning & Release Push | Clean atomic commits and push to GitHub repository | M7 | Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Anti-Ghosting Calibration & Multi-Backend Presets | `DeploymentPlanner.cs`, `RuntimeConfigService.cs`, `FrameGenerationPlannerTests.cs`, anti-ghosting flags | none | PLANNED |
| M2 | Shared Memory IPC & Telemetry Packet Engine | `XtrTelemetryPacket.cs`, `XtrTelemetrySharedMemory.cs`, IPC tests | none | PLANNED |
| M3 | Non-Invasive ETW Telemetry & Process Watcher | `XtrEtwTelemetryService.cs`, `ProcessWatcher.cs`, telemetry tests | M2 | PLANNED |
| M4 | In-Game Branded Emerald HUD | `XtrHudFormatter.cs`, `HudPatcher.cs`, `OptiScaler.ini` HUD styling, hotkey controls | M1, M2 | PLANNED |
| M5 | Unified Native Fluent Studio (lepoco/wpfui) & Analyzers | Clean C# architecture, wpfui integration, 3 hubs, Roslyn/Sonar/Meziantou analyzers, 0 warnings | M1, M2, M3, M4 | PLANNED |
| M6 | E2E Test Suite (Tiers 1-4) & Adversarial Hardening (Tier 5) | Complete E2E test verification, adversarial coverage audit, Forensic Audit | M1..M5 | PLANNED |
| M7 | Release Packaging & Delivery | `dist/XTR-Unlocker-Setup.exe`, standalone ZIP, git commits, push, Sentinel completion report | M5, M6 | PLANNED |

## Interface Contracts
### `XtrTelemetryPacket` (Binary Struct)
- Total size: exactly 112 bytes (`Pack = 1`).
- Offset 0: `uint Magic` = `0x58545231` ("XTR1").
- Offset 4: `uint SequenceNumber` (Seqlock monotonic counter).
- Offset 8: `uint ProcessId` (Target game PID).
- Offset 12: `fixed byte GpuName[64]` (UTF-8 null-terminated).
- Offset 76: `float GpuTemperatureC`.
- Offset 80: `float GpuUtilization`.
- Offset 84: `uint VramUsedMb`.
- Offset 88: `uint VramTotalMb`.
- Offset 92: `uint FrameGenMode` (0=Off, 1=NativeDLSS, 2=FSR31_Bridge, 3=XeFG).
- Offset 96: `uint Multiplier` (2x, 3x, 4x).
- Offset 100: `uint AntiGhostingShield` (1=Active, 0=Inactive).
- Offset 104: `float InGameFps`.
- Offset 108: `float InGameFrametimeMs`.

### `XtrTelemetrySharedMemory` ↔ `XtrEtwTelemetryService` / `XtrHudFormatter`
- Shared memory name: `Local\XtrTelemetrySharedMem`.
- Reader validates Seqlock before and after reading: if `SequenceNumber % 2 != 0` or counter changed, reader spins and retries.
- Zero-allocation reads and writes via `MemoryMappedViewAccessor` or `Span<byte>`.

### `DeploymentPlanner` Presets
- `DlssWithFrameGeneration` produces:
  - `[InitFlags] DisableReactiveMask="false"`
  - `[FSR] UseReactiveMaskForTransparency="true"`, `DlssReactiveMaskBias="0.45"`, `ReactiveScale="2.0"`, `AccAddPerFrame="0.05"`, `MinDisOccAcc="-0.50"`
  - `[OptiFG] MakeMVCopy="true"`, `MakeDepthCopy="true"`, `HUDFix="true"`, `HUDFixExtended="true"`
  - `[DLSS] RenderPresetOverride="true"`, `RenderPresetForAll="5"`
- Forza Horizon / Streamline profile sets:
  - `version.dll` proxy
  - `[OptiFG] AlwaysTrackHeaps="false"`

## Code Layout
- `desktop/src/Runtime/OptiScaler/OptiScaler.ini`: Baseline configuration
- `desktop/src/Runtime/OptiScaler/xtr-runtime.json`: Manifest file
- `desktop/src/Data/Runtime/DeploymentPlanner.cs`: Deployment preset definitions
- `desktop/src/Data/Runtime/RuntimeConfigService.cs`: Section-bounded INI parsing helper
- `desktop/src/Engine/Ipc/XtrTelemetryPacket.cs`: Binary IPC telemetry packet
- `desktop/src/Engine/Ipc/XtrTelemetrySharedMemory.cs`: Shared memory IPC implementation
- `desktop/src/Engine/Telemetry/XtrEtwTelemetryService.cs`: Non-invasive ETW event consumer
- `desktop/src/Engine/Lifecycle/ProcessWatcher.cs`: Background game process lifecycle watcher
- `desktop/src/Engine/Hud/XtrHudFormatter.cs`: Emerald in-game HUD overlay formatter
- `desktop/src/UI/`: Modern Windows 11 Fluent UI (lepoco/wpfui)
- `desktop/tests/XTR.Tests/`: Unit and integration test suite
- `desktop/tests/XTR.E2ETests/`: Opaque-box E2E test suite (Tiers 1-5)
- `scripts/build-windows.ps1`: Release compilation and installer packaging
