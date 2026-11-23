#pragma once

#include "xtr_telemetry.h"
#include <windows.h>
#include <atomic>
#include <immintrin.h>

/**
 * @brief Lock-free seqlock reader for XTR telemetry shared memory.
 * Never allocates on the render thread and caps spins at 3 to guarantee zero latency hit.
 */
class xtr_hud_ipc {
public:
    xtr_hud_ipc() = default;
    ~xtr_hud_ipc() {
        close();
    }

    // Non-copyable, non-movable
    xtr_hud_ipc(const xtr_hud_ipc&) = delete;
    xtr_hud_ipc& operator=(const xtr_hud_ipc&) = delete;

    bool open(const char* map_name = XTR_SHARED_MEMORY_NAME) {
        if (m_buffer != nullptr) {
            return true;
        }

        m_map_handle = OpenFileMappingA(FILE_MAP_READ, FALSE, map_name);
        if (m_map_handle == nullptr || m_map_handle == INVALID_HANDLE_VALUE) {
            return false;
        }

        m_buffer = MapViewOfFile(m_map_handle, FILE_MAP_READ, 0, 0, sizeof(xtr_telemetry_packet_t));
        if (m_buffer == nullptr) {
            CloseHandle(m_map_handle);
            m_map_handle = nullptr;
            return false;
        }

        return true;
    }

    void close() {
        if (m_buffer != nullptr) {
            UnmapViewOfFile(m_buffer);
            m_buffer = nullptr;
        }
        if (m_map_handle != nullptr && m_map_handle != INVALID_HANDLE_VALUE) {
            CloseHandle(m_map_handle);
            m_map_handle = nullptr;
        }
    }

    bool is_open() const {
        return m_buffer != nullptr;
    }

    /**
     * @brief Lock-free seqlock read with maximum 3 spins.
     * @param out_packet Output buffer for read telemetry.
     * @return true if consistent packet was read; false otherwise.
     */
    bool try_read(xtr_telemetry_packet_t& out_packet) {
        if (m_buffer == nullptr) {
            // Lazy reconnect if desktop app was launched after game
            if (!open()) {
                return false;
            }
        }

        const auto* packet_ptr = static_cast<const xtr_telemetry_packet_t*>(m_buffer);
        const auto* seq_ptr = reinterpret_cast<const uint32_t*>(reinterpret_cast<const char*>(packet_ptr) + 4);

        for (int spin = 0; spin < 3; ++spin) {
            // Read sequence number before payload with acquire semantics
#if defined(_MSC_VER)
            uint32_t seq1 = static_cast<uint32_t>(_InterlockedCompareExchange(reinterpret_cast<volatile long*>(const_cast<uint32_t*>(seq_ptr)), 0, 0));
#else
            uint32_t seq1 = __atomic_load_n(seq_ptr, __ATOMIC_ACQUIRE);
#endif

            // Odd sequence counter indicates write in progress
            if ((seq1 & 1) != 0) {
                _mm_pause();
                continue;
            }

            // Copy packet
            std::memcpy(&out_packet, packet_ptr, sizeof(xtr_telemetry_packet_t));

            // Read sequence number after payload with acquire semantics
#if defined(_MSC_VER)
            uint32_t seq2 = static_cast<uint32_t>(_InterlockedCompareExchange(reinterpret_cast<volatile long*>(const_cast<uint32_t*>(seq_ptr)), 0, 0));
#else
            uint32_t seq2 = __atomic_load_n(seq_ptr, __ATOMIC_ACQUIRE);
#endif

            // Check consistency and valid magic
            if (seq1 == seq2 && out_packet.magic == XTR_TELEMETRY_MAGIC) {
                return true;
            }

            _mm_pause();
        }

        return false;
    }

private:
    HANDLE m_map_handle = nullptr;
    void*  m_buffer     = nullptr;
};
