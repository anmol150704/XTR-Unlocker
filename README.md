# XTR Unlocker

XTR Unlocker is a unified Windows app for managing supported DLSS and upscaling options across an installed game library.

## Product structure

- **Desktop app (`desktop/`)**: game-library discovery, DLL management, per-game profiles, install/update/remove, backups, and restore.
- **Game runtime (`runtime/OptiScaler/`)**: the pinned OptiScaler source that will provide the per-game integration and redesigned in-game menu.

These are internal parts of one product. Users will manage their library and runtime from XTR Unlocker; the game-side component supplies controls while a selected game is running.

## Automatic game discovery

The desktop app detects installed games from Steam, GOG, Epic Games, Ubisoft Connect, Xbox App, and Battle.net. It also supports adding a game manually. The current XTR build has been launched locally and confirmed to discover the user's Steam library.

## Project status

The current `desktop/` source is based on [DLSS Swapper v1.2.7.0](https://github.com/beeradmoore/dlss-swapper/tree/v1.2.7.0) under GPL-3.0, with XTR identity, storage, update-feed routing, runtime profile storage, and install ownership manifests. The desktop Release build launches and scans games. OptiScaler v0.9.4 is pinned as a source submodule; an XTR patch adds branded styling and game-specific identification plus Live and Settings tabs, with a session-aware frame generation toggle. Runtime installation and restoration, more focused upscaling and frame generation pages, game compatibility records, and end-to-end behavior remain to be implemented and validated.

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
msbuild "runtime\OptiScaler\OptiScaler.sln" /m /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
```

## Compatibility

Changing an upscaler DLL or reporting a different GPU capability does not by itself add missing game integration or physical GPU features. Options will be shown according to verified game/runtime combinations, and the first end-to-end validation target will be one offline DirectX 12 game.
