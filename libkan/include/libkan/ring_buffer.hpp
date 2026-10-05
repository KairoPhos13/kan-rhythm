#pragma once
#include <atomic>
#include <vector>
#include <cstddef>

namespace kan {

/// Lock-free single-producer single-consumer (SPSC) ring buffer.
/// Golden rule of real-time audio: the microphone callback must never wait
/// nor allocate. This atomic queue hands raw samples to the consumer thread
/// (detector/game) in microseconds.
/// Capacity must be a power of two.
template <typename T, size_t Capacity>
class LockFreeRingBuffer {
public:
    /// Producer side. Returns false when the buffer is full (samples dropped).
    bool push(const T* data, size_t count) noexcept {
        const size_t write_idx = write_pos_.load(std::memory_order_relaxed);
        const size_t read_idx  = read_pos_.load(std::memory_order_acquire);

        if (Capacity - (write_idx - read_idx) < count) {
            return false; // Buffer full
        }

        for (size_t i = 0; i < count; ++i) {
            buffer_[(write_idx + i) & mask_] = data[i];
        }

        write_pos_.store(write_idx + count, std::memory_order_release);
        return true;
    }

    /// Consumer side. Returns false when not enough samples are available yet.
    bool pop(T* dest, size_t count) noexcept {
        const size_t read_idx  = read_pos_.load(std::memory_order_relaxed);
        const size_t write_idx = write_pos_.load(std::memory_order_acquire);

        if (write_idx - read_idx < count) {
            return false; // Not enough samples yet
        }

        for (size_t i = 0; i < count; ++i) {
            dest[i] = buffer_[(read_idx + i) & mask_];
        }

        read_pos_.store(read_idx + count, std::memory_order_release);
        return true;
    }

private:
    static_assert((Capacity & (Capacity - 1)) == 0, "Capacity must be a power of 2");
    static constexpr size_t mask_ = Capacity - 1;
    T buffer_[Capacity]{};
    // Separate cache lines to avoid false sharing between producer/consumer.
    alignas(64) std::atomic<size_t> write_pos_{0};
    alignas(64) std::atomic<size_t> read_pos_{0};
};

} // namespace kan
