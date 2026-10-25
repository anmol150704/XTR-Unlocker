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

`desktop/src/Runtime/OptiScaler/` is the bundled OptiScaler v0.9.4 runtime payload from [optiscaler/OptiScaler](https://github.com/optiscaler/OptiScaler).

- License: GNU General Public License v3.0; see the upstream project and the notices shipped beside the payload.
- The payload's optional provider licenses are included under `desktop/src/Runtime/OptiScaler/Licenses/`.
- The payload is installed per game through XTR's ownership and rollback system.
- Optional runtime modules remain subject to their own version, compatibility, and redistribution review.
