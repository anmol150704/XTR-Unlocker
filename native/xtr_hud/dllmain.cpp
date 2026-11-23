#include <windows.h>
#include <atomic>
#include <thread>
#include <chrono>
#include "xtr_telemetry.h"
#include "xtr_hud_ipc.h"
#include "hud_renderer.h"

namespace {
    std::atomic<bool> g_running{ false };
    std::thread       g_worker_thread;
    xtr_hud_ipc       g_ipc;
    xtr_hud_renderer  g_renderer;

    void WorkerLoop() {
        // Allow game engine / OptiScaler swapchain to finish initializing
        std::this_thread::sleep_for(std::chrono::milliseconds(1000));

        g_ipc.open();

        while (g_running.load(std::memory_order_relaxed)) {
            // Check hotkeys (F10 = 0x79 toggle HUD, F9 = 0x78 cycle detail mode)
            if (GetAsyncKeyState(xtr_hotkeys::FpsShortcutKey_F10) & 0x8000) {
                g_renderer.process_key_event(xtr_hotkeys::FpsShortcutKey_F10);
            }
            if (GetAsyncKeyState(xtr_hotkeys::FpsCycleShortcutKey_F9) & 0x8000) {
                g_renderer.process_key_event(xtr_hotkeys::FpsCycleShortcutKey_F9);
            }

            // Read live telemetry from XTR desktop engine shared memory
            xtr_telemetry_packet_t packet{};
            if (g_ipc.try_read(packet)) {
                if (g_renderer.is_visible()) {
                    // Update VRAM alert guardrail
                    auto vram_status = xtr_hud_renderer::evaluate_vram_status(packet.vram_used_mb, packet.vram_total_mb);
                    (void)vram_status;
                }
            }

            // 60 Hz polling loop for hotkeys & telemetry sync
            std::this_thread::sleep_for(std::chrono::milliseconds(16));
        }

        g_ipc.close();
    }
}

// ASI Plugin entry points
extern "C" {
    __declspec(dllexport) void InitializeASI() {
        if (!g_running.exchange(true)) {
            g_worker_thread = std::thread(WorkerLoop);
        }
    }

    __declspec(dllexport) void ShutdownASI() {
        if (g_running.exchange(false)) {
            if (g_worker_thread.joinable()) {
                g_worker_thread.join();
            }
        }
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
        case DLL_PROCESS_ATTACH:
            DisableThreadLibraryCalls(hModule);
            InitializeASI();
            break;

        case DLL_PROCESS_DETACH:
            if (lpReserved == nullptr) {
                ShutdownASI();
            }
            break;

        default:
            break;
    }
    return TRUE;
}
