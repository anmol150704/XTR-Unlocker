#pragma once

#include "xtr_telemetry.h"
#include <string>
#include <cstdint>

enum class xtr_hud_mode_e : uint32_t {
    Compact = 0,
    Detailed = 1,
    Minimal = 2
};

enum class xtr_vram_status_e : uint32_t {
    Normal = 0,
    Warning = 1,  // > 85% VRAM usage (Amber)
    Critical = 2  // > 92% VRAM usage (Red)
};

struct xtr_color_rgba_t {
    float r = 1.0f;
    float g = 1.0f;
    float b = 1.0f;
    float a = 1.0f;
};

// Apple Emerald Palette constants matching XtrHudFormatterContract
namespace xtr_palette {
    constexpr xtr_color_rgba_t EmeraldAccent { 0.20f, 0.78f, 0.35f, 1.0f }; // #34C759
    constexpr xtr_color_rgba_t GlassBackground{ 0.08f, 0.09f, 0.11f, 0.92f }; // #14171C @ 92%
    constexpr xtr_color_rgba_t TextPrimary   { 0.95f, 0.96f, 0.98f, 1.0f }; // #F3F4F6
    constexpr xtr_color_rgba_t TextSecondary { 0.61f, 0.64f, 0.69f, 1.0f }; // #9CA3AF
    constexpr xtr_color_rgba_t VramAmber     { 0.96f, 0.62f, 0.04f, 1.0f }; // #F59E0B (>85%)
    constexpr xtr_color_rgba_t VramRed       { 0.94f, 0.27f, 0.27f, 1.0f }; // #EF4444 (>92%)
    constexpr float OverlayAlpha = 0.75f;
}

// Hotkey constants matching XtrHudFormatterContract
namespace xtr_hotkeys {
    constexpr int ShortcutKey_Insert = 0x2D;
    constexpr int FpsShortcutKey_F10 = 0x79;
    constexpr int FpsCycleShortcutKey_F9 = 0x78;
    constexpr int FGShortcutKey_F12 = 0x7B;
}

class xtr_hud_renderer {
public:
    xtr_hud_renderer();

    void toggle_visible() { m_visible = !m_visible; }
    void set_visible(bool visible) { m_visible = visible; }
    [[nodiscard]] bool is_visible() const { return m_visible; }

    void cycle_mode();
    [[nodiscard]] xtr_hud_mode_e get_mode() const { return m_mode; }
    void set_mode(xtr_hud_mode_e mode) { m_mode = mode; }

    // Evaluates VRAM usage against 4GB hardware guard thresholds
    [[nodiscard]] static xtr_vram_status_e evaluate_vram_status(uint32_t used_mb, uint32_t total_mb);
    [[nodiscard]] static xtr_color_rgba_t get_vram_color(xtr_vram_status_e status);

    // Formats single-line HUD string matching XtrHudFormatterContract
    [[nodiscard]] static std::string format_hud_string(
        float fps,
        uint32_t multiplier,
        float frametime_ms,
        const char* gpu_name);

    [[nodiscard]] static std::string format_from_packet(const xtr_telemetry_packet_t& packet);

    // Handles keyboard inputs (debounced)
    void process_key_event(int vkey);

private:
    bool           m_visible = true;
    xtr_hud_mode_e m_mode    = xtr_hud_mode_e::Compact;
    uint32_t       m_last_key_tick = 0;
};
