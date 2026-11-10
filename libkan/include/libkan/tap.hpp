#pragma once
// Non-consuming detection tap for the kanfx input bus
// (platform-architecture.md §3 "Judgment taps"): the detection pipeline
// attaches to the input bus via non-consuming taps; monitoring FX must
// never alter what the judges analyze (transient blanking happens pre-tap,
// inside the detection chain, not in the bus).
//
// Contract for every consumer of this buffer:
//   - input is const and side-effect free (detectors take `const float*`
//     and never write through it),
//   - readers never consume: each Reader has its own cursor and observing
//     the stream cannot affect any other consumer,
//   - producer never blocks: publish() is audio-thread safe (fixed storage,
//     single atomic index, no allocation, no locks, no I/O).
//
// Overwrite semantics: the buffer holds the newest NumBlocks blocks; a
// reader that lags more than NumBlocks blocks loses the gap (reported via
// Reader::dropped()). This is the correct trade for detection taps:
// freshness beats completeness, and the game thread never stalls the bus.
#include <atomic>
#include <cstddef>
#include <cstdint>

namespace kan {

/// One immutable view of a published block. `samples` points into the tap's
/// fixed storage and stays valid until the writer wraps around (NumBlocks
/// blocks later). Analyze it in the same consumer pass, do not store it.
struct TapFrame {
    const float* samples = nullptr;
    size_t count = 0;
    uint64_t sequence = 0; ///< strictly increasing publication index
};

/// Single-writer, multi-reader tap over fixed block storage.
/// `T` is the sample type (float), `BlockSize` the block length,
/// `NumBlocks` the ring depth (power of two not required).
template <typename T, size_t BlockSize, size_t NumBlocks>
class TapBuffer {
    static_assert(BlockSize > 0, "BlockSize must be positive");
    static_assert(NumBlocks >= 2, "NumBlocks must hold at least 2 blocks");

public:
    static constexpr size_t block_size() noexcept { return BlockSize; }
    static constexpr size_t num_blocks() noexcept { return NumBlocks; }

    /// Producer side (audio thread or bus tap): copy one block into the
    /// ring. RT-safe, no allocation, no locks, no I/O. Never fails; the
    /// oldest block is overwritten if nobody consumed it.
    void publish(const T* block) noexcept {
        const uint64_t w = write_.load(std::memory_order_relaxed);
        T* slot = slots_[w % NumBlocks];
        for (size_t i = 0; i < BlockSize; ++i) slot[i] = block[i];
        // Release: readers that observe write_ >= w+1 see the copied block.
        write_.store(w + 1, std::memory_order_release);
    }

    /// Latest published sequence + 1 (0 = nothing published yet).
    uint64_t write_sequence() const noexcept {
        return write_.load(std::memory_order_acquire);
    }

    /// Reader side: an independent, non-consuming cursor into the tap.
    /// Copying a Reader copies its cursor; every consumer (detector, meter,
    /// recorder) makes its own.
    class Reader {
    public:
        explicit Reader(const TapBuffer& buf) noexcept
            : buf_(buf), next_seq_(buf.write_sequence()) {}

        /// Number of blocks not yet observed by this cursor.
        uint64_t available() const noexcept {
            const uint64_t w = buf_.write_sequence();
            return (w > next_seq_) ? (w - next_seq_) : 0;
        }

        /// Blocks lost to overwrites since the last successful next()
        /// (this reader lagged the ring depth).
        uint64_t dropped() const noexcept { return dropped_; }

        /// Advance the cursor by one block. Returns false when caught up.
        /// Allocation-free; the frame references the tap's storage.
        bool next(TapFrame& frame) noexcept {
            const uint64_t w = buf_.write_sequence();
            if (next_seq_ >= w) return false;
            if (w - next_seq_ > NumBlocks) { // gap lost to overwrites
                const uint64_t first_kept = w - NumBlocks;
                dropped_ += first_kept - next_seq_;
                next_seq_ = first_kept;
            }
            const T* slot = buf_.slots_[next_seq_ % NumBlocks];
            frame.samples = slot;
            frame.count = BlockSize;
            frame.sequence = next_seq_;
            ++next_seq_;
            return true;
        }

        /// Rewind to the newest block (skip everything queued).
        void skip_to_latest() noexcept {
            next_seq_ = buf_.write_sequence();
        }

    private:
        const TapBuffer& buf_;
        uint64_t next_seq_;
        uint64_t dropped_ = 0;
    };

private:
    friend class Reader;

    T slots_[NumBlocks][BlockSize] = {};
    alignas(64) std::atomic<uint64_t> write_{0};
};

} // namespace kan
