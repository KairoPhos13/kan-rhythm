#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "libkan/ring_buffer.hpp"
#include "libkan/dsp_detector.hpp"
#include "libkan/precondition.hpp"
#ifdef KAN_CLI_HAVE_BACF
#include "libkan/bacf_detector.hpp"
#endif

#include <algorithm>
#include <atomic>
#include <chrono>
#include <csignal>
#include <cstdlib>
#include <cstring>
#include <iomanip>
#include <iostream>
#include <thread>
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
    //          --noise-cal <ms> (initial room-silence calibration window),
    //          --profile <plucked|wind|keyboard> (instrument profile),
    //          --detector <yin|bacf> (harmonic-path pitch detector),
    //          --loopback <wav> (inject a WAV file instead of the mic),
    //          --latency-report (print onset timing summary at exit).
    bool use_gate = true;
    int noise_cal_ms = 500;
    const char* profile_name = nullptr;
    const char* detector_name = "yin";
    const char* loopback_path = nullptr;
    bool latency_report = false;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--no-gate") == 0) {
            use_gate = false;
        } else if (std::strcmp(argv[i], "--noise-cal") == 0 && i + 1 < argc) {
            noise_cal_ms = std::atoi(argv[++i]);
        } else if (std::strcmp(argv[i], "--profile") == 0 && i + 1 < argc) {
            profile_name = argv[++i];
        } else if (std::strcmp(argv[i], "--detector") == 0 && i + 1 < argc) {
            detector_name = argv[++i];
        } else if (std::strcmp(argv[i], "--loopback") == 0 && i + 1 < argc) {
            loopback_path = argv[++i];
        } else if (std::strcmp(argv[i], "--latency-report") == 0) {
            latency_report = true;
        } else {
            std::cerr << "[ERROR] Unknown option: " << argv[i]
                      << "\nUsage: kan_cli [--no-gate] [--noise-cal <ms>]"
                         " [--profile <plucked|wind|keyboard>]"
                         " [--detector <yin|bacf>]"
                         " [--loopback <file.wav>] [--latency-report]\n";
            return -1;
        }
    }
    const bool use_bacf = std::strcmp(detector_name, "bacf") == 0;
    if (use_bacf) {
#ifndef KAN_CLI_HAVE_BACF
        std::cerr << "[ERROR] This build has no BACF support (KAN_ENABLE_BACF=OFF).\n";
        return -1;
#else
        std::cout << "[stage 3] harmonic path detector: Cycfi Q BACF (blind full-range)\n";
#endif
    } else if (std::strcmp(detector_name, "yin") != 0) {
        std::cerr << "[ERROR] Unknown detector '" << detector_name << "'. Valid: yin, bacf.\n";
        return -1;
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

    // --- loopback file source (no audio device needed) --------------------
    std::vector<float> file_samples;
    size_t file_pos = 0;
    if (loopback_path != nullptr) {
        ma_decoder_config dec = ma_decoder_config_init(ma_format_f32, 1, SAMPLE_RATE);
        ma_decoder decoder;
        if (ma_decoder_init_file(loopback_path, &dec, &decoder) != MA_SUCCESS) {
            std::cerr << "[ERROR] Could not open WAV file: " << loopback_path << "\n";
            return -1;
        }
        float chunk[4096];
        for (;;) {
            ma_uint64 frames_read = 0;
            if (ma_decoder_read_pcm_frames(&decoder, chunk, 4096, &frames_read) != MA_SUCCESS
                || frames_read == 0) {
                break;
            }
            file_samples.insert(file_samples.end(), chunk, chunk + frames_read);
        }
        ma_decoder_uninit(&decoder);
        std::cout << "[loopback] loaded " << file_samples.size() << " samples ("
                  << std::fixed << std::setprecision(2)
                  << double(file_samples.size()) / SAMPLE_RATE << " s) from "
                  << loopback_path << "\n";
    }

    ma_device device{};
    const bool use_device = (loopback_path == nullptr);
    if (use_device) {
        ma_device_config config = ma_device_config_init(ma_device_type_capture);
        config.capture.format   = ma_format_f32;
        config.capture.channels = 1;
        config.sampleRate       = SAMPLE_RATE;
        config.periodSizeInFrames = BLOCK_SIZE;
        config.performanceProfile = ma_performance_profile_low_latency;
        config.dataCallback     = audio_capture_callback;

        if (ma_device_init(nullptr, &config, &device) != MA_SUCCESS) {
            std::cerr << "[ERROR] Could not initialize the microphone/audio interface.\n";
            return -1;
        }
        ma_device_start(&device);
    }

    kan::HybridDetector detector(SAMPLE_RATE);
    kan::InstrumentProfile profile = kan::InstrumentProfile::plucked();
    if (profile_name != nullptr) {
        if (!kan::InstrumentProfile::from_name(profile_name, profile)) {
            std::cerr << "[ERROR] Unknown profile '" << profile_name
                      << "'. Valid: plucked, wind (bowed), keyboard (piano).\n";
            ma_device_uninit(&device);
            return -1;
        }
        std::cout << "[stage 2] instrument profile '" << profile_name
                  << "': transient blanking " << profile.blanking_ms
                  << " ms, pitch range " << profile.min_freq_hz << "-"
                  << profile.max_freq_hz << " Hz\n";
    }
    detector.set_profile(profile);
    constexpr float BLOCK_MS = 1000.0f * BLOCK_SIZE / SAMPLE_RATE;

    kan::Preconditioner preconditioner(SAMPLE_RATE);
#ifdef KAN_CLI_HAVE_BACF
    kan::BacfDetector bacf(SAMPLE_RATE); // blind full-range mode (no chart in the CLI)
#endif
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
    std::vector<float> onset_times_ms; // --latency-report source timestamps
    const auto session_start = std::chrono::steady_clock::now();

    while (g_running.load()) {
        // Block source: live ring buffer or the injected WAV (loopback).
        bool have_block = false;
        if (loopback_path != nullptr) {
            if (file_pos + BLOCK_SIZE <= file_samples.size()) {
                std::memcpy(block, file_samples.data() + file_pos, BLOCK_SIZE * sizeof(float));
                file_pos += BLOCK_SIZE;
                have_block = true;
            }
        } else {
            have_block = g_audio_ring.pop(block, BLOCK_SIZE);
        }

        if (have_block) {
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
                if (latency_report) {
                    // Source time of the block end (block-quantized detection):
                    // file position for loopback, session wall time for live.
                    if (loopback_path != nullptr) {
                        onset_times_ms.push_back(float(1000.0 * double(file_pos) / SAMPLE_RATE));
                    } else {
                        onset_times_ms.push_back(float(std::chrono::duration<double, std::milli>(
                            std::chrono::steady_clock::now() - session_start).count()));
                    }
                }
#ifdef KAN_CLI_HAVE_BACF
                if (use_bacf) bacf.reset(); // streak belongs to the new note
#endif
                std::cout << "⚡ [ONSET! 300/PERFECT] Hit detected (<3ms) -> Evaluating pitch...\r" << std::flush;
            }

#ifdef KAN_CLI_HAVE_BACF
            // Keep the BACF bitstream detector continuously fed so its
            // internal state is warm when the blanking window ends.
            kan::PitchResult bacf_pitch;
            if (use_bacf) bacf_pitch = bacf.process_block(conditioned, BLOCK_SIZE);
#endif

            // 2. HARMONIC PATH: stage-2 transient blanking per instrument
            //    profile, then validate the note on the steady-state window.
            if (waiting_pitch_confirmation) {
                blocks_since_onset++;
                const float ms_since_onset = blocks_since_onset * BLOCK_MS;
                if (!detector.in_transient_blanking(ms_since_onset)) {
#ifdef KAN_CLI_HAVE_BACF
                    const kan::PitchResult pitch =
                        use_bacf ? bacf_pitch : detector.detect_pitch(rolling_window);
#else
                    const kan::PitchResult pitch = detector.detect_pitch(rolling_window);
#endif
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
            if (loopback_path != nullptr) break; // file fully consumed
            std::this_thread::sleep_for(std::chrono::microseconds(200));
        }
    }

    if (latency_report) {
        const float block_ms = float(1000.0 * BLOCK_SIZE / SAMPLE_RATE);
        std::cout << "\n--- latency report ---\n";
        std::cout << "onsets: " << onset_times_ms.size()
                  << ", detection is block-quantized ("
                  << std::fixed << std::setprecision(2) << block_ms
                  << " ms per " << BLOCK_SIZE << "-sample block)\n";
        for (size_t i = 0; i < onset_times_ms.size() && i < 64; ++i) {
            std::cout << "  onset " << i << ": source t = " << std::setprecision(1)
                      << onset_times_ms[i] << " ms\n";
        }
        std::cout << "The hardware->event gate (<= 4.5 ms) needs the loopback-cable\n"
                  << "procedure (docs/dsp/latency-measurements.md). This report\n"
                  << "covers the file-injection / block-quantization half; ground-\n"
                  << "truth onset latency is asserted by libkan_loopback_test.\n";
    }

    if (use_device) ma_device_uninit(&device);
    return 0;
}
