# XTR Unlocker

XTR Unlocker is a unified Windows app for managing supported DLSS and upscaling options across an installed game library.

## Product structure

- **Desktop app (`desktop/`)**: game-library discovery, DLL management, per-game profiles, install/update/remove, backups, and restore.
- **Game runtime (`runtime/OptiScaler/`)**: the pinned OptiScaler source that will provide the per-game integration and redesigned in-game menu.

These are internal parts of one product. Users will manage their library and runtime from XTR Unlocker; the game-side component supplies controls while a selected game is running.

## Automatic game discovery

The desktop app detects installed games from Steam, GOG, Epic Games, Ubisoft Connect, Xbox App, and Battle.net. It also supports adding a game manually. The current XTR build has been launched locally and confirmed to discover the user's Steam library.

## Project status

The current `desktop/` source is based on [DLSS Swapper v1.2.7.0](https://github.com/beeradmoore/dlss-swapper/tree/v1.2.7.0) under GPL-3.0, with XTR identity, storage, update-feed routing, runtime profile storage, and install ownership manifests. The desktop Release build launches and scans games. Its per-game profile installs an extracted OptiScaler package beside the selected game executable and provides a guarded restore action backed by hashed originals. OptiScaler v0.9.4 is pinned as a source submodule; the XTR patch adds branded styling and game identification plus Overview, Upscaling, Frame Generation, and Advanced tabs. Overview reports the live session, Upscaling offers the active backend switch, Frame Generation exposes its session-aware toggle, and Advanced retains all runtime and compatibility controls. Compatibility records and end-to-end validation with a real game remain in progress.

The initial product scope and implementation sequence are in [docs/unified-app-mvp.md](docs/unified-app-mvp.md). Attribution and license notes are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Build

Clone with source submodules enabled so the pinned runtime source and its dependencies are available:

```powershell
git clone --recurse-submodules https://github.com/anmol150704/XTR-Unlocker.git
```

Build the unified Release app with the .NET 10 SDK and Visual Studio 2022 with the Desktop development with C++ workload. Run this from a Visual Studio Developer PowerShell so `msbuild` is available:

```powershell
.\scripts\build-windows.ps1 -Configuration Release
```

The executable and bundled OptiScaler package are produced under `desktop\src\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64`. The runtime menu installs the bundled build by default; choose another extracted OptiScaler package from the install menu to use a custom build. Then choose the folder containing the game's executable and the proxy DLL name. XTR records ownership and backups under its app data folder. Restore checks file hashes and stops if a tracked runtime file was changed unexpectedly.

Each installed game gets an independent runtime manifest and profile in XTR's app-data directory. The manifest records the exact runtime files, proxy DLL, install root, runtime bundle identity, declared components, and original-file backups. The profile records the selected runtime identity separately from the game folder, so a game update cannot silently discard it. Runtime packages may include an `xtr-runtime.json` descriptor so XTR can explain their engine, provider, and frame-generation layers without guessing from filenames. OptiScaler's in-game menu remains the source of truth for live upscaler and frame-generation settings; XTR only reports a runtime as ready after its manifest has been recorded.

## Compatibility

Changing an upscaler DLL or reporting a different GPU capability does not by itself add missing game integration or physical GPU features. Options will be shown according to verified game/runtime combinations, and the first end-to-end validation target will be one offline DirectX 12 game.

The DLSS-Unlocked runtime path has the same boundary: upstream requires a native
DLSS2/DLSS3 DirectX 12 integration before its upscaler, neural-rendering, or MFG layers
can be used. XTR therefore treats “DLSS detected” as a prerequisite, not a guarantee,
and keeps frame generation marked experimental until the selected runtime reports a
usable input/output path.
