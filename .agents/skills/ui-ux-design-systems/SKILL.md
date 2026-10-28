---
name: ui-ux-design-systems
description: Master desktop and web UI/UX designer specializing in modern Windows 11 Fluent Design System, Mica/Acrylic backdrops, Apple glass aesthetics, dark themes, typography, and micro-interactions. Use when designing, restructuring, or auditing user interfaces.
---

# UI/UX Design Systems & Native Desktop Ergonomics

## Philosophy
Exceptional software design feels invisible, fast, and tactile. For gaming utilities, performance and clarity are king. A cluttered UI wastes memory and distracts the user; a sterile UI feels cheap. The sweet spot is a high-performance **Mica/Glass dark theme** with purposeful accent colors, clear typography, and responsive micro-interactions.

## Core Rules for Native Desktop Utilities

### 1. Visual Hierarchy & Scannability
- The most important action (e.g. **⚡ Activate Turbo Boost** or **Launch Game**) must be instantly visible within 100ms of looking at the screen.
- Primary actions use high-contrast vibrant accents (Emerald green `#10B981` or Cyberpunk Cyan `#06B6D4`).
- Secondary and tertiary actions use subtle borders and translucent surface backgrounds (`#1E293B` on `#0F172A`).
- Status badges use semantic coloring:
  - 🟢 Active / Running: Soft green glow with clear badge.
  - 🟡 Standby / Available: Amber or slate badge.
  - 🔴 Error / Conflict: Crimson warning pill.

### 2. Windows 11 Fluent 2 & Mica/Acrylic Aesthetics
- Backdrop: System Mica or Acrylic blur with fallback to rich deep obsidian dark (`#0B0F17`).
- Rounded corners: 8px to 12px for cards, 6px for buttons, 16px for modal flyouts.
- Card structure: Clean border (`1px solid rgba(255, 255, 255, 0.08)`), subtle elevation shadow (`0 4px 16px rgba(0, 0, 0, 0.4)`), and hover elevation.
- Typography: Segoe UI Variable or Inter. Standardized font scale:
  - Display Title: 24px - 28px SemiBold
  - Section Header: 16px - 18px SemiBold
  - Body Text: 13px - 14px Regular
  - Caption / Micro: 11px - 12px Medium (uppercase letter-spacing: 0.05em)

### 3. Desktop Gaming Ergonomics
- Never block the UI thread during disk I/O, network queries, or GPU driver diagnostics.
- Always provide immediate tactile feedback on click (pressed state animation, instant status change).
- Support quick keyboard shortcuts and smooth controller / tab navigation.
- Minimize to system tray option for true background companion operation.
