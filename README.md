# XTR Unlocker

XTR Unlocker is a unified Windows app for managing supported DLSS and upscaling options across an installed game library.

## Product structure

- **Desktop app (`desktop/`)**: game-library discovery, DLL management, per-game profiles, install/update/remove, backups, and restore.
- **Game runtime (`runtime/OptiScaler/`)**: the pinned OptiScaler source that will provide the per-game integration and redesigned in-game menu.

These are internal parts of one product. Users will manage their library and runtime from XTR Unlocker; the game-side component supplies controls while a selected game is running.

## Automatic game discovery

The desktop app detects installed games from Steam, GOG, Epic Games, Ubisoft Connect, Xbox App, and Battle.net. It also supports adding a game manually. The current XTR build has been launched locally and confirmed to discover the user's Steam library.

## Project status

The current `desktop/` source is based on [DLSS Swapper v1.2.7.0](https://github.com/beeradmoore/dlss-swapper/tree/v1.2.7.0) under GPL-3.0, with XTR identity, storage, update-feed routing, runtime profile storage, and install ownership manifests. The desktop Release build launches and scans games. Its per-game profile now installs an extracted OptiScaler package beside the selected game executable and provides a guarded restore action backed by hashed originals. OptiScaler v0.9.4 is pinned as a source submodule; an XTR patch adds branded styling, game-specific identification, Live and Settings tabs, and a session-aware frame generation toggle. Full in-game feature pages, compatibility records, and end-to-end validation with a real game remain in progress.

The initial product scope and implementation sequence are in [docs/unified-app-mvp.md](docs/unified-app-mvp.md). Attribution and license notes are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Build

Clone with source submodules enabled so the pinned runtime source and its dependencies are available:

```powershell
git clone --recurse-submodules https://github.com/anmol150704/XTR-Unlocker.git
```

Build the Release desktop app with the .NET 10 SDK:

```powershell
dotnet build "desktop\DLSS Swapper.sln" --configuration Release
```

The executable is produced under `desktop\src\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\XTR Unlocker.exe`.

To build the native runtime, install Visual Studio 2022 with the Desktop development with C++ workload and run:

```powershell
git -C runtime/OptiScaler apply ../../runtime/patches/xtr-overlay-branding.patch
msbuild "runtime\OptiScaler\OptiScaler.sln" /m /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
```

The extracted runtime package is created at `runtime\OptiScaler\x64\Release\a`. In a game's XTR profile, choose that folder (or an extracted OptiScaler release folder), then choose the folder containing the game's executable and the proxy DLL name. XTR records ownership and backups under its app data folder. Restore checks file hashes and stops if a tracked runtime file was changed unexpectedly.

## Compatibility

Changing an upscaler DLL or reporting a different GPU capability does not by itself add missing game integration or physical GPU features. Options will be shown according to verified game/runtime combinations, and the first end-to-end validation target will be one offline DirectX 12 game.
