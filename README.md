# XTR Unlocker

XTR Unlocker is a unified Windows app for managing supported DLSS and upscaling options across an installed game library.

## Product structure

- **Desktop app (`desktop/`)**: game-library discovery, DLL management, per-game profiles, install/update/remove, backups, and restore.
- **Game runtime (planned)**: OptiScaler-based per-game integration and a redesigned in-game menu for options supported by that game.

These are internal parts of one product. Users will manage their library and runtime from XTR Unlocker; the game-side component supplies controls while a selected game is running.

## Automatic game discovery

The desktop app detects installed games from Steam, GOG, Epic Games, Ubisoft Connect, Xbox App, and Battle.net. It also supports adding a game manually. The current XTR build has been launched locally and confirmed to discover the user's Steam library.

## Project status

The current `desktop/` source is based on [DLSS Swapper v1.2.7.0](https://github.com/beeradmoore/dlss-swapper/tree/v1.2.7.0) under GPL-3.0, with XTR identity, storage, update-feed routing, runtime profile storage, and install ownership manifests. Debug and Release builds both compile cleanly, and the Release build launches and scans games. OptiScaler installation, the in-game menu, and game compatibility records remain to be implemented and validated; this is a functioning desktop foundation, not yet the complete unified product.

The initial product scope and implementation sequence are in [docs/unified-app-mvp.md](docs/unified-app-mvp.md). Attribution and license notes are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Build

On Windows, install the .NET 10 SDK and build the Release desktop app:

```powershell
dotnet build "desktop\DLSS Swapper.sln" --configuration Release
```

The executable is produced under `desktop\src\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\XTR Unlocker.exe`.

## Compatibility

Changing an upscaler DLL or reporting a different GPU capability does not by itself add missing game integration or physical GPU features. Options will be shown according to verified game/runtime combinations, and the first end-to-end validation target will be one offline DirectX 12 game.
