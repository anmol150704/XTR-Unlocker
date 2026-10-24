# XTR Unlocker

A unified Windows app for managing supported DLSS and upscaling options across an installed game library.

## Product direction

XTR Unlocker is designed as one app with two coordinated parts:

- **Desktop manager:** automatically discovers games from supported launchers, lets users add or rescan folders, manages per-game DLL/runtime profiles, and provides backup, install, update, and restore controls.
- **In-game menu:** a custom interface for the options exposed by the selected game's runtime integration. The initial runtime direction is OptiScaler, with its existing overlay/rendering path as the foundation for a redesigned menu.

The desktop manager owns game discovery and file operations. The game-side runtime owns rendering and in-game controls. They ship and are versioned together as one product.

## Planned first milestone

- Scan installed games from supported launchers and allow manual folder selection.
- Present each detected game's integration/support status clearly.
- Install one selected profile per game, preserving existing files and recording ownership.
- Provide a redesigned in-game menu for supported upscaling, frame-generation, and status controls.
- Restore the original game files cleanly.

Feature availability will be based on verified game/runtime combinations. A DLL swap or a hardware capability spoof does not by itself add missing game integration or GPU hardware features.

## Project status

Early planning and repository setup. No app binaries or compatibility claims are published yet. The first implementation milestone will be tested on a single offline DirectX 12 title before expanding game coverage.

## Code and licenses

The planned desktop foundation is DLSS Swapper, and the planned game-side foundation is OptiScaler. Both are GPL-3.0 projects. Any reused code must retain its license and notices; bundled third-party runtimes must also keep their own licenses. NVIDIA DLSS binaries will not be redistributed unless their terms explicitly allow it.

See [docs/unified-app-mvp.md](docs/unified-app-mvp.md) for the initial architecture and implementation plan.
