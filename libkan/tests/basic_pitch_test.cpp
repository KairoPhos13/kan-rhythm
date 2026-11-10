// Basic Pitch port (libkan/basic_pitch.hpp): known input, expected output.
//
// 1. Parity: one window through the network against values from the
//    reference onnxruntime run of basic-pitch's nmp.onnx (the same input
//    built the same way in Python; libkan/tools/basic_pitch_export.py
//    documents the model). The full pipeline was also checked on a 220 s
//    ocarina take: 498 of 498 notes identical to the reference's.
// 2. Synthetic melody with exact truth at 44.1 kHz (exercises the
//    resampler, the windowing and the note decoding).
// 3. Determinism across thread counts; the resampler keeps a sine.
#include "libkan/basic_pitch.hpp"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <vector>

namespace {
bool g_pass = true;
void check(bool ok, const char* what) {
    std::printf("  [%s] %s\n", ok ? " ok " : "FAIL", what);
    if (!ok) g_pass = false;
}

constexpr double kPi = 3.14159265358979323846;
namespace bp = kan::basic_pitch;

struct Ref {
    int t, f;
    float v;
};

// From onnxruntime 1.31 on nmp.onnx (sha256 2c3c1d14…): the 8 strongest
// cells of each output and 8 at random.
const Ref kNoteRef[] = {
    {20, 48, 0.890595f}, {19, 48, 0.885383f}, {45, 51, 0.882202f}, {21, 48, 0.880740f}, {46, 51, 0.879518f},
    {89, 43, 0.878786f}, {90, 43, 0.874287f}, {88, 43, 0.868548f}, {162, 55, 0.101085f}, {117, 78, 0.088455f},
    {99, 68, 0.095420f}, {143, 19, 0.097900f}, {9, 26, 0.100100f}, {49, 76, 0.210082f}, {156, 0, 0.112607f},
    {85, 72, 0.100341f},
};
const Ref kOnsetRef[] = {
    {103, 55, 0.968238f}, {102, 55, 0.959175f}, {42, 51, 0.949654f}, {17, 48, 0.949213f}, {86, 43, 0.945909f},
    {43, 51, 0.940608f}, {85, 43, 0.913054f}, {16, 48, 0.909375f}, {22, 70, 0.088962f}, {20, 41, 0.059798f},
    {140, 26, 0.084414f}, {58, 24, 0.076210f}, {123, 22, 0.089850f}, {170, 39, 0.084660f}, {82, 44, 0.101516f},
    {100, 48, 0.091802f},
};
const Ref kContourRef[] = {
    {64, 154, 0.867052f}, {65, 154, 0.866159f}, {63, 154, 0.861997f}, {57, 154, 0.861454f}, {56, 154, 0.857739f},
    {58, 154, 0.856808f}, {66, 154, 0.856600f}, {62, 154, 0.855854f}, {87, 262, 0.097544f}, {138, 209, 0.081161f},
    {120, 164, 0.091277f}, {58, 261, 0.095532f}, {80, 56, 0.089430f}, {145, 42, 0.099766f}, {147, 161, 0.092233f},
    {19, 11, 0.100864f},
};

// The reference window: four overlapping decaying harmonic tones (A4, C5,
// E4, E5), built exactly as the Python side builds it.
std::vector<float> reference_window() {
    constexpr int n = 43844;
    std::vector<double> x(n, 0.0);
    const double tones[4][3] = {{440.0, 0.2, 0.9}, {523.25, 0.5, 1.4}, {329.63, 1.0, 1.9}, {659.25, 1.2, 1.6}};
    for (const auto& tone : tones) {
        for (int i = 0; i < n; ++i) {
            const double t = double(i) / 22050.0;
            const double env = (t >= tone[1] && t < tone[2]) ? std::exp(-2.0 * std::max(t - tone[1], 0.0)) : 0.0;
            for (int h = 1; h < 6; ++h) x[size_t(i)] += env * 0.3 / h * std::sin(2.0 * kPi * tone[0] * h * t);
        }
    }
    std::vector<float> out(x.size());
    for (size_t i = 0; i < x.size(); ++i) out[i] = float(x[i]);
    return out;
}

template <size_t N>
float worst(const Ref (&refs)[N], const std::vector<float>& out, int width) {
    float w = 0.0f;
    for (const Ref& r : refs) w = std::max(w, std::fabs(out[size_t(r.t) * size_t(width) + size_t(r.f)] - r.v));
    return w;
}

// A wind-like tone: harmonics falling off, a 20 ms attack, a 30 ms release.
void add_note(std::vector<float>& x, double sr, int midi, double start, double dur) {
    const double f0 = 440.0 * std::pow(2.0, (midi - 69) / 12.0);
    const size_t a = size_t(start * sr), b = std::min(x.size(), size_t((start + dur) * sr));
    for (size_t i = a; i < b; ++i) {
        const double t = double(i - a) / sr;
        const double env = std::min({1.0, t / 0.02, (dur - t) / 0.03});
        double v = 0.0;
        for (int h = 1; h <= 4; ++h) v += std::sin(2.0 * kPi * f0 * h * t) / (h * h);
        x[i] += float(0.25 * env * v);
    }
}
}  // namespace

int main() {
    std::printf("=== basic pitch test ===\n");

    // 1. One window against the reference.
    {
        const std::vector<float> w = reference_window();
        std::vector<float> note(172 * 88), onset(172 * 88), contour(172 * 264);
        bp::infer_window(w.data(), note.data(), onset.data(), contour.data());
        const float wn = worst(kNoteRef, note, 88), wo = worst(kOnsetRef, onset, 88),
                    wc = worst(kContourRef, contour, 264);
        std::printf("  worst difference to the reference: note %.1e, onset %.1e, contour %.1e\n", wn, wo, wc);
        check(wn < 1e-3f && wo < 1e-3f && wc < 1e-3f, "network output matches the reference within 1e-3");
    }

    // 2. A melody with exact truth: steps, leaps, a repeated note, a rest,
    //    at 44.1 kHz.
    {
        constexpr double sr = 44100.0;
        struct Truth {
            int midi;
            double start, dur;
        };
        const Truth melody[] = {
            {72, 0.50, 0.40}, {74, 1.00, 0.40}, {76, 1.50, 0.40}, {79, 2.00, 0.80}, {79, 3.00, 0.40},
            {77, 3.50, 0.40}, {69, 4.00, 0.60}, {84, 5.20, 0.50}, {67, 6.00, 0.40}, {72, 6.50, 1.00},
        };
        std::vector<float> x(size_t(8.0 * sr), 0.0f);
        for (const Truth& n : melody) add_note(x, sr, n.midi, n.start, n.dur);

        const bp::Posteriorgrams p = bp::infer(x.data(), x.size(), sr);

        // As the reference decodes it, a strong second harmonic also shows
        // up as a note an octave up (the model is polyphonic).
        const std::vector<bp::Note> poly = bp::notes(p);
        int octave_ghosts = 0;
        for (const bp::Note& g : poly) {
            for (const Truth& t : melody) {
                if (g.midi == t.midi + 12 && std::fabs(g.start_s - t.start) <= 0.05) ++octave_ghosts;
            }
        }
        std::printf("  polyphonic decoding: %zu notes, %d of them the octave above a melody note\n", poly.size(),
                    octave_ghosts);
        check(poly.size() == 10 + size_t(octave_ghosts), "polyphonic: the melody plus octave harmonics only");

        bp::NoteOptions mono;
        mono.monophonic = true;
        const std::vector<bp::Note> got = bp::notes(p, mono);

        int found = 0;
        double worst_onset = 0.0;
        std::vector<bool> used(got.size(), false);
        for (const Truth& t : melody) {
            for (size_t i = 0; i < got.size(); ++i) {
                if (!used[i] && got[i].midi == t.midi && std::fabs(got[i].start_s - t.start) <= 0.05) {
                    used[i] = true;
                    ++found;
                    worst_onset = std::max(worst_onset, std::fabs(got[i].start_s - t.start));
                    break;
                }
            }
        }
        int extra = 0;
        for (size_t i = 0; i < got.size(); ++i) {
            if (!used[i]) {
                ++extra;
                std::printf("  extra: %.3f-%.3f midi %d amp %.2f\n", got[i].start_s, got[i].end_s, got[i].midi,
                            got[i].amplitude);
            }
        }
        std::printf("  monophonic: %d of 10 notes (onset within 50 ms, right pitch), %d extra, worst onset %.0f ms\n",
                    found, extra, worst_onset * 1000.0);
        check(found == 10, "monophonic: every note of the melody is found on time with its pitch");
        check(extra == 0, "monophonic: no extra notes");

        // 3. Same result whatever the number of threads.
        const bp::Posteriorgrams one = bp::infer(x.data(), x.size(), sr, 1);
        check(one.frames == p.frames && one.note == p.note && one.onset == p.onset,
              "deterministic across thread counts");
    }

    // The resampler keeps a 1 kHz sine (level and frequency) from 44.1 and
    // 48 kHz (147/320 phases) and from a non-integer rate down to 22.05 kHz.
    for (const double from : {44100.0, 48000.0, 44100.5}) {
        std::vector<float> s(static_cast<size_t>(from));
        for (size_t i = 0; i < s.size(); ++i) s[i] = float(0.5 * std::sin(2.0 * kPi * 1000.0 * double(i) / from));
        const std::vector<float> r = bp::resample(s.data(), s.size(), from, 22050.0);
        double err = 0.0;
        for (size_t i = 1000; i + 1000 < r.size(); ++i) {
            err = std::max(err, std::fabs(r[i] - 0.5 * std::sin(2.0 * kPi * 1000.0 * double(i) / 22050.0)));
        }
        std::printf("  resampled sine from %.1f Hz: %zu samples, worst error %.1e\n", from, r.size(), err);
        check(r.size() == size_t(std::floor(double(s.size()) * 22050.0 / from)) && err < 2e-3,
              "resampler keeps a 1 kHz sine");
    }

    std::printf("%s\n", g_pass ? "PASS" : "FAIL");
    return g_pass ? 0 : 1;
}
