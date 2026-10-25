# Project: XTR-Unlocker Unified DLSS Engine & Apple Liquid UI

## Architecture
- **Engine Core & Runtime**:
  - `desktop/src/Runtime/OptiScaler/OptiScaler.ini`: Baseline configuration for OptiScaler 0.9.4 runtime.
  - `desktop/src/Runtime/OptiScaler/xtr-runtime.json`: Cryptographic package manifest pinning SHA-256 and byte length of runtime files.
  - `desktop/src/Data/Runtime/DeploymentPlanner.cs`: Preset generation for `NativeDlssG` (clean hardware bypass) and `DlssWithFrameGeneration` (universal FSR 3.1 frame generation bridge with anti-ghosting shield).
- **Control Center & Section-Aware Configuration**:
  - `desktop/src/UserControls/GameControlModel.cs`: Reactive UI view model managing game installation, real-time telemetry, hotkeys, and section-aware INI mutations.
  - `desktop/src/Data/Runtime/RuntimeConfigService.cs`: Section-bounded INI parsing and mutation engine.
- **Apple Liquid Design System**:
  - `desktop/src/Themes/XtrDesign.xaml`: Apple Pro Studio / VisionOS translucent acrylic glass themes, squircle geometries, and iridescent gradient borders.
  - `desktop/src/UserControls/GameControl.xaml`: Control center with Segoe Fluent iconography, DLSS version badges, and real-time engine health indicators.
- **Test & Validation Harness**:
  - `desktop/tests/XTR.Tests/`: 29 MSTest unit tests ensuring 100% regression-free operation across deployment planning, version formatting, health checks, and package integrity.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Baseline Upscaler Defaults | Set `Dx11Upscaler=dlss`, `Dx12Upscaler=dlss`, `VulkanUpscaler=dlss` in `OptiScaler.ini` | M1 | Survey §1.1 |
| 2 | Runtime Manifest Pinning | Recalculate and update `sha256` and `sizeBytes` in `xtr-runtime.json` for `OptiScaler.ini` | M1 | Survey §1.2 |
| 3 | NativeDlssG Hardware Bypass | In `DeploymentPlanner.cs`, set `[FrameGen] Enabled=false, FGInput=nofg, FGOutput=nofg; [Spoofing] StreamlineSpoofing=true, SpoofHAGS=true; [Upscalers] Dx12Upscaler=dlss` | M1 | Survey §1.3 |
| 4 | DlssWithFrameGeneration Routing | In `DeploymentPlanner.cs`, route upscaler to `dlss`, FG to `fsrfg`, and attach complete anti-ghosting shield | M1 | Survey §1.3 |
| 5 | Anti-Ghosting Section Realignment | Realign anti-ghosting keys in `DeploymentPlanner.cs` to `[InitFlags]`, `[FrameGen]`, `[FSR]`, and `[OptiFG]` | M1 | Survey §1.3 |
| 6 | Unit Test Assertions Update | Update `FrameGenerationPlannerTests.cs` to validate new preset structures (`NativeDlssG` bypass and `[FSR]` anti-ghosting keys) | M1 | Survey §1.3 |
| 7 | Section-Aware INI Writing | Update `GameControlModel.cs` to write INI keys bounded to their specific sections (`[InitFlags]`, `[FrameGen]`, `[FSR]`, `[OptiFG]`) | M2 | Survey §1.5 |
| 8 | Anti-Ghosting Shield Live Toggles | Update `ApplyAntiGhostingSetting` in `GameControlModel.cs` with complete key coverage across sections | M2 | Survey §1.5 |
| 9 | Iridescent Acrylic Border Styling | Add `XtrIridescentBorderBrush` and polish squircle card borders in `Themes/XtrDesign.xaml` | M3 | Survey §1.2 |
| 10 | Segoe Fluent Icons & Mojibake Check | Verify 7 Fluent glyphs (`&#xE945;`, `&#xE735;`, `&#xE72C;`, `&#xE730;`, `&#xE9D9;`, `&#xE72E;`, `&#xE90F;`), DLSS badges, and zero mojibake | M3 | Survey §1.6 |
| 11 | Full Suite Regression & Release Verification | Verify 29 unit tests pass and app compiles with 0 errors and 0 warnings under .NET 10 x64 Release | M4 | Survey §1.1 |
| 12 | Anti-Hallucination Forensic Audit | Validate on-disk integrity, lack of mock/dummy implementations, and strict conformance to requirements | M4 | Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Runtime Engine & Presets | `OptiScaler.ini`, `xtr-runtime.json`, `DeploymentPlanner.cs`, `FrameGenerationPlannerTests.cs` | none | DONE (32 tests pass, build 0 err/warn) |
| M2 | Section-Aware INI Writing | `GameControlModel.cs` section-aware INI updates and anti-ghosting toggle integration | M1 | DONE (35 tests pass, atomic section updates) |
| M3 | Apple Liquid UI & Fluent Design | `XtrDesign.xaml`, XAML styling, squircle card borders, Segoe Fluent glyphs, zero mojibake | none | IN_PROGRESS (worker_m3: f42f8784-9223-4d4f-bfff-3f6e8a90e8e0) |
| M4 | Verification & Forensic Audit | Run 29 unit tests, .NET 10 x64 Release build, Challenger adversarial tests, Forensic Auditor integrity check | M1, M2, M3 | PLANNED |

## Interface Contracts
### `DeploymentPlanner.cs` ↔ `FrameGenerationPlannerTests.cs`
- `NativeDlssG` preset produces:
  - `[Upscalers] Dx12Upscaler="dlss"`, `Dx11Upscaler="dlss"`, `VulkanUpscaler="dlss"`
  - `[FrameGen] Enabled="false"`, `FGInput="nofg"`, `FGOutput="nofg"`
  - `[Spoofing] StreamlineSpoofing="true"`, `SpoofHAGS="true"`
- `DlssWithFrameGeneration` preset produces:
  - `[Upscalers] Dx12Upscaler="dlss"`
  - `[FrameGen] Enabled="true"`, `FGInput="dlssg"|"upscaler"`, `FGOutput="fsrfg"`, `DrawUIOverFG="true"`, `DisableUI="false"`, `DisableHudless="false"`
  - `[InitFlags] DisableReactiveMask="false"`
  - `[FSR] UseReactiveMaskForTransparency="true"`, `DlssReactiveMaskBias="0.45"`, `ReactiveScale="2.0"`, `AccAddPerFrame="0.05"`, `MinDisOccAcc="-0.5"`
  - `[OptiFG] HUDFix="true"`, `HUDFixExtended="true"`, `DisableHUDFix="false"`, `MakeDepthCopy="true"`, `MakeMVCopy="true"`

### `GameControlModel.cs` ↔ `OptiScaler.ini`
- `UpdateOptiScalerConfigSetting(section, key, value)` or batch update writes to targeted section headers.
- Never appends orphaned keys to EOF.
- Updates live files atomically using section-bounded line replacement.

## Code Layout
- `desktop/src/Runtime/OptiScaler/OptiScaler.ini`: Baseline configuration
- `desktop/src/Runtime/OptiScaler/xtr-runtime.json`: Manifest file
- `desktop/src/Data/Runtime/DeploymentPlanner.cs`: Deployment preset definitions
- `desktop/src/Data/Runtime/RuntimeConfigService.cs`: INI parsing helper
- `desktop/src/UserControls/GameControlModel.cs`: UI control view model & INI writer
- `desktop/src/Themes/XtrDesign.xaml`: UI styling tokens and brushes
- `desktop/src/UserControls/GameControl.xaml`: UI layout & Segoe Fluent glyphs
- `desktop/tests/XTR.Tests/FrameGenerationPlannerTests.cs`: Preset unit tests
