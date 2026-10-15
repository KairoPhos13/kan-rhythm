// Basic Pitch (Spotify AB, Apache-2.0) ported to plain C++ for libkan.
// See include/libkan/basic_pitch.hpp. Each step names the reference code
// it follows (basic-pitch fa5997a: models.py, layers/nnaudio.py,
// layers/signal.py, nn.py, inference.py, note_creation.py).
#include "libkan/basic_pitch.hpp"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <queue>
#include <thread>

namespace kan::basic_pitch {
namespace {

#include "basic_pitch_weights.inc"

constexpr int kWindow = 43844;          // AUDIO_N_SAMPLES = 22050 * 2 - 256
constexpr int kFrames = 172;            // frames per window
constexpr int kOverlapFrames = 30;      // DEFAULT_OVERLAPPING_FRAMES
constexpr int kOverlap = kOverlapFrames * kHop;
constexpr int kWindowHop = kWindow - kOverlap;
constexpr int kOctaves = 9;
constexpr int kBinsPerOctave = 36;
constexpr int kCqtBins = 309;           // 103 semitones x 3
constexpr int kKernel = 256;            // CQT and low-pass kernel length
constexpr int kStack = 8;               // harmonics 0.5, 1, 2 .. 7
constexpr int kShifts[kStack] = {-36, 0, 36, 57, 72, 84, 93, 101};

// ---- CQT2010v2 (layers/nnaudio.py) ----------------------------------
// The top octave's 36 kernels slide over the input with the full hop;
// the input is then low-passed and halved, and the same kernels give the
// next octave down with half the hop. Reflect padding by half a kernel
// centres the frames (CQT2010v2, pad_mode "reflect").

std::vector<float> reflect_pad(const std::vector<float>& x, int pad) {
    const int n = int(x.size());
    std::vector<float> out(static_cast<size_t>(n + 2 * pad));
    for (int i = 0; i < n + 2 * pad; ++i) {
        int j = i - pad;
        if (j < 0) j = -j;
        if (j >= n) j = 2 * (n - 1) - j;
        out[size_t(i)] = x[size_t(j)];
    }
    return out;
}

// downsampling_by_n(..., match_torch_exactly=True): zero pad 127 each side,
// then the 256-tap low-pass with stride 2.
std::vector<float> halve(const std::vector<float>& x) {
    const int n = int(x.size());
    const int pad = (kKernel - 1) / 2;
    const int out_n = (n + 2 * pad - kKernel) / 2 + 1;
    std::vector<float> out(static_cast<size_t>(out_n));
    for (int o = 0; o < out_n; ++o) {
        const int base = o * 2 - pad;
        float acc = 0.0f;
        for (int k = 0; k < kKernel; ++k) {
            const int j = base + k;
            if (j >= 0 && j < n) acc += x[size_t(j)] * kLowpass[k];
        }
        out[size_t(o)] = acc;
    }
    return out;
}

// Log-normalised CQT magnitude of one window: [kFrames][kCqtBins].
void cqt(const float* window, float* out) {
    std::vector<float> sig(window, window + kWindow);
    for (int oct = 0; oct < kOctaves; ++oct) {
        const int hop = kHop >> oct;
        const std::vector<float> padded = reflect_pad(sig, kKernel / 2);
        const int frames = int(sig.size()) / hop + 1;
        // Every octave yields the same frame count for this window size.
        const int base_bin = (kOctaves - 1 - oct) * kBinsPerOctave;  // low octaves first
        for (int t = 0; t < kFrames && t < frames; ++t) {
            const float* seg = padded.data() + size_t(t) * size_t(hop);
            for (int k = 0; k < kBinsPerOctave; ++k) {
                const float* kr = kCqtReal + k * kKernel;
                const float* ki = kCqtImag + k * kKernel;
                float re = 0.0f, im = 0.0f;
                for (int j = 0; j < kKernel; ++j) {
                    re += seg[j] * kr[j];
                    im += seg[j] * ki[j];
                }
                // Kept bins: the top kCqtBins of the 9 octaves.
                const int b = base_bin + k - (kOctaves * kBinsPerOctave - kCqtBins);
                if (b >= 0) {
                    const float s = kSqrtLengths[b];
                    const float r = re * s, i = -im * s;
                    out[size_t(t) * kCqtBins + size_t(b)] = std::sqrt(r * r + i * i);
                }
            }
        }
        if (oct + 1 < kOctaves) sig = halve(sig);
    }

    // NormalizedLog (layers/signal.py): power in dB, shifted to start at 0
    // and scaled to end at 1 over the whole window.
    const size_t cells = size_t(kFrames) * kCqtBins;
    float lo = INFINITY;
    for (size_t i = 0; i < cells; ++i) {
        const float p = out[i] * out[i] + 1e-10f;
        out[i] = std::log(p) * 0.4342944622039795f * 10.0f;
        lo = std::min(lo, out[i]);
    }
    float hi = -INFINITY;
    for (size_t i = 0; i < cells; ++i) {
        out[i] -= lo;
        hi = std::max(hi, out[i]);
    }
    for (size_t i = 0; i < cells; ++i) out[i] = hi == 0.0f ? 0.0f : out[i] / hi;
}

// ---- convolutions (models.py), tensors [channels][kFrames][width] ----
struct Conv {
    const float* w;
    const float* b;
    int out_ch, in_ch, kt, kf, stride_f, pad_t, pad_f;
};

void conv2d(const Conv& c, const float* in, int in_w, float* out, int out_w) {
    for (int o = 0; o < c.out_ch; ++o) {
        float* dst = out + size_t(o) * kFrames * size_t(out_w);
        std::fill(dst, dst + size_t(kFrames) * size_t(out_w), c.b[o]);
        for (int ci = 0; ci < c.in_ch; ++ci) {
            const float* src = in + size_t(ci) * kFrames * size_t(in_w);
            for (int a = 0; a < c.kt; ++a) {
                for (int k = 0; k < c.kf; ++k) {
                    const float w = c.w[((size_t(o) * c.in_ch + size_t(ci)) * c.kt + size_t(a)) * c.kf + size_t(k)];
                    // Output columns whose input column f*stride + k - pad is inside.
                    const int f0 = std::max(0, (c.pad_f - k + c.stride_f - 1) / c.stride_f);
                    const int f1 = std::min(out_w, (in_w - 1 + c.pad_f - k) / c.stride_f + 1);
                    for (int t = 0; t < kFrames; ++t) {
                        const int ts = t + a - c.pad_t;
                        if (ts < 0 || ts >= kFrames) continue;
                        const float* row = src + size_t(ts) * size_t(in_w);
                        float* drow = dst + size_t(t) * size_t(out_w);
                        if (c.stride_f == 1) {
                            const int shift = k - c.pad_f;
                            for (int f = f0; f < f1; ++f) drow[f] += w * row[f + shift];
                        } else {
                            for (int f = f0; f < f1; ++f) drow[f] += w * row[f * c.stride_f + k - c.pad_f];
                        }
                    }
                }
            }
        }
    }
}

// NoteOptions::monophonic. Input sorted by start.
std::vector<Note> one_at_a_time(std::vector<Note> in) {
    auto overlap = [](const Note& a, const Note& b) {
        return std::max(0.0, std::min(a.end_s, b.end_s) - std::max(a.start_s, b.start_s));
    };
    auto mostly = [&](const Note& covered, const Note& by) {
        return overlap(covered, by) >= 0.5 * (covered.end_s - covered.start_s);
    };

    // Harmonics reported as notes.
    std::vector<bool> drop(in.size(), false);
    for (size_t i = 0; i < in.size(); ++i) {
        for (size_t j = 0; j < in.size() && !drop[i]; ++j) {
            const int up = in[i].midi - in[j].midi;
            if ((up == 12 || up == 19 || up == 24) && mostly(in[i], in[j])) drop[i] = true;
        }
    }
    std::vector<Note> kept;
    for (size_t i = 0; i < in.size(); ++i) {
        if (!drop[i]) kept.push_back(in[i]);
    }

    // Overlaps left: the weaker of two mostly overlapping notes goes; a
    // shorter overlap ends the earlier note.
    std::vector<Note> out;
    for (const Note& n : kept) {
        if (!out.empty() && overlap(out.back(), n) > 0.0) {
            Note& prev = out.back();
            if (mostly(n, prev) || mostly(prev, n)) {
                if (n.amplitude > prev.amplitude) prev = n;
                continue;
            }
            prev.end_s = n.start_s;
        }
        out.push_back(n);
    }
    return out;
}

inline float sigmoid(float x) { return 1.0f / (1.0f + std::exp(-x)); }

void relu(std::vector<float>& v) {
    for (float& x : v) x = std::max(x, 0.0f);
}

}  // namespace

void infer_window(const float* window, float* note, float* onset, float* contour) {
    // Input representation: log CQT, batch norm, harmonic stacking.
    std::vector<float> spec(size_t(kFrames) * kCqtBins);
    cqt(window, spec.data());
    for (float& v : spec) v = v * kInputBnScale + kInputBnShift;

    std::vector<float> stacked(size_t(kStack) * kFrames * kContourBins, 0.0f);
    for (int h = 0; h < kStack; ++h) {
        for (int t = 0; t < kFrames; ++t) {
            float* dst = stacked.data() + (size_t(h) * kFrames + size_t(t)) * kContourBins;
            const float* src = spec.data() + size_t(t) * kCqtBins;
            for (int f = 0; f < kContourBins; ++f) {
                const int j = f + kShifts[h];
                if (j >= 0 && j < kCqtBins) dst[f] = src[j];
            }
        }
    }

    // Contours: conv 3x39 (+ folded batch norm) + ReLU, conv 5x5 + sigmoid.
    std::vector<float> c1(size_t(8) * kFrames * kContourBins);
    conv2d({k_contour1_w, k_contour1_b, 8, 8, 3, 39, 1, 1, 19}, stacked.data(), kContourBins, c1.data(), kContourBins);
    relu(c1);
    std::vector<float> cont(size_t(kFrames) * kContourBins);
    conv2d({k_contour2_w, k_contour2_b, 1, 8, 5, 5, 1, 2, 2}, c1.data(), kContourBins, cont.data(), kContourBins);
    for (float& v : cont) v = sigmoid(v);

    // Notes: conv 7x7 stride 3 + ReLU, conv 7x3 + sigmoid.
    std::vector<float> n1(size_t(32) * kFrames * kNotes);
    conv2d({k_note1_w, k_note1_b, 32, 1, 7, 7, 3, 3, 2}, cont.data(), kContourBins, n1.data(), kNotes);
    relu(n1);
    std::vector<float> notes_and_onsets(size_t(33) * kFrames * kNotes);
    float* nt = notes_and_onsets.data();
    conv2d({k_note2_w, k_note2_b, 1, 32, 7, 3, 1, 3, 1}, n1.data(), kNotes, nt, kNotes);
    for (size_t i = 0; i < size_t(kFrames) * kNotes; ++i) nt[i] = sigmoid(nt[i]);

    // Onsets: conv 5x5 stride 3 (+ folded batch norm) + ReLU on the stacked
    // CQT, joined with the notes, conv 3x3 + sigmoid.
    std::vector<float> o1(size_t(32) * kFrames * kNotes);
    conv2d({k_onset1_w, k_onset1_b, 32, 8, 5, 5, 3, 2, 1}, stacked.data(), kContourBins, o1.data(), kNotes);
    relu(o1);
    std::copy(o1.begin(), o1.end(), notes_and_onsets.begin() + std::ptrdiff_t(size_t(kFrames) * kNotes));
    std::vector<float> on(size_t(kFrames) * kNotes);
    conv2d({k_onset2_w, k_onset2_b, 1, 33, 3, 3, 1, 1, 1}, notes_and_onsets.data(), kNotes, on.data(), kNotes);

    for (size_t i = 0; i < size_t(kFrames) * kNotes; ++i) {
        note[i] = nt[i];
        onset[i] = sigmoid(on[i]);
    }
    std::copy(cont.begin(), cont.end(), contour);
}

std::vector<float> resample(const float* audio, size_t n, double from_rate, double to_rate) {
    if (n == 0) return {};
    if (from_rate == to_rate) return std::vector<float>(audio, audio + n);
    const double ratio = to_rate / from_rate;
    const double cutoff = std::min(1.0, ratio) * 0.97;  // of the input Nyquist
    constexpr double kZeroCrossings = 32.0;
    const double half = kZeroCrossings / cutoff;        // input samples each side
    const size_t out_n = size_t(std::floor(double(n) * ratio));
    std::vector<float> out(out_n);
    constexpr double kPi = 3.14159265358979323846;
    for (size_t j = 0; j < out_n; ++j) {
        const double x = double(j) / ratio;
        const long k0 = long(std::ceil(x - half));
        const long k1 = long(std::floor(x + half));
        double acc = 0.0;
        for (long k = std::max(0L, k0); k <= std::min(long(n) - 1, k1); ++k) {
            const double d = x - double(k);
            const double u = cutoff * d;
            const double sinc = u == 0.0 ? 1.0 : std::sin(kPi * u) / (kPi * u);
            const double w = 0.42 + 0.5 * std::cos(kPi * d / half) + 0.08 * std::cos(2.0 * kPi * d / half);  // Blackman
            acc += double(audio[k]) * cutoff * sinc * w;
        }
        out[j] = float(acc);
    }
    return out;
}

double Posteriorgrams::frame_time(int i) const {
    // note_creation.model_frames_to_time: the reference's per-window drift
    // correction (frames counted in 172-frame windows) plus its alignment offset.
    constexpr double kMagicAlignmentOffset = 0.0018;
    const double window_offset = (double(kHop) / kSampleRate) * (kFrames - double(kWindow) / kHop) + kMagicAlignmentOffset;
    return double(i) * kHop / kSampleRate - window_offset * std::floor(double(i) / kFrames);
}

Posteriorgrams infer(const float* audio, size_t n, double sample_rate, int threads) {
    // inference.run_inference: half an overlap of silence first, 2 s windows
    // every kWindowHop samples, then half the overlap cut from each side.
    const std::vector<float> sig = resample(audio, n, sample_rate, kSampleRate);
    std::vector<float> padded(size_t(kOverlap / 2), 0.0f);
    padded.insert(padded.end(), sig.begin(), sig.end());

    const size_t windows = (padded.size() + kWindowHop - 1) / kWindowHop;
    constexpr int kKeep = kFrames - kOverlapFrames;
    std::vector<float> note(windows * kKeep * kNotes), onset(windows * kKeep * kNotes),
        contour(windows * kKeep * kContourBins);

    std::atomic<size_t> next{0};
    auto work = [&]() {
        std::vector<float> win(kWindow);
        std::vector<float> wn(size_t(kFrames) * kNotes), wo(size_t(kFrames) * kNotes),
            wc(size_t(kFrames) * kContourBins);
        for (size_t w; (w = next.fetch_add(1)) < windows;) {
            const size_t start = w * kWindowHop;
            std::fill(win.begin(), win.end(), 0.0f);
            const size_t avail = std::min(size_t(kWindow), padded.size() - start);
            std::copy(padded.begin() + std::ptrdiff_t(start), padded.begin() + std::ptrdiff_t(start + avail), win.begin());
            infer_window(win.data(), wn.data(), wo.data(), wc.data());
            const size_t skip = kOverlapFrames / 2;
            std::copy(wn.begin() + std::ptrdiff_t(skip * kNotes), wn.begin() + std::ptrdiff_t((skip + kKeep) * kNotes),
                      note.begin() + std::ptrdiff_t(w * kKeep * kNotes));
            std::copy(wo.begin() + std::ptrdiff_t(skip * kNotes), wo.begin() + std::ptrdiff_t((skip + kKeep) * kNotes),
                      onset.begin() + std::ptrdiff_t(w * kKeep * kNotes));
            std::copy(wc.begin() + std::ptrdiff_t(skip * kContourBins),
                      wc.begin() + std::ptrdiff_t((skip + kKeep) * kContourBins),
                      contour.begin() + std::ptrdiff_t(w * kKeep * kContourBins));
        }
    };
    int count = threads > 0 ? threads : int(std::max(1u, std::thread::hardware_concurrency()));
    count = int(std::min<size_t>(size_t(count), std::max<size_t>(1, windows)));
    std::vector<std::thread> pool;
    for (int i = 1; i < count; ++i) pool.emplace_back(work);
    work();
    for (auto& t : pool) t.join();

    // unwrap_output: trim to the frames the original length accounts for.
    Posteriorgrams p;
    const double expected_windows = double(sig.size()) / kWindowHop;
    p.frames = int(std::min(double(windows * kKeep), std::floor(expected_windows * kKeep)));
    note.resize(size_t(p.frames) * kNotes);
    onset.resize(size_t(p.frames) * kNotes);
    contour.resize(size_t(p.frames) * kContourBins);
    p.note = std::move(note);
    p.onset = std::move(onset);
    p.contour = std::move(contour);
    return p;
}

std::vector<Note> notes(const Posteriorgrams& p, const NoteOptions& opt) {
    // note_creation.output_to_notes_polyphonic + get_pitch_bends.
    const int n = p.frames;
    if (n <= 0) return {};
    const int min_len = int(std::lround(opt.min_note_ms / 1000.0 * (double(kSampleRate) / kHop)));
    constexpr int kEnergyTol = 11;
    constexpr int kMaxFreqIdx = 87;
    const float onset_t = opt.onset_threshold, frame_t = opt.frame_threshold;

    std::vector<float> frames = p.note, onsets = p.onset;
    auto at = [](std::vector<float>& m, int t, int f) -> float& { return m[size_t(t) * kNotes + size_t(f)]; };

    // constrain_frequency (min/max as MIDI, both inclusive).
    const int lo = opt.min_midi > 0 ? opt.min_midi - kMidiOffset : 0;
    const int hi = opt.max_midi > 0 ? opt.max_midi - kMidiOffset + 1 : kNotes;
    for (int t = 0; t < n; ++t) {
        for (int f = 0; f < kNotes; ++f) {
            if (f < lo || f >= hi) at(frames, t, f) = at(onsets, t, f) = 0.0f;
        }
    }

    // get_infered_onsets: the rise of the note activation over 1 and 2
    // frames, scaled to the onsets' maximum, also counts as an onset.
    if (opt.infer_onsets) {
        std::vector<float> diff(frames.size(), 0.0f);
        float max_diff = 0.0f, max_onset = 0.0f;
        for (float v : onsets) max_onset = std::max(max_onset, v);
        for (int t = 2; t < n; ++t) {
            for (int f = 0; f < kNotes; ++f) {
                const float d = std::max(0.0f, std::min(at(frames, t, f) - at(frames, t - 1, f),
                                                        at(frames, t, f) - at(frames, t - 2, f)));
                at(diff, t, f) = d;
                max_diff = std::max(max_diff, d);
            }
        }
        if (max_diff > 0.0f) {
            for (size_t i = 0; i < onsets.size(); ++i) onsets[i] = std::max(onsets[i], max_onset * diff[i] / max_diff);
        }
    }

    struct Raw {
        int start, end, f;
        float amp;
    };
    std::vector<Raw> raw;
    std::vector<float> remaining = frames;
    auto clear = [&](int t, int f) {
        at(remaining, t, f) = 0.0f;
        if (f < kMaxFreqIdx) at(remaining, t, f + 1) = 0.0f;
        if (f > 0) at(remaining, t, f - 1) = 0.0f;
    };
    auto mean = [&](int a, int b, int f) {
        double s = 0.0;
        for (int t = a; t < b; ++t) s += at(frames, t, f);
        return float(b > a ? s / (b - a) : 0.0);
    };

    // Onset peaks (scipy argrelmax over time, edges excluded) above the
    // threshold, latest first.
    std::vector<std::pair<int, int>> peaks;
    for (int t = 1; t + 1 < n; ++t) {
        for (int f = 0; f < kNotes; ++f) {
            const float v = at(onsets, t, f);
            if (v > at(onsets, t - 1, f) && v > at(onsets, t + 1, f) && v >= onset_t) peaks.emplace_back(t, f);
        }
    }
    for (auto it = peaks.rbegin(); it != peaks.rend(); ++it) {
        const auto [start, f] = *it;
        if (start >= n - 1) continue;
        int i = start + 1, k = 0;
        while (i < n - 1 && k < kEnergyTol) {
            k = at(remaining, i, f) < frame_t ? k + 1 : 0;
            ++i;
        }
        i -= k;
        if (i - start <= min_len) continue;
        for (int t = start; t < i; ++t) clear(t, f);
        raw.push_back({start, i, f, mean(start, i, f)});
    }

    // The "melodia trick": notes without an onset, grown from the strongest
    // remaining activation forwards and backwards.
    if (opt.melodia_trick) {
        using Cell = std::pair<float, int64_t>;  // value, -index (first max wins ties)
        std::priority_queue<Cell> heap;
        for (size_t i = 0; i < remaining.size(); ++i) {
            if (remaining[i] > frame_t) heap.emplace(remaining[i], -int64_t(i));
        }
        while (!heap.empty()) {
            const auto [v, neg] = heap.top();
            heap.pop();
            const size_t idx = size_t(-neg);
            if (remaining[idx] != v) continue;  // cleared since
            const int i_mid = int(idx / kNotes), f = int(idx % kNotes);
            remaining[idx] = 0.0f;

            int i = i_mid + 1, k = 0;
            while (i < n - 1 && k < kEnergyTol) {
                k = at(remaining, i, f) < frame_t ? k + 1 : 0;
                clear(i, f);
                ++i;
            }
            const int i_end = i - 1 - k;

            i = i_mid - 1;
            k = 0;
            while (i > 0 && k < kEnergyTol) {
                k = at(remaining, i, f) < frame_t ? k + 1 : 0;
                clear(i, f);
                --i;
            }
            const int i_start = i + 1 + k;

            if (i_end - i_start <= min_len) continue;
            raw.push_back({i_start, i_end, f, mean(i_start, i_end, f)});
        }
    }

    // Pitch bends from the contours, in 1/3 semitones (get_pitch_bends);
    // the note's cents are their median.
    constexpr int kTol = 25;
    float gauss[2 * kTol + 1];
    for (int j = 0; j <= 2 * kTol; ++j) {
        const double d = j - kTol;
        gauss[j] = float(std::exp(-0.5 * d * d / 25.0));
    }
    std::vector<Note> out;
    out.reserve(raw.size());
    for (const Raw& r : raw) {
        const int fi = 3 * r.f;
        const int a = std::max(0, fi - kTol), b = std::min(kContourBins, fi + kTol + 1);
        std::vector<int> bends;
        for (int t = r.start; t < r.end; ++t) {
            int best = a;
            float best_v = -1.0f;
            for (int c = a; c < b; ++c) {
                const float v = p.contour[size_t(t) * kContourBins + size_t(c)] * gauss[c - fi + kTol];
                if (v > best_v) {
                    best_v = v;
                    best = c;
                }
            }
            bends.push_back(best - fi);
        }
        float cents = 0.0f;
        if (!bends.empty()) {
            std::sort(bends.begin(), bends.end());
            const size_t m = bends.size() / 2;
            const float median = bends.size() % 2 ? float(bends[m]) : 0.5f * float(bends[m - 1] + bends[m]);
            cents = std::clamp(median * 100.0f / 3.0f, -50.0f, 50.0f);
        }
        out.push_back({p.frame_time(r.start), p.frame_time(r.end), r.f + kMidiOffset, r.amp, cents});
    }
    std::sort(out.begin(), out.end(), [](const Note& x, const Note& y) {
        return x.start_s != y.start_s ? x.start_s < y.start_s : x.midi < y.midi;
    });
    return opt.monophonic ? one_at_a_time(std::move(out)) : out;
}

}  // namespace kan::basic_pitch
