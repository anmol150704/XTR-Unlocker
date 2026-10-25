# XTR Unlocker

Hardware frame generation orchestration and DLSS library studio for Windows.

[![Release](https://img.shields.io/github/v/release/anmol150704/XTR-Unlocker?style=flat-square&color=007AFF)](https://github.com/anmol150704/XTR-Unlocker/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20%7C%2010%20x64-34C759?style=flat-square)](https://github.com/anmol150704/XTR-Unlocker)
[![License](https://img.shields.io/badge/license-GPL--3.0-5856D6?style=flat-square)](LICENSE)

---

XTR Unlocker unifies hardware frame generation, neural model preset tuning, and real-time upscaler hot-swapping into a focused, high-performance desktop utility. Built with a fluid, dark-mode glass interface, it gives gamers granular control over runtime rendering pipelines.

## Capabilities

- **Native Hardware Frame Generation**: Direct optical flow execution on supported hardware with zero proxy overhead.
- **Universal Frame Generation Bridge**: Real-time translation layer enabling frame generation across supported DirectX 11, DirectX 12, and Vulkan titles.
- **Anti-Ghosting Shield**: Eliminates motion trails and edge disocclusion artifacts through tuned reactive masks and DLSS Preset E model selection.
- **Engine Tuning Profiles**: Instant optimization tailored for high-framerate competitive play, visual fidelity, and artifact remediation.
- **Runtime Library Management**: Hot-swap upscaler libraries across Steam, Epic Games, Xbox, and GOG installations without restarting launchers.
- **In-Game Telemetry HUD**: Lightweight on-screen display tracking real-time framerates, pacing, and frame generation status.

## Architecture & Compatibility

| GPU Architecture | Frame Generation Mode | Upscaler Pipeline | Anti-Ghosting |
| :--- | :--- | :---: | :---: |
| NVIDIA GeForce RTX 40 & 50 Series | Native DLSS-G (Optical Flow) | DLSS 3.7+ | Active |
| NVIDIA GeForce RTX 20 & 30 Series | Universal Bridge (FSR 3.1) | DLSS 3.7+ | Active |
| AMD Radeon RX 6000 & 7000 Series | Universal Bridge (FSR 3.1) | XeSS / FSR | Active |
| Intel Arc A-Series & Battlemage | Universal Bridge (XeFG / FSR 3.1) | XeSS / FSR | Active |

## Controls & Hotkeys

When deployed to a game directory, the in-engine overlay is accessible via global hotkeys:

| Key | Function | Default |
| :---: | :--- | :--- |
| `Page Up` | Toggle real-time FPS and Frame Generation telemetry HUD | Enabled |
| `Page Down` | Cycle telemetry detail and graph modes | Enabled |
| `End` | Toggle frame generation interpolation dynamically | Enabled |
| `Insert` | Developer diagnostics panel | Enabled |

## Installation

1. Download the latest build from [Releases](https://github.com/anmol150704/XTR-Unlocker/releases).
2. Extract the archive and launch `XTR Unlocker.exe`.
3. Select an installed game from your detected library.
4. Select your preferred integration mode and apply.
5. Launch the game normally.

## Building from Source

### Prerequisites
- Windows 10 (1809+) or Windows 11 x64
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build Commands
```powershell
git clone https://github.com/anmol150704/XTR-Unlocker.git
cd XTR-Unlocker

# Execute test suite
dotnet test desktop/tests/XTR.Tests/XTR.Tests.csproj

# Compile Release x64 binary
dotnet build "desktop/src/DLSS Swapper.csproj" -c Release -p:Platform=x64
```

Build outputs are generated in `desktop/src/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/`.

## Acknowledgments

Engineered by [Anmol](https://github.com/anmol150704).

Derived from and appreciative of open-source research:
- [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by Brad Moore (GPL-3.0)
- [OptiScaler](https://github.com/optiscaler/OptiScaler) (MIT)
- NVIDIA Streamline & AMD FidelityFX SDK

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
