# ⚡ XTR Unlocker

> **Next-generation hardware frame generation and DLSS library studio for PC gaming.**  
> Crafted by [Anmol](https://github.com/anmol150704).

[![Release](https://img.shields.io/github/v/release/anmol150704/XTR-Unlocker?style=flat-square&color=007AFF)](https://github.com/anmol150704/XTR-Unlocker/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20%7C%2010%20x64-34C759?style=flat-square)](https://github.com/anmol150704/XTR-Unlocker)
[![License](https://img.shields.io/badge/license-GPL--3.0-5856D6?style=flat-square)](LICENSE)

---

**XTR Unlocker** unifies native hardware frame generation, in-engine neural preset overrides, and instant DLSS library hot-swapping into a clean, Apple-inspired desktop experience.

Whether you are running an NVIDIA RTX 40/50 series GPU or looking to bring smooth frame generation to older hardware, XTR Unlocker optimizes your games in a single click.

---

## Highlights

* **⚡ Native Hardware Frame Generation**: Direct Ada Lovelace and Blackwell (RTX 40/50) optical flow execution with zero proxy interpolation overhead.
* **🛡️ Anti-Ghosting Shield**: Eliminates motion trails and ghosting behind moving objects by enforcing DLSS **Preset E** and responsive pixel mask retention.
* **🏎️ 3 Tailored Engine Profiles**:
  * **High-Velocity**: Fast-paced titles (Forza, Rust, Apex) with rapid temporal clearing and low-latency frame pacing.
  * **Cinematic Story**: Immersive games (God of War, Alan Wake 2, Wukong) with 16x anisotropic filtering and negative LOD bias.
  * **Anti-Ghosting Shield**: Maximum remediation for games with severe trailing artifacts.
* **🔄 Zero-Restart DLSS Hot-Swapping**: Switch between DLSS 3.7+ (latest neural model), DLSS 3.5 (Ray Reconstruction), and DLSS 2.5 across your entire Steam, Epic, Xbox, and GOG library.
* **💎 Universal Frame Gen Bridge**: Translates Streamline calls into ultra-fast FSR 3.1 frame generation on RTX 20/30, AMD Radeon, and Intel Arc GPUs.
* **🖥️ Apple Liquid Interface**: Translucent acrylic glass styling, squircle cards, and native Segoe Fluent iconography with zero bloat.

---

## Hardware Compatibility

| GPU Family | Frame Generation Mode | Upscaler Swapping | Anti-Ghosting |
| :--- | :--- | :---: | :---: |
| **NVIDIA RTX 40 / 50 Series** | **Native Hardware DLSS-G** (Pure OFA) | DLSS 3.7+ | Full Shield |
| **NVIDIA RTX 20 / 30 Series** | **Universal Bridge** (FSR 3.1) | DLSS 3.7+ | Full Shield |
| **AMD Radeon RX 6000 / 7000+** | **Universal Bridge** (FSR 3.1) | XeSS / FSR | Full Shield |
| **Intel Arc A-Series / Battlemage** | **Universal Bridge** (XeFG / FSR 3.1) | XeSS / FSR | Full Shield |

---

## In-Game Hotkeys

When XTR Unlocker is deployed in any DirectX 11, DirectX 12, or Vulkan game:

| Hotkey | Action |
| :---: | :--- |
| `Page Up` | Toggle real-time FPS & Frame Generation telemetry HUD |
| `End` | Dynamically toggle Frame Generation on / off |
| `Insert` | Open underlying engine developer overlay *(suppressed by default)* |

---

## Quick Start

1. **Download** the latest release (`XTR-Unlocker.zip`) from [Releases](https://github.com/anmol150704/XTR-Unlocker/releases).
2. **Extract** and run `XTR Unlocker.exe`.
3. **Select your game** from the auto-detected library.
4. Click **⚡ Activate Turbo Boost** (or choose your preferred mode).
5. Launch and play your game normally.

---

## Building from Source

**Requirements**: Windows 10/11 x64, [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/anmol150704/XTR-Unlocker.git
cd XTR-Unlocker

# Run automated test suite (50 unit tests)
$env:DOTNET_ROOT = "C:\Path\To\.dotnet"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet test desktop/tests/XTR.Tests/XTR.Tests.csproj

# Compile Release x64 binary
dotnet build "desktop/src/DLSS Swapper.csproj" -c Release -p:Platform=x64
```

The compiled binaries will be placed in `desktop\src\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64`.

---

## Acknowledgments

XTR Unlocker is engineered by **[Anmol](https://github.com/anmol150704)**.

Special thanks to the open-source graphics research community:
* [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by Brad Moore (GPL-3.0) — DLL library discovery architecture.
* [OptiScaler](https://github.com/optiscaler/OptiScaler) (MIT) — upstream upscaler hooks and translation pipelines.
* [NVIDIA Streamline](https://github.com/NVIDIAGameWorks/Streamline) & [AMD FidelityFX](https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK).

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
