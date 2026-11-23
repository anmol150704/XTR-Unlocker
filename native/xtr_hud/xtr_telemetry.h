#pragma once

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <cmath>

#pragma pack(push, 1)

/**
 * @brief Zero-allocation 112-byte telemetry packet shared between XTR Unlocker desktop app
 * and the in-game C++20 Emerald HUD companion (xtr_hud.asi).
 * Binary-compatible with XtrTelemetryPacket in C# (Pack = 1).
 */
struct xtr_telemetry_packet_t {
    uint32_t magic;               // Offset 0 (4 bytes): 0x58545231 ("XTR1" in LE)
    uint32_t sequence_number;     // Offset 4 (4 bytes): Monotonic seqlock counter
    uint32_t process_id;          // Offset 8 (4 bytes): Target game PID
    char     gpu_name[64];        // Offset 12 (64 bytes): Null-terminated UTF-8 GPU name
    float    gpu_temperature_c;   // Offset 76 (4 bytes): IEEE 754 float
    float    gpu_utilization;     // Offset 80 (4 bytes): IEEE 754 float (0.0f - 100.0f)
    uint32_t vram_used_mb;        // Offset 84 (4 bytes): Megabytes allocated
    uint32_t vram_total_mb;       // Offset 88 (4 bytes): Megabytes total physical
    uint32_t frame_gen_mode;      // Offset 92 (4 bytes): 0=Off, 1=NativeDLSS, 2=FSR31_Bridge, 3=XeFG
    uint32_t multiplier;          // Offset 96 (4 bytes): 2x, 3x, 4x
    uint32_t anti_ghosting_shield;// Offset 100 (4 bytes): 1=Active, 0=Inactive
    float    in_game_fps;         // Offset 104 (4 bytes): Telemetry hook output
    float    in_game_frametime_ms;// Offset 108 (4 bytes): Telemetry hook output
};

#pragma pack(pop)

// Compile-time static assertions verifying exact binary layout
static_assert(sizeof(xtr_telemetry_packet_t) == 112, "xtr_telemetry_packet_t size must be exactly 112 bytes");
static_assert(offsetof(xtr_telemetry_packet_t, magic) == 0, "magic offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, sequence_number) == 4, "sequence_number offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, process_id) == 8, "process_id offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, gpu_name) == 12, "gpu_name offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, gpu_temperature_c) == 76, "gpu_temperature_c offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, gpu_utilization) == 80, "gpu_utilization offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, vram_used_mb) == 84, "vram_used_mb offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, vram_total_mb) == 88, "vram_total_mb offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, frame_gen_mode) == 92, "frame_gen_mode offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, multiplier) == 96, "multiplier offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, anti_ghosting_shield) == 100, "anti_ghosting_shield offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, in_game_fps) == 104, "in_game_fps offset mismatch");
static_assert(offsetof(xtr_telemetry_packet_t, in_game_frametime_ms) == 108, "in_game_frametime_ms offset mismatch");

constexpr uint32_t XTR_TELEMETRY_MAGIC = 0x58545231; // "XTR1"
constexpr const char* XTR_SHARED_MEMORY_NAME = "Local\\XtrTelemetrySharedMem";

// Frame generation modes
enum class xtr_frame_gen_mode_e : uint32_t {
    Off = 0,
    NativeDLSS = 1,
    FSR31_Bridge = 2,
    XeFG = 3
};
