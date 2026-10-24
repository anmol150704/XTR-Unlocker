# XTR Unlocker

XTR Unlocker is a unified Windows app for managing supported DLSS and upscaling options across an installed game library.

## Product structure

- **Desktop app (`desktop/`)**: game-library discovery, DLL management, per-game profiles, install/update/remove, backups, and restore.
- **Game runtime (planned)**: OptiScaler-based per-game integration and a redesigned in-game menu for options supported by that game.

These are internal parts of one product. Users will manage their library and runtime from XTR Unlocker; the game-side component supplies controls while a selected game is running.

## Automatic game discovery

The imported desktop foundation already detects games from Steam, GOG, Epic Games, Ubisoft Connect, Xbox App, and Battle.net. It also supports adding a game manually. The first implementation work will adapt this library and scanner to XTR Unlocker's per-game profiles and runtime management.

## Project status

The current `desktop/` source is imported from [DLSS Swapper v1.2.7.0](https://github.com/beeradmoore/dlss-swapper/tree/v1.2.7.0) under GPL-3.0. It is an upstream foundation, not yet an XTR Unlocker release: the app branding, OptiScaler runtime integration, custom in-game menu, and game compatibility records are still to be implemented and validated.

The initial product scope and implementation sequence are in [docs/unified-app-mvp.md](docs/unified-app-mvp.md). Attribution and license notes are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Compatibility

Changing an upscaler DLL or reporting a different GPU capability does not by itself add missing game integration or physical GPU features. Options will be shown according to verified game/runtime combinations, and the first end-to-end validation target will be one offline DirectX 12 game.
