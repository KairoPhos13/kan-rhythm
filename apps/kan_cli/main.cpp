#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/precondition.hpp"

#include <iostream>
#include <iomanip>
#include <chrono>
#include <thread>
#include <atomic>
#include <csignal>
#include <cstring>
#include <cstdlib>
#include <vector>

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

int main(int argc, char** argv) {
    // Options: --no-gate (disable the adaptive noise gate),
    //          --noise-cal <ms> (initial room-silence calibration window).
    bool use_gate = true;
    int noise_cal_ms = 500;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--no-gate") == 0) {
            use_gate = false;
        } else if (std::strcmp(argv[i], "--noise-cal") == 0 && i + 1 < argc) {
            noise_cal_ms = std::atoi(argv[++i]);
        } else {
            std::cerr << "[ERROR] Unknown option: " << argv[i]
                      << "\nUsage: kan_cli [--no-gate] [--noise-cal <ms>]\n";
            return -1;
        }
    }

    std::cout << "====================================================\n";
    std::cout << "  kan! (libkan v0.1) - Hybrid DSP Engine Benchmark  \n";
    std::cout << "  Buffer: " << BLOCK_SIZE << " samples @ " << SAMPLE_RATE << " Hz (~2.66 ms)\n";
    std::cout << "  Grab your instrument. You kan! (Ctrl+C to exit)   \n";
    std::cout << "====================================================\n" << std::endl;
    if (use_gate) {
        std::cout << "[stage 0] 35 Hz high-pass + adaptive noise gate; keep the room\n"
                  << "          quiet for the first " << noise_cal_ms << " ms to calibrate...\n";
    }

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
    kan::Preconditioner preconditioner(SAMPLE_RATE);
    std::vector<float> calib_silence;
    if (use_gate && noise_cal_ms > 0) {
        calib_silence.reserve(static_cast<size_t>(SAMPLE_RATE / 1000 * noise_cal_ms));
    }
    const size_t calib_target = calib_silence.capacity();
    bool gate_calibrated = !use_gate || calib_target == 0;

    float block[BLOCK_SIZE];
    float conditioned[BLOCK_SIZE]; // stage-0 output feeds detection + pitch window
    std::vector<float> rolling_window(PITCH_WINDOW_SAMPLES, 0.0f);

    bool waiting_pitch_confirmation = false;
    int blocks_since_onset = 0;
    auto onset_timestamp = std::chrono::high_resolution_clock::now();

    while (g_running.load()) {
        if (g_audio_ring.pop(block, BLOCK_SIZE)) {
            // Stage 0 preconditioning (consumer thread, RT-safe primitives):
            // 35 Hz high-pass + adaptive noise gate calibrated to room silence.
            if (use_gate) {
                if (!gate_calibrated) {
                    calib_silence.insert(calib_silence.end(), block, block + BLOCK_SIZE);
                    if (calib_silence.size() >= calib_target) {
                        preconditioner.calibrate(calib_silence.data(), calib_silence.size());
                        // Share the learned floor with the onset detector.
                        const float floor_rms = preconditioner.gate().noise_floor_rms();
                        detector.set_noise_floor(floor_rms * floor_rms);
                        gate_calibrated = true;
                        calib_silence.clear();
                        calib_silence.shrink_to_fit();
                        std::cout << "[stage 0] noise gate calibrated: floor RMS = "
                                  << std::fixed << std::setprecision(5) << floor_rms << "\n";
                    }
                }
                preconditioner.process_block(block, conditioned, BLOCK_SIZE);
            } else {
                std::memcpy(conditioned, block, BLOCK_SIZE * sizeof(float));
            }

            // Update the sliding window used for pitch analysis
            std::move(rolling_window.begin() + BLOCK_SIZE, rolling_window.end(), rolling_window.begin());
            std::copy(conditioned, conditioned + BLOCK_SIZE, rolling_window.end() - BLOCK_SIZE);

            // 1. FAST PATH: detect the physical attack instantly
            if (detector.process_onset_block(conditioned, BLOCK_SIZE)) {
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
