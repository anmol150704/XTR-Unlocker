# E2E Test Infra: XTR-Unlocker

## Test Philosophy
- Opaque-box, requirement-driven. Derived from `ORIGINAL_REQUEST.md` and user-facing specifications, not internal implementation design.
- Methodology: Category-Partition + Boundary Value Analysis + Pairwise Combinatorial Testing + Real-World Workload Testing.

## Feature Inventory
| # | Feature | Source (Requirement) | Tier 1 | Tier 2 | Tier 3 |
|---|---------|---------------------|:------:|:------:|:------:|
| 1 | Anti-Ghosting Calibration (`AccAddPerFrame=0.05`, `MinDisOccAcc=-0.50`) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 2 | Responsive Mask Preservation (`DisableReactiveMask=false`) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 3 | Async Compute MV & Depth Clones (`MakeMVCopy=true`, `MakeDepthCopy=true`) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 4 | High-Motion Smear Suppression (`DlssReactiveMaskBias=0.45`, `ReactiveScale=2.0`, `RenderPresetForAll=5`) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 5 | Streamline Bridge & Heap Passthrough (`AlwaysTrackHeaps=false`, `version.dll`) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 6 | Hybrid Multi-Backend Dispatcher (Nukem9, FSR 3.1, XeFG) | ORIGINAL_REQUEST §R3 | 5 | 5 | ✓ |
| 7 | Shared Memory IPC (`Local\XtrTelemetrySharedMem`, `XtrTelemetryPacket` 112-byte) | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 8 | Seqlock Synchronization & Zero-Allocation Bidirectional Transfer | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 9 | Non-Invasive ETW Telemetry Engine (DXGI `{CA11C036-...}`, D3D12 `{5D8087DD-...}`) | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 10 | 1% Low & Frametime Variance Percentile Engine | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 11 | In-Game Branded Emerald HUD (`[ XTR ENGINE ] ...`, F10 toggle) | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 12 | Background ProcessWatcher (<500ms launch detection, High priority elevation) | ORIGINAL_REQUEST §R2 | 5 | 5 | ✓ |
| 13 | Unified Native Fluent Studio (wpfui, <45MB RAM, ~200ms cold startup, 0.0% CPU) | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 14 | Upscaler DLL Switcher & Version Management | ORIGINAL_REQUEST §R1 | 5 | 5 | ✓ |
| 15 | Release Packaging & Single-File Installer (`dist/XTR-Unlocker-Setup.exe`) | Acceptance Criteria | 5 | 5 | ✓ |

## Test Architecture
- Test runner: `dotnet test desktop/tests/XTR.Tests/XTR.Tests.csproj` and `dotnet test desktop/tests/XTR.E2ETests/XTR.E2ETests.csproj`
- Invocations:
  - Standard test suite: `dotnet test desktop/tests/XTR.Tests/XTR.Tests.csproj -c Release`
  - E2E test suite: `dotnet test desktop/tests/XTR.E2ETests/XTR.E2ETests.csproj -c Release`
- Pass/Fail Semantics: 100% of tests must pass (Exit code 0). Zero compiler warnings with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

## Real-World Application Scenarios (Tier 4)
| # | Scenario | Features Exercised | Complexity |
|---|----------|--------------------|------------|
| 1 | Forza Horizon 6 High-Speed Disocclusion (300 km/h chase cam) | F1, F2, F3, F4, F5, F7, F8, F11 | High |
| 2 | Cyberpunk 2077 Night City Ray Tracing + Frame Gen Injection | F2, F4, F6, F7, F9, F10, F11 | High |
| 3 | Competitive DX12 Title Zero-Cheat Companion Launch & Auto-Minimize | F7, F9, F10, F12, F13 | Medium |
| 4 | Universal Fallback on Non-RTX Discrete GPU (Intel Arc / AMD Radeon) | F1, F6, F7, F11, F14 | Medium |
| 5 | Continuous High-Frequency Telemetry Ingestion (144 Hz stress, Seqlock integrity) | F7, F8, F9, F10, F11 | High |

## Coverage Thresholds
- Tier 1: ≥5 test cases per feature (15 × 5 = 75 test cases)
- Tier 2: ≥5 test cases per feature boundary/corner condition (15 × 5 = 75 test cases)
- Tier 3: Pairwise coverage across major feature interactions (≥15 test cases)
- Tier 4: Realistic end-to-end application scenarios (≥5 test cases)
- Tier 5: Adversarial stress testing (Seqlock race conditions, rapid process lifecycle, telemetry tearing)
