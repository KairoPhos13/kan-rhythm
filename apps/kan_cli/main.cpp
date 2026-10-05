#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"

#include <iostream>
#include <iomanip>
#include <chrono>
#include <thread>
#include <atomic>
#include <csignal>

constexpr uint32_t SAMPLE_RATE = 48000;
constexpr uint32_t BLOCK_SIZE  = 128; // 2.66 ms of buffer latency
constexpr size_t   PITCH_WINDOW_SAMPLES = 2048; // ~42 ms sliding window

kan::LockFreeRingBuffer<float, 16384> g_audio_ring;
std::atomic<bool> g_running{true};

void signal_handler(int /*sig*/) {
    g_running.store(false);
}

// Real-time audio thread (zero blocking, zero allocations)
void audio_capture_callback(ma_device* /*pDevice*/, void* /*pOutput*/, const void* pInput, ma_uint32 frameCount) {
    const float* in = static_cast<const float*>(pInput);
    if (in != nullptr) {
        g_audio_ring.push(in, frameCount);
    }
}

int main() {
    std::cout << "====================================================\n";
    std::cout << "  kan! (libkan v0.1) - Hybrid DSP Engine Benchmark  \n";
    std::cout << "  Buffer: " << BLOCK_SIZE << " samples @ " << SAMPLE_RATE << " Hz (~2.66 ms)\n";
    std::cout << "  Grab your instrument. You kan! (Ctrl+C to exit)   \n";
    std::cout << "====================================================\n" << std::endl;

    std::signal(SIGINT, signal_handler);
    std::signal(SIGTERM, signal_handler);

    ma_device_config config = ma_device_config_init(ma_device_type_capture);
    config.capture.format   = ma_format_f32;
    config.capture.channels = 1;
    config.sampleRate       = SAMPLE_RATE;
    config.periodSizeInFrames = BLOCK_SIZE;
    config.performanceProfile = ma_performance_profile_low_latency;
    config.dataCallback     = audio_capture_callback;

    ma_device device;
    if (ma_device_init(nullptr, &config, &device) != MA_SUCCESS) {
        std::cerr << "[ERROR] Could not initialize the microphone/audio interface.\n";
        return -1;
    }

    ma_device_start(&device);

    kan::HybridDetector detector(SAMPLE_RATE);
    float block[BLOCK_SIZE];
    std::vector<float> rolling_window(PITCH_WINDOW_SAMPLES, 0.0f);

    bool waiting_pitch_confirmation = false;
    int blocks_since_onset = 0;
    auto onset_timestamp = std::chrono::high_resolution_clock::now();

    while (g_running.load()) {
        if (g_audio_ring.pop(block, BLOCK_SIZE)) {
            // Update the sliding window used for pitch analysis
            std::move(rolling_window.begin() + BLOCK_SIZE, rolling_window.end(), rolling_window.begin());
            std::copy(block, block + BLOCK_SIZE, rolling_window.end() - BLOCK_SIZE);

            // 1. FAST PATH: detect the physical attack instantly
            if (detector.process_onset_block(block, BLOCK_SIZE)) {
                onset_timestamp = std::chrono::high_resolution_clock::now();
                waiting_pitch_confirmation = true;
                blocks_since_onset = 0;
                std::cout << "⚡ [ONSET! 300/PERFECT] Hit detected (<3ms) -> Evaluating pitch...\r" << std::flush;
            }

            // 2. HARMONIC PATH: wait ~8 blocks (~21 ms of transient discard) and validate the note
            if (waiting_pitch_confirmation) {
                blocks_since_onset++;
                if (blocks_since_onset >= 8) { // ~21 ms post-onset
                    auto pitch = detector.detect_pitch(rolling_window);
                    if (pitch.valid) {
                        auto now = std::chrono::high_resolution_clock::now();
                        double ms = std::chrono::duration<double, std::milli>(now - onset_timestamp).count();

                        std::cout << "✅ [HIT CONFIRMED at +" << std::fixed << std::setprecision(1) << ms << " ms] "
                                  << "Note: " << std::setw(3) << pitch.note_name
                                  << " | Freq: " << std::setw(6) << pitch.frequency_hz << " Hz"
                                  << " | Offset: " << std::showpos << pitch.cents_offset << std::noshowpos << " cents\n";
                        waiting_pitch_confirmation = false;
                    } else if (blocks_since_onset > 20) {
                        // If after ~50 ms it was only a percussive hit without a clear note
                        std::cout << "🥁 [PERCUSSIVE / MUTE] Rhythmic attack without sustained tone  \n";
                        waiting_pitch_confirmation = false;
                    }
                }
            }
        } else {
            std::this_thread::sleep_for(std::chrono::microseconds(200));
        }
    }

    ma_device_uninit(&device);
    return 0;
}
