---
name: anti-ai-slop-and-quality
description: Strict guidelines and linters against generic AI slop, synthetic buzzwords, placeholder mockups, and low-effort copy. Enforces grounded engineering reality, authentic design, and concrete technical documentation.
---

# Anti-AI Slop & Engineering Quality Standard

## The Problem
Generic AI agents tend to default to recognizable "AI slop":
- Repetitive, flowery marketing buzzwords ("Revolutionize your gaming experience", "Seamlessly elevate your framerates", "Cutting-edge state-of-the-art solution").
- Fake or half-baked feature cards with placeholder handlers that do nothing or log "Not implemented".
- Cliché purple/pink gradient designs littered with arbitrary rocket and fire emojis.
- Superficial documentation that repeats the prompt without providing architectural truth, concrete tables, or CLI commands.

## Mandatory Anti-Slop Rules

### 1. Zero Fake Controls
- Every button, slider, and toggle in the application MUST be bound to a real, tested backend property or service command.
- If a feature is not ready, do not put a fake toggle that does nothing. Either build it completely or omit it.

### 2. Concrete, Technical, Grounded Copy
- **Banned Words & Phrases**:
  - "Revolutionize", "Game-changer", "Seamlessly", "Elevate", "Cutting-edge", "State-of-the-art", "Next-gen marvel", "Harness the power of".
- **Approved Engineering Voice**:
  - State the technical truth plainly: "Instruments DXGI Present calls via ETW", "Clones motion vectors prior to async compute submission", "Applies single-pass O(N+M) INI updates", "Reduces working set RAM to 38 MB".
  - Always explain the *mechanism*, not just the promise.

### 3. Authentic, High-End Presentation
- Design looks like an authentic tool built by senior graphics programmers (like RivaTuner, OBS Studio, Lossless Scaling, CapFrameX, SpecialK).
- GitHub README must look like a premier open-source repository:
  - Clean SVG badges (Build passing, .NET 10, Windows 11, License).
  - Clear installation and quick-start guide with exact copyable commands.
  - Architectural flowcharts and benchmark comparison tables.
  - Verified hardware compatibility matrix (RTX 20/30/40, Radeon, Intel Arc).
  - Clean release download links.
