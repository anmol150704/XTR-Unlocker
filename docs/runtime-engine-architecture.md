# XTR runtime engine

XTR treats the game-side runtime as a versioned engine bundle, not as a visual overlay.
The desktop application owns deployment, compatibility decisions, configuration, and
rollback. The runtime owns interception, upscaling, frame-generation bridges, and its
in-game diagnostics surface.

## Bundle layers

Every runtime bundle must identify these layers independently:

- `engine`: the OptiScaler DLL and its configuration schema.
- `upscaler`: the DLSS, XeSS, and FSR provider files used by the selected profile.
- `frameGeneration`: the selected bridge or unlock adapter, such as native DLSS-G,
  `dlssg_sm86`, FSR-FG, XeFG, or a game-specific companion.
- `loader`: the proxy DLL and any Streamline or loader dependencies.
- `licenses`: the notices shipped with every third-party component.

The bundle manifest records a version and SHA-256 for every file. XTR must not combine
modules from different engine generations unless the compatibility record explicitly
allows that combination.

## Per-game profile

Profiles are stored outside the game directory and contain:

- game ID and executable name;
- runtime bundle ID and version;
- proxy filename;
- API (`DX11`, `DX12`, or `Vulkan`);
- detected native inputs (DLSS, FSR, XeSS, and frame generation);
- GPU capability result;
- selected upscaler and frame-generation input/output;
- support state: `supported`, `experimental`, or `unavailable`.

The profile writer generates the game-local `OptiScaler.ini` from this validated data.
XTR currently owns the API-specific upscaler key plus `FGInput` and `FGOutput`; it
updates those keys atomically and leaves comments, unknown options, and user tuning
untouched. The runtime menu may change live values, but the desktop profile remains the
source for the next launch. Every generated value must be traceable to a known
OptiScaler option.

## Compatibility gate

XTR should offer a frame-generation option only when all required evidence is present:

1. The game exposes a supported temporal upscaler input.
2. The selected API and runtime adapter support that game's path.
3. The selected adapter supports the detected GPU family.
4. Required companion DLLs and configuration files are present and hash-verified.
5. The game is not marked incompatible by its compatibility record.

If any gate fails, XTR keeps the option visible as unavailable or experimental with the
reason. It must never imply that replacing a DLSS DLL alone adds missing game logic.

## Current migration target

The repository now stages a signed OptiScaler v0.9.4 baseline bundle with an
`xtr-runtime.json` descriptor. It is intentionally a baseline engine: it does not claim
to provide DLSS-NR or RTX 20/30 native MFG by itself. The next bundle migration should
use a reviewed, version-pinned DLSS-Unlocked package when those layers are needed,
preserve all upstream licenses, and expose its declared components through the same
manifest path. It must be validated on one offline DX12 title before the compatibility
record is expanded to other games.

The upstream comparison supports this split: DLSS Swapper is useful for versioned DLL
management, while DLSS-Unlocked combines OptiScaler, DLSS-NR, MFG unlockers, and
companion bridges. XTR therefore treats both as runtime package sources, not as the
runtime engine itself.


## Preflight and deployment planning

Before modifying any game directory, XTR executes a structured preflight audit and builds a non-destructive dry-run plan:

1. **Preflight Service (RuntimePreflightService)**:
   - Detects primary executable and game root path.
   - Evaluates active rendering API (DX11, DX12, Vulkan) from game files.
   - Audits native DLSS, DLSS-G, and Streamline interposer integration.
   - Scans for proxy collisions (e.g. unmanaged dxgi.dll) and update drift.
   - Cryptographically verifies runtime pack files against SHA-256 manifests.
   - Screens for multiplayer anti-cheat binaries (EAC, BattlEye, Vanguard, etc.).

2. **Deployment Planner (DeploymentPlanner)**:
   - Compiles a complete transactional staging plan without modifying game files.
   - Calculates exact files to deploy, original files requiring backup, and OptiScaler.ini key adjustments.
   - Normal UI presents an Apple-style review with progressive disclosure before the user confirms installation.