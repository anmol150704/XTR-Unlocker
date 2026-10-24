# Unified Upscaler Manager MVP

## Product shape

One Windows app and one release, with two internal parts:

- `desktop`: game library, game detection, upscaler DLL catalog, per-game profiles, install/update/remove, backups, and restore.
- `runtime`: the game-side OptiScaler integration and the redesigned in-game menu.

The runtime is installed into a game's directory by the desktop app. Users manage everything from the unified app; the runtime supplies the in-game controls while a game is running.

## MVP scope

1. Automatically detect installed games from the launchers already supported by DLSS Swapper.
2. Allow a user to add a game folder manually and rescan configured locations.
3. Show each game's detected upscaler DLLs and a clear support state: supported, experimental, or unavailable.
4. Install one selected runtime/profile per game, recording exactly which files the app owns.
5. Back up pre-existing files before changes and provide a restore/uninstall action.
6. Provide an in-game menu with Upscaling, Frame Generation, and Status pages. Only show controls supported by the selected game's integration.

## Runtime boundary

OptiScaler remains the game-side engine. The first custom menu iteration should reuse its existing overlay renderer, input handling, and runtime feature controls, while replacing the presentation and page layout. The desktop manager must not claim that a setting works merely because it can write a config value; support must be tied to a verified game/runtime combination.

## Safety and compatibility rules

- Never overwrite or remove a game file unless the app has recorded ownership and a recoverable original.
- Make install/update/restore operations transactional and report partial failures.
- Keep per-game profiles and runtime versions independent.
- Treat spoofing/unlock modules as optional, version-pinned adapters with their own compatibility and license records.
- Do not bundle NVIDIA DLSS binaries until their redistribution terms have been reviewed; support user-provided or properly sourced DLLs first.
- Preserve GPL-3 notices and third-party notices for reused DLSS Swapper and OptiScaler code.

## First validation target

Use one offline, DX12 game that already exposes DLSS and can be tested on the user's RTX 2050 laptop. Verify detection, backup, install, menu opening, available controls, game launch, and clean restore before expanding the compatibility list. Do not infer support for every title from one successful game.

## Initial implementation sequence

1. Keep DLSS Swapper's library discovery and DLL management as the desktop foundation.
2. Define a versioned per-game profile and an ownership/backup manifest.
3. Add the OptiScaler runtime as a separately built internal component and keep its upstream license/notices intact.
4. Redesign the existing OptiScaler overlay against the MVP pages.
5. Connect desktop profile selection to the runtime's per-game config, then validate the first game end to end.

