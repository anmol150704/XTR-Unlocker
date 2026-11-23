#include "hud_renderer.h"
#include <cstdio>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <windows.h>

xtr_hud_renderer::xtr_hud_renderer()
    : m_visible(true),
      m_mode(xtr_hud_mode_e::Compact),
      m_last_key_tick(0)
{
}

void xtr_hud_renderer::cycle_mode() {
    switch (m_mode) {
        case xtr_hud_mode_e::Compact:
            m_mode = xtr_hud_mode_e::Detailed;
            break;
        case xtr_hud_mode_e::Detailed:
            m_mode = xtr_hud_mode_e::Minimal;
            break;
        case xtr_hud_mode_e::Minimal:
        default:
            m_mode = xtr_hud_mode_e::Compact;
            break;
    }
}

xtr_vram_status_e xtr_hud_renderer::evaluate_vram_status(uint32_t used_mb, uint32_t total_mb) {
    if (total_mb == 0) return xtr_vram_status_e::Normal;

    double ratio = static_cast<double>(used_mb) / static_cast<double>(total_mb);
    if (ratio > 0.92) {
        return xtr_vram_status_e::Critical; // > 92% (Red alert for PCIe spill)
    }
    if (ratio > 0.85) {
        return xtr_vram_status_e::Warning;  // > 85% (Amber warning)
    }
    return xtr_vram_status_e::Normal;
}

xtr_color_rgba_t xtr_hud_renderer::get_vram_color(xtr_vram_status_e status) {
    switch (status) {
        case xtr_vram_status_e::Critical:
            return xtr_palette::VramRed;
        case xtr_vram_status_e::Warning:
            return xtr_palette::VramAmber;
        case xtr_vram_status_e::Normal:
        default:
            return xtr_palette::TextPrimary;
    }
}

std::string xtr_hud_renderer::format_hud_string(
    float fps,
    uint32_t multiplier,
    float frametime_ms,
    const char* gpu_name)
{
    const char* safe_gpu = (gpu_name != nullptr && gpu_name[0] != '\0') ? gpu_name : "Unknown GPU";
    int rounded_fps = std::max(0, static_cast<int>(std::round(fps)));
    float safe_ft = std::max(0.0f, frametime_ms);

    char buffer[256];
    if (multiplier > 1) {
        std::snprintf(buffer, sizeof(buffer), "[ XTR ENGINE ] • FPS: %d (FG %uX) • %.1f ms • %s",
                      rounded_fps, multiplier, safe_ft, safe_gpu);
    } else {
        std::snprintf(buffer, sizeof(buffer), "[ XTR ENGINE ] • FPS: %d • %.1f ms • %s",
                      rounded_fps, safe_ft, safe_gpu);
    }

    return std::string(buffer);
}

std::string xtr_hud_renderer::format_from_packet(const xtr_telemetry_packet_t& packet) {
    return format_hud_string(packet.in_game_fps, packet.multiplier, packet.in_game_frametime_ms, packet.gpu_name);
}

void xtr_hud_renderer::process_key_event(int vkey) {
    uint32_t now = GetTickCount();
    if (now - m_last_key_tick < 200) {
        return; // Debounce 200ms
    }

    if (vkey == xtr_hotkeys::FpsShortcutKey_F10) {
        toggle_visible();
        m_last_key_tick = now;
    } else if (vkey == xtr_hotkeys::FpsCycleShortcutKey_F9) {
        cycle_mode();
        m_last_key_tick = now;
    }
}
