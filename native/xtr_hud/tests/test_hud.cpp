#include "../xtr_telemetry.h"
#include "../hud_renderer.h"
#include <cassert>
#include <iostream>
#include <string>

int main() {
    std::cout << "[Native HUD Test] Verifying struct sizes and offsets..." << std::endl;

    assert(sizeof(xtr_telemetry_packet_t) == 112);
    assert(offsetof(xtr_telemetry_packet_t, magic) == 0);
    assert(offsetof(xtr_telemetry_packet_t, sequence_number) == 4);
    assert(offsetof(xtr_telemetry_packet_t, process_id) == 8);
    assert(offsetof(xtr_telemetry_packet_t, gpu_name) == 12);
    assert(offsetof(xtr_telemetry_packet_t, gpu_temperature_c) == 76);
    assert(offsetof(xtr_telemetry_packet_t, gpu_utilization) == 80);
    assert(offsetof(xtr_telemetry_packet_t, vram_used_mb) == 84);
    assert(offsetof(xtr_telemetry_packet_t, vram_total_mb) == 88);
    assert(offsetof(xtr_telemetry_packet_t, frame_gen_mode) == 92);
    assert(offsetof(xtr_telemetry_packet_t, multiplier) == 96);
    assert(offsetof(xtr_telemetry_packet_t, anti_ghosting_shield) == 100);
    assert(offsetof(xtr_telemetry_packet_t, in_game_fps) == 104);
    assert(offsetof(xtr_telemetry_packet_t, in_game_frametime_ms) == 108);

    std::cout << "[Native HUD Test] Verifying formatting functions..." << std::endl;

    std::string s1 = xtr_hud_renderer::format_hud_string(60.2f, 2, 16.64f, "RTX 2050");
    assert(s1 == "[ XTR ENGINE ] • FPS: 60 (FG 2X) • 16.6 ms • RTX 2050");

    std::string s2 = xtr_hud_renderer::format_hud_string(144.0f, 1, 6.94f, "RTX 4090");
    assert(s2 == "[ XTR ENGINE ] • FPS: 144 • 6.9 ms • RTX 4090");

    std::cout << "[Native HUD Test] Verifying VRAM status guardrails..." << std::endl;

    // Normal: 2000 / 4096 = 48.8%
    assert(xtr_hud_renderer::evaluate_vram_status(2000, 4096) == xtr_vram_status_e::Normal);

    // Warning (>85%): 3600 / 4096 = 87.8%
    assert(xtr_hud_renderer::evaluate_vram_status(3600, 4096) == xtr_vram_status_e::Warning);

    // Critical (>92%): 3900 / 4096 = 95.2%
    assert(xtr_hud_renderer::evaluate_vram_status(3900, 4096) == xtr_vram_status_e::Critical);

    std::cout << "[Native HUD Test] Verifying HUD mode transitions..." << std::endl;
    xtr_hud_renderer renderer;
    assert(renderer.get_mode() == xtr_hud_mode_e::Compact);
    renderer.cycle_mode();
    assert(renderer.get_mode() == xtr_hud_mode_e::Detailed);
    renderer.cycle_mode();
    assert(renderer.get_mode() == xtr_hud_mode_e::Minimal);
    renderer.cycle_mode();
    assert(renderer.get_mode() == xtr_hud_mode_e::Compact);

    std::cout << "[Native HUD Test] All assertions passed successfully!" << std::endl;
    return 0;
}
