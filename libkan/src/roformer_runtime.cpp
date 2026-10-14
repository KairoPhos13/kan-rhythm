// Adapter over the GGUF RoFormer runtime (BSRoformer.cpp + GGML, both MIT).
// Third-party inference code is compiled into libkan_roformer so the
// official flow needs no installed separator (editor-pipeline s7b).
#include "libkan/roformer_runtime.hpp"

#include "bs_roformer/audio.h"
#include "bs_roformer/inference.h"

#include <cmath>
#include <cstdio>
#include <vector>

namespace kan {

bool roformer_runtime_available() {
    try {
        // Device/backend availability is probed by ggml at backend init,
        // which happens with the first Inference construction; here we only
        // claim compiled-in support. Model acquisition is separate.
        return true;
    } catch (...) {
        return false;
    }
}

int roformer_sample_rate() { return 44100; }
int roformer_stem_count() { return 6; }

namespace {

// Linear resampler (the models are fixed at 44.1 kHz; our pipeline uses
// any rate). Adequate for separation input conditioning.
std::vector<float> resample_stereo(const std::vector<float>& in, int ch,
                                   double from, double to) {
    if (std::fabs(from - to) < 1.0) return in;
    const size_t frames_in = in.size() / size_t(ch);
    const size_t frames_out = size_t(double(frames_in) * to / from);
    std::vector<float> out(frames_out * size_t(ch));
    for (size_t i = 0; i < frames_out; ++i) {
        const double pos = double(i) * from / to;
        const size_t i0 = size_t(pos);
        const double frac = pos - double(i0);
        for (int c = 0; c < ch; ++c) {
            const float a = in[i0 * size_t(ch) + size_t(c)];
            const float b = (i0 + 1 < frames_in) ? in[(i0 + 1) * size_t(ch) + size_t(c)] : a;
            out[i * size_t(ch) + size_t(c)] = float(double(a) + frac * double(b - a));
        }
    }
    return out;
}

} // namespace

bool roformer_separate_wav(const std::string& input_wav,
                           const std::string& out_dir,
                           const std::string& model_path,
                           const std::function<void(float)>& progress) {
    try {
        Inference engine(model_path);
        AudioBuffer buf = AudioFile::Load(input_wav);
        const int want_sr = engine.GetSampleRate();
        const int src_ch = int(buf.channels) >= 2 ? 2 : 1;

        // To interleaved stereo at the model rate.
        std::vector<float> stereo(buf.data);
        if (src_ch == 1) {
            stereo.resize(buf.data.size() * 2);
            for (size_t i = buf.data.size(); i-- > 0;) {
                stereo[2 * i] = buf.data[i];
                stereo[2 * i + 1] = buf.data[i];
            }
        }
        if (double(buf.sampleRate) != double(want_sr)) {
            stereo = resample_stereo(stereo, 2, double(buf.sampleRate), double(want_sr));
        }
        if (progress) progress(0.05f);

        std::function<void(float)> fwd;
        if (progress) {
            fwd = [&](float p) { progress(0.05f + 0.9f * p); };
        }
        std::vector<std::vector<float>> stems =
            engine.Process(stereo, engine.GetDefaultChunkSize(),
                           engine.GetDefaultNumOverlap(), fwd);
        if (stems.empty()) return false;

        static const char* N[6] = {"bass", "drums", "other", "vocals", "guitar", "piano"};
        for (size_t s = 0; s < stems.size() && s < 6; ++s) {
            AudioBuffer out;
            out.data = std::move(stems[s]);
            out.channels = 2;
            out.sampleRate = unsigned(want_sr);
            out.samples = out.data.size();
            AudioFile::Save(out_dir + "/" + N[s] + ".wav", out);
        }
        if (progress) progress(1.0f);
        return true;
    } catch (const std::exception& e) {
        std::fprintf(stderr, "[roformer] %s\n", e.what());
        return false;
    } catch (...) {
        return false;
    }
}

} // namespace kan
