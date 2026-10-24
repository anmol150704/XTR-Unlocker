# Third-party notices

## DLSS Swapper

The `desktop/` directory contains source imported from [beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper), tag `v1.2.7.0`.

- License: GNU General Public License v3.0. See [`LICENSE`](LICENSE) and [`desktop/LICENSE`](desktop/LICENSE).
- The upstream README, acknowledgement material, and project files are retained under `desktop/`.
- Upstream project: https://github.com/beeradmoore/dlss-swapper

## SkiaSharp

The desktop client uses SkiaSharp 3.119.2 for cover-image resizing and WebP decoding.

- License: MIT.
- Project: https://github.com/mono/SkiaSharp

## OptiScaler

`runtime/OptiScaler/` is the OptiScaler v0.9.4 source submodule, pinned at commit `7534ad00bf9e590eedb99e8dd9fd8c89dae3654f` from [optiscaler/OptiScaler](https://github.com/optiscaler/OptiScaler).

- License: GNU General Public License v3.0; see `runtime/OptiScaler/LICENSE`.
- Upstream submodules retain their own licenses and notices.
- `runtime/patches/xtr-overlay-branding.patch` is an XTR modification applied to the pinned source during the native build.
- The OptiScaler source is present for development. XTR does not yet package or install the runtime, and no upstream runtime release binaries are redistributed by this repository.
- Optional runtime modules remain subject to their own version, compatibility, and redistribution review.
