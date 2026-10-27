---
name: production-readiness
description: Standardized production build, packaging, verification, and GitHub release checklist for XTR Unlocker.
---

# Production Readiness & Release Runbook for XTR Unlocker

This skill guides engineers and autonomous agents through compiling, testing, packaging, and releasing production-grade versions of XTR Unlocker.

## 1. Prerequisites & Environment Check

Before building, verify the following prerequisites:
* **.NET 10 SDK**: Verify via `dotnet --version` (requires .NET 10.0+).
* **MSBuild / C++ Runtimes**: Required for WinUI 3 desktop dependencies.
* **7-Zip**: Required at `C:\Program Files\7-Zip\7z.exe` (with `7z.sfx` module) for generating self-extracting single-file installers.

## 2. Automated Test Verification Gate

Every production candidate must pass all automated unit tests before packaging:

```powershell
dotnet test desktop/tests/XTR.Tests/XTR.Tests.csproj --configuration Release --verbosity normal
```

**Gate Requirement**: 100% pass rate (50 / 50 passing). Zero tolerance for skipped or failing tests.

## 3. Compilation & Packaging Commands

### A. Standalone Release Build
Compiles a fully self-contained unpackaged WinUI 3 application with native OptiScaler runtime bundle:

```powershell
.\scripts\build-windows.ps1 -Configuration Release -SelfContained -PackageZip
```

* Output folder: `dist/XTR-Unlocker-Release-win-x64/`
* Zip archive: `dist/XTR-Unlocker-Release-win-x64.zip`

### B. Portable Release Build
Compiles a portable instance storing configurations locally:

```powershell
.\scripts\build-windows.ps1 -Configuration Release_Portable -SelfContained -PackageZip
```

* Output folder: `dist/XTR-Unlocker-Release_Portable-win-x64/`
* Zip archive: `dist/XTR-Unlocker-Release_Portable-win-x64.zip`

### C. Clean Single-File Setup Installer
Generates `dist/XTR-Unlocker-Setup.exe` combining the 7z SFX stub, configuration file, and compressed application payload:

```powershell
& "C:\Program Files\7-Zip\7z.exe" a -t7z "dist\app_payload.7z" ".\dist\XTR-Unlocker-Release-win-x64\*" -mx=9
cmd /c "copy /b `"C:\Program Files\7-Zip\7z.sfx`" + dist\sfx_config.txt + dist\app_payload.7z dist\XTR-Unlocker-Setup.exe"
```

## 4. In-Game Overlay & Runtime Manifest Verification

Ensure runtime assets are strictly synchronized:
1. `OptiScaler.ini`:
   * `ShortcutKey=0x2D` (`Insert` - Dev Menu)
   * `FpsShortcutKey=0x21` (`Page Up` - HUD Toggle)
   * `FpsCycleShortcutKey=0x22` (`Page Down` - HUD Cycle)
   * `FGShortcutKey=0x23` (`End` - Frame Gen Toggle)
   * `FpsOverlayHorizontal=true`
2. `xtr-runtime.json`:
   * Matches exact SHA-256 and byte size of `OptiScaler.ini` and `OptiScaler.dll`.

## 5. GitHub Release Automation

Releases are published via `.github/workflows/release.yml` triggered on git tags:

```powershell
git tag -a v1.0.1 -m "Release v1.0.1: Production Ready Build"
git push origin v1.0.1
```
The workflow triggers on GitHub Actions, builds all self-contained packages and installer, and attaches them to the GitHub Release.
