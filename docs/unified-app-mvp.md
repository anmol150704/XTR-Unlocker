# Unified app MVP plan

## Product shape

One Windows app and one release, with two coordinated internal components:

- `desktop`: automatically discovers games, manages per-game profiles and upscaler DLLs, installs/updates/removes runtime files, and backs up/restores game files.
- `runtime`: the game-side OptiScaler integration and redesigned in-game menu.

Users operate one product. The desktop manager installs the game-side runtime into a selected game's directory; the runtime supplies the in-game controls while that game runs.

## MVP scope

1. Automatically discover installed games through the launcher adapters already present in the DLSS Swapper foundation.
2. Allow manual game-folder selection and rescanning.
3. Display each game's detected upscaler files and a clear support state: supported, experimental, or unavailable.
4. Install a selected per-game profile while recording exactly which files the app owns.
5. Back up pre-existing files before changes and provide a restore/uninstall action.
6. Provide in-game pages for Upscaling, Frame Generation, and Status. Show only controls supported by the selected game/runtime combination.

## Runtime boundary

OptiScaler remains the game-side engine. Start the custom menu by reusing its existing overlay renderer, input handling, and runtime feature controls, then replace the presentation and page layout. The desktop manager must not claim that a feature works merely because it can write a configuration value; support must be tied to a verified game/runtime combination.

## File safety and compatibility

- Never overwrite or remove a game file unless the app has recorded ownership and a recoverable original.
- Make install, update, and restore operations transactional and report partial failures.
- Keep profiles and runtime versions independent per game.
- Treat unlock/spoof modules as optional, version-pinned adapters with their own compatibility and license records.
- Do not bundle NVIDIA DLSS binaries unless redistribution terms allow it; support properly sourced or user-provided DLLs first.
- Preserve GPL-3.0 and third-party notices for reused code and dependencies.

## First validation target

Choose one offline DirectX 12 game that already exposes DLSS and can be tried on the RTX 2050 laptop. Verify game detection, backup, install, menu opening, available controls, launch, and clean restore before expanding compatibility claims. Success with one game does not establish support for all titles.

## Implementation sequence

1. Retain DLSS Swapper's launcher discovery and DLL management as the desktop foundation.
2. Define a versioned per-game profile and ownership/backup manifest.
3. Add OptiScaler as the separately built game-side component, preserving upstream notices.
4. Redesign its existing overlay around the MVP pages.
5. Connect desktop profile selection to the runtime's per-game configuration.
6. Validate the full flow in the first selected game.
