#pragma once
// Per-note description stream ("register by note") — libkan describes what
// was played; it never judges (docs/architecture/note-description-stream.md).
//
// Each attack opens a note record and facts are added as soon as they become
// available, one fixed-size POD event at a time:
//   HIT        onset time + strength. Fixed at the onset, never revised.
//   PITCH      one provisional estimate, then one final estimate (or
//              "unresolved"). One consistent estimate per event: midi +
//              cents, with Hz derived from that pair — never numbers from
//              different estimators.
//   CONTENT    mono vs polyphonic attack (blind, candidate-free); when the
//              caller supplies candidate pitches, one fact per candidate:
//              presence, cents, amplitude (+ the explained-energy of the
//              whole set). Candidate facts carry both note ids: the attack
//              that produced the window and the record the candidate
//              belongs to.
//   EXPRESSION envelope (attack/release/sustain), vibrato, dynamics level.
//   CLOSE      note end (decay-relative) + summary.
//
// Honesty rules (the fairness rule of the design doc): the stream never
// emits a number the tool distrusts. Flags describe audio-true states only
// (MIX/HOP/POLYPHONIC/DOMINANT/UNEXPLAINED_ENERGY/LOW_REGISTER/
// SEARCH_SATURATED) — never correct/wrong. Unmeasurable means `unresolved`,
// which downstream must treat as "not measured", never as "wrong".
//
// Threading/RT: the audio callback is untouched (it keeps pushing samples
// into the existing ring). `process_block` is the analysis fast path and is
// allocation-free; `finalize_due()` performs at most one pending long-window
// fit per call (the expensive part) so an app may run it on a worker thread.
// All scratch is allocated in constructors. The event stream is lock-free
// SPSC per consumer.
//
// Determinism: the stream is a pure function of (sample stream at a given
// block size, candidate sequence). All times derive from sample positions;
// no wall clocks, no scheduling state. Same input -> byte-identical JSONL
// (the live/offline equivalence property).
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <type_traits>
#include <vector>

#include "dsp_detector.hpp"
#include "harmonic_chord.hpp"
#include "instrument_profile.hpp"
#include "technique.hpp"

namespace kan {

// ---------------------------------------------------------------------------
// Event vocabulary.

enum class NoteKind : uint8_t { Hit = 0, Pitch, Content, Expression, Close };
enum class PitchStage : uint8_t { Provisional = 0, Final, Unresolved };
enum class ContentClass : uint8_t { Unknown = 0, Mono, Polyphonic };

/// Audio-true states only — never judgment.
enum NoteFlags : uint32_t {
    kFlagNone = 0,
    kFlagMixed = 1u << 0,             ///< mono pitch validity < 0.5 (overlapped)
    kFlagHop = 1u << 1,               ///< tracker hopping between sources
    kFlagPolyphonic = 1u << 2,        ///< attack classified polyphonic
    kFlagDominant = 1u << 3,          ///< one candidate clearly dominant this window
    kFlagUnexplainedEnergy = 1u << 4, ///< candidate set does not explain the energy
    kFlagLowRegister = 1u << 5,       ///< estimate below C3 on sub-ideal window length
    kFlagSearchSaturated = 1u << 6,   ///< refined cents hit the search bound: no evidence
};

/// Fixed-size, trivially copyable per-note fact. One kind of payload is
/// active per event (see factories in note_event).
struct NoteEvent {
    uint32_t note_id = 0;         ///< record this fact belongs to (monotonic)
    uint32_t flags = 0;           ///< NoteFlags
    float t_ms = 0.0f;            ///< stream time of the fact (evidence window end)
    NoteKind kind = NoteKind::Hit;
    PitchStage stage = PitchStage::Provisional;
    ContentClass klass = ContentClass::Unknown;
    uint8_t candidate_index = 0;

    union Payload {
        struct { float onset_ms; float strength; } hit;
        struct { float midi; float cents; float hz; } pitch;
        struct { float midi; float presence; float cents; float amplitude;
                 float similarity; uint32_t cand_note_id; } candidate;
        struct { float n_unresolved; float dominant_share; } content;
        struct { float dynamics_db; float attack_ms; float release_ms;
                 float sustain_ratio; float vib_rate_hz; float vib_depth_c; } expression;
        struct { float end_ms; float duration_ms; float peak_rms; } close;
    } u;
};

static_assert(std::is_trivially_copyable<NoteEvent>::value,
              "NoteEvent must stay POD for the lock-free stream");

namespace note_event {

/// HIT — fixed at the moment of the onset; never revised or superseded.
inline NoteEvent make_hit(uint32_t note_id, float t_ms, float onset_ms,
                          float strength) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.kind = NoteKind::Hit;
    e.u.hit.onset_ms = onset_ms;
    e.u.hit.strength = strength;
    return e;
}

/// PITCH — one consistent estimate (midi + cents; Hz derived from the pair).
inline NoteEvent make_pitch(uint32_t note_id, float t_ms, PitchStage stage,
                            float midi, float cents, uint32_t flags) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.flags = flags;
    e.kind = NoteKind::Pitch;
    e.stage = stage;
    e.u.pitch.midi = midi;
    e.u.pitch.cents = cents;
    e.u.pitch.hz = 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f + cents / 1200.0f);
    return e;
}

/// PITCH — nothing measurable ("unresolved"): no pitch numbers at all.
inline NoteEvent make_pitch_unresolved(uint32_t note_id, float t_ms,
                                       PitchStage stage, uint32_t flags) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.flags = flags;
    e.kind = NoteKind::Pitch;
    e.stage = stage;
    e.u.pitch.midi = 0.0f;
    e.u.pitch.cents = 0.0f;
    e.u.pitch.hz = 0.0f;
    return e;
}

/// CONTENT class — blind mono-vs-polyphonic statement of the attack.
inline NoteEvent make_content_class(uint32_t note_id, float t_ms, ContentClass klass,
                                    float n_unresolved, float dominant_share,
                                    uint32_t flags) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.flags = flags;
    e.kind = NoteKind::Content;
    e.klass = klass;
    e.u.content.n_unresolved = n_unresolved;
    e.u.content.dominant_share = dominant_share;
    return e;
}

/// CONTENT candidate — one measurement of one caller-supplied hypothesis.
inline NoteEvent make_content_candidate(uint32_t note_id, float t_ms,
                                        uint8_t candidate_index, uint32_t cand_note_id,
                                        float midi, float presence, float cents,
                                        float amplitude, float similarity,
                                        uint32_t flags) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.flags = flags;
    e.kind = NoteKind::Content;
    e.candidate_index = candidate_index;
    e.u.candidate.midi = midi;
    e.u.candidate.presence = presence;
    e.u.candidate.cents = cents;
    e.u.candidate.amplitude = amplitude;
    e.u.candidate.similarity = similarity;
    e.u.candidate.cand_note_id = cand_note_id;
    return e;
}

/// EXPRESSION — how the note was played (measurements only).
inline NoteEvent make_expression(uint32_t note_id, float t_ms, float dynamics_db,
                                 float attack_ms, float release_ms, float sustain_ratio,
                                 float vib_rate_hz, float vib_depth_c) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.kind = NoteKind::Expression;
    e.u.expression.dynamics_db = dynamics_db;
    e.u.expression.attack_ms = attack_ms;
    e.u.expression.release_ms = release_ms;
    e.u.expression.sustain_ratio = sustain_ratio;
    e.u.expression.vib_rate_hz = vib_rate_hz;
    e.u.expression.vib_depth_c = vib_depth_c;
    return e;
}

/// CLOSE — the note is over (decay-relative end); never revises earlier facts.
inline NoteEvent make_close(uint32_t note_id, float t_ms, float end_ms,
                            float duration_ms, float peak_rms) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.kind = NoteKind::Close;
    e.u.close.end_ms = end_ms;
    e.u.close.duration_ms = duration_ms;
    e.u.close.peak_rms = peak_rms;
    return e;
}

} // namespace note_event

// ---------------------------------------------------------------------------
// JSONL serialization (schema v1, docs/architecture/note-description-stream.md
// §5.2). Deterministic key order so live and offline logs diff cleanly.

/// Writes one JSON line (including the trailing newline). Returns the number
/// of bytes written, or 0 if `cap` is too small. No allocation.
inline size_t note_event_jsonl(const NoteEvent& e, char* buf, size_t cap) noexcept {
    static const char* kFlagNames[7] = {"MIX", "HOP", "POLYPHONIC", "DOMINANT",
                                        "UNEXPLAINED_ENERGY", "LOW_REGISTER",
                                        "SEARCH_SATURATED"};
    char flags[128];
    size_t fp = 0;
    flags[fp++] = '[';
    bool first = true;
    for (int i = 0; i < 7; ++i) {
        if ((e.flags & (1u << i)) == 0) continue;
        if (!first) flags[fp++] = ',';
        first = false;
        fp += size_t(std::snprintf(flags + fp, sizeof(flags) - fp, "\"%s\"", kFlagNames[i]));
        if (fp >= sizeof(flags) - 2) break;
    }
    flags[fp++] = ']';
    flags[fp] = '\0';

    char head[192];
    std::snprintf(head, sizeof(head), "{\"v\":1,\"note\":%u,\"kind\":\"%s\",\"t_ms\":%.2f",
                  e.note_id,
                  e.kind == NoteKind::Hit ? "HIT"
                      : e.kind == NoteKind::Pitch ? "PITCH"
                      : e.kind == NoteKind::Content ? "CONTENT"
                      : e.kind == NoteKind::Expression ? "EXPRESSION" : "CLOSE",
                  double(e.t_ms));

    char body[384];
    switch (e.kind) {
    case NoteKind::Hit:
        std::snprintf(body, sizeof(body), ",\"onset_ms\":%.2f,\"strength\":%.4f",
                      double(e.u.hit.onset_ms), double(e.u.hit.strength));
        break;
    case NoteKind::Pitch:
        if (e.stage == PitchStage::Unresolved || e.u.pitch.hz <= 0.0f) {
            std::snprintf(body, sizeof(body),
                          ",\"stage\":\"unresolved\",\"midi\":null,\"cents\":null,\"hz\":null");
        } else {
            std::snprintf(body, sizeof(body),
                          ",\"stage\":\"%s\",\"midi\":%.0f,\"cents\":%.2f,\"hz\":%.3f",
                          e.stage == PitchStage::Final ? "final" : "provisional",
                          double(e.u.pitch.midi), double(e.u.pitch.cents),
                          double(e.u.pitch.hz));
        }
        break;
    case NoteKind::Content:
        if (e.klass != ContentClass::Unknown) {
            std::snprintf(body, sizeof(body),
                          ",\"class\":\"%s\",\"n_unresolved\":%.0f,\"dominant_share\":%.3f",
                          e.klass == ContentClass::Mono ? "mono" : "polyphonic",
                          double(e.u.content.n_unresolved),
                          double(e.u.content.dominant_share));
        } else {
            std::snprintf(body, sizeof(body),
                          ",\"cand\":%u,\"cand_note\":%u,\"midi\":%.0f,\"presence\":%.3f,"
                          "\"cents\":%.2f,\"amp\":%.5f,\"similarity\":%.3f",
                          e.candidate_index, e.u.candidate.cand_note_id,
                          double(e.u.candidate.midi), double(e.u.candidate.presence),
                          double(e.u.candidate.cents), double(e.u.candidate.amplitude),
                          double(e.u.candidate.similarity));
        }
        break;
    case NoteKind::Expression:
        std::snprintf(body, sizeof(body),
                      ",\"dyn_db\":%.2f,\"attack_ms\":%.1f,\"release_ms\":%.1f,"
                      "\"sustain\":%.3f,\"vib_hz\":%.2f,\"vib_depth_c\":%.1f",
                      double(e.u.expression.dynamics_db), double(e.u.expression.attack_ms),
                      double(e.u.expression.release_ms), double(e.u.expression.sustain_ratio),
                      double(e.u.expression.vib_rate_hz), double(e.u.expression.vib_depth_c));
        break;
    case NoteKind::Close:
        std::snprintf(body, sizeof(body),
                      ",\"end_ms\":%.2f,\"dur_ms\":%.2f,\"peak_rms\":%.5f",
                      double(e.u.close.end_ms), double(e.u.close.duration_ms),
                      double(e.u.close.peak_rms));
        break;
    }

    const int n = std::snprintf(buf, cap, "%s%s,\"flags\":%s}\n", head, body, flags);
    if (n <= 0 || size_t(n) >= cap) return 0;
    return size_t(n);
}

// ---------------------------------------------------------------------------
// Blind content classification (no candidate input).

/// What the audio alone says about an attack: how many distinct harmonic
/// series sound (octave doublings merge — blind physics), and how much of
/// the series energy the strongest one carries.
struct BlindContent {
    ContentClass klass = ContentClass::Mono;
    float capture = 0.0f;       ///< best-series energy share over accepted series
    float dominant_midi = 0.0f; ///< strongest series fundamental (nearest midi)
    float n_series = 1.0f;      ///< accepted series count ("N unresolved")
};

namespace detail {

/// Goertzel magnitude at one frequency, scaled ~ sinusoid amplitude.
inline double goertzel_mag(const float* x, size_t n, float sr, float f) noexcept {
    const double w = 2.0 * M_PI * double(f) / double(sr);
    const double coeff = 2.0 * std::cos(w);
    double s1 = 0.0, s2 = 0.0;
    for (size_t i = 0; i < n; ++i) {
        const double s0 = double(x[i]) + coeff * s1 - s2;
        s2 = s1;
        s1 = s0;
    }
    const double re = s1 - s2 * std::cos(w);
    const double im = s2 * std::sin(w);
    return 2.0 * std::sqrt(re * re + im * im) / double(n);
}

inline float midi_to_hz(float midi) noexcept {
    return 440.0f * std::pow(2.0f, (midi - 69.0f) / 12.0f);
}

/// Harmonic-series energy of a fundamental: sum |harmonic|² / h² over the
/// first six harmonics (fixed grid math, no allocation).
inline double series_energy(const float* x, size_t n, float sr, float f0,
                            float* f0_mag = nullptr) noexcept {
    double e = 0.0;
    for (int h = 1; h <= 6; ++h) {
        const float f = f0 * float(h);
        if (f > 0.45f * sr) break;
        const double m = goertzel_mag(x, n, sr, f);
        if (h == 1 && f0_mag != nullptr) *f0_mag = float(m);
        e += (m / double(h)) * (m / double(h));
    }
    return e;
}

} // namespace detail

/// Blind mono-vs-polyphonic description of an analysis window. Counts
/// distinct harmonic series on a fixed midi grid (coarse + fine refine),
/// rejecting grid points that are harmonics of an already-accepted series
/// (their energy is the accepted note's) and points without their own f0
/// line. A second series only counts when it carries `profile.
/// mono_poly_threshold` of the best series' energy. Wind/bowed profiles
/// (max_simultaneous_notes == 1) skip the search entirely — one note at a
/// time is instrument physics, not an audio guess. Allocation-free.
inline BlindContent classify_content(const float* window, size_t count,
                                     float sample_rate,
                                     const InstrumentProfile& profile) noexcept {
    BlindContent out;
    if (count < 256) return out;

    // Best single series: coarse 0.5-semitone grid, then a fine refine.
    double best_e = -1.0;
    float best_midi = 60.0f;
    for (float m = 28.0f; m <= 92.0f; m += 0.5f) {
        const double e = detail::series_energy(window, count, sample_rate,
                                               detail::midi_to_hz(m));
        if (e > best_e) { best_e = e; best_midi = m; }
    }
    for (float m = best_midi - 0.5f; m <= best_midi + 0.5f; m += 0.05f) {
        const double e = detail::series_energy(window, count, sample_rate,
                                               detail::midi_to_hz(m));
        if (e > best_e) { best_e = e; best_midi = m; }
    }
    out.dominant_midi = std::round(best_midi);
    if (best_e <= 0.0) return out;

    float best_f0_mag = 0.0f;
    (void)detail::series_energy(window, count, sample_rate, detail::midi_to_hz(best_midi),
                                &best_f0_mag);

    if (profile.max_simultaneous_notes <= 1) {
        out.klass = ContentClass::Mono;
        out.capture = 1.0f;
        out.n_series = 1.0f;
        return out;
    }

    // Accept further series: descending energy, non-harmonic-derivative of
    // the accepted ones, with their own fundamental line.
    float accepted[8] = {best_midi};
    size_t n_acc = 1;
    double acc_energy = best_e;
    const float ratio = profile.mono_poly_threshold;
    for (int pass = 0; pass < 7; ++pass) {
        double pick_e = 0.0;
        float pick_midi = 0.0f, pick_f0_mag = 0.0f;
        for (float m = 28.0f; m <= 92.0f; m += 0.5f) {
            bool derivative = false;
            for (size_t a = 0; a < n_acc; ++a) {
                const float r = detail::midi_to_hz(m) / detail::midi_to_hz(accepted[a]);
                const float k = std::round(r);
                if (k >= 1.0f && k <= 8.0f && std::fabs(r - k) < 0.02f) {
                    derivative = true; // harmonic (or unison/octave) of an accepted series
                    break;
                }
            }
            if (derivative) continue;
            float f0_mag = 0.0f;
            const double e = detail::series_energy(window, count, sample_rate,
                                                   detail::midi_to_hz(m), &f0_mag);
            if (e > pick_e) { pick_e = e; pick_midi = m; pick_f0_mag = f0_mag; }
        }
        if (pick_midi == 0.0f || pick_e < double(ratio) * best_e
            || pick_f0_mag < 0.12 * double(best_f0_mag)) {
            break;
        }
        accepted[n_acc++] = pick_midi;
        acc_energy += pick_e;
    }

    out.n_series = float(n_acc);
    out.capture = float(best_e / (acc_energy > 0.0 ? acc_energy : 1.0));
    out.klass = (n_acc >= 2) ? ContentClass::Polyphonic : ContentClass::Mono;
    return out;
}

// ---------------------------------------------------------------------------
// Event stream: lock-free SPSC per consumer, drop-on-full with a visible
// counter (a display may lose events, a JSONL log may not — and neither may
// lose them silently).

class NoteEventStream {
public:
    /// Allocates the buffer here (setup path). Capacity is rounded up to a
    /// power of two.
    explicit NoteEventStream(size_t capacity = 1024) {
        size_t cap = 1;
        while (cap < capacity) cap <<= 1;
        buffer_.resize(cap);
        mask_ = cap - 1;
    }

    /// Producer side. False when full (event dropped; see dropped()).
    bool push(const NoteEvent& e) noexcept {
        const size_t w = write_pos_.load(std::memory_order_relaxed);
        const size_t r = read_pos_.load(std::memory_order_acquire);
        if (buffer_.size() - (w - r) < 1) {
            dropped_.fetch_add(1, std::memory_order_relaxed);
            return false;
        }
        buffer_[w & mask_] = e;
        write_pos_.store(w + 1, std::memory_order_release);
        return true;
    }

    /// Consumer side: pops up to `max` events, returns how many.
    size_t pop(NoteEvent* out, size_t max) noexcept {
        const size_t r = read_pos_.load(std::memory_order_relaxed);
        const size_t w = write_pos_.load(std::memory_order_acquire);
        size_t n = w - r;
        if (n > max) n = max;
        for (size_t i = 0; i < n; ++i) out[i] = buffer_[(r + i) & mask_];
        read_pos_.store(r + n, std::memory_order_release);
        return n;
    }

    size_t available() const noexcept {
        return write_pos_.load(std::memory_order_acquire)
             - read_pos_.load(std::memory_order_acquire);
    }

    /// Events lost to a full stream since construction (never silent).
    uint32_t dropped() const noexcept { return dropped_.load(std::memory_order_relaxed); }

private:
    std::vector<NoteEvent> buffer_;
    size_t mask_ = 0;
    std::atomic<size_t> write_pos_{0};
    std::atomic<size_t> read_pos_{0};
    std::atomic<uint32_t> dropped_{0};
};

// ---------------------------------------------------------------------------
// The describer: per-note state machine + scheduled measurement.

class NoteDescriber {
public:
    static constexpr size_t kMaxCandidates = 10;   // hard cap (profile caps below)
    static constexpr size_t kMaxOpenNotes = 32;
    static constexpr size_t kMaxSubscribers = 4;
    static constexpr size_t kProvisionalWindow = 2048;
    static constexpr size_t kFinalWindow = 8192;   // 170 ms at 48 kHz
    static constexpr size_t kReadingWindow = 2048;
    static constexpr size_t kHistory = 32768;      // power of two; must cover
                                                   // delayed finalize (0.68 s)
    static constexpr size_t kRmsSeriesMax = 512;

    struct CandidateEntry {
        uint32_t cand_note_id = 0; ///< record the hypothesis belongs to (0 = anonymous)
        float midi = 0.0f;         ///< hypothesis, equal-tempered
    };

    struct CandidateSet {
        uint32_t note_id = 0;
        uint8_t count = 0;
        CandidateEntry entries[kMaxCandidates] = {};
    };

    /// Setup path: allocates all scratch once. `profile` supplies the
    /// instrument caps (max_simultaneous_notes, mono_poly_threshold).
    NoteDescriber(float sample_rate, const InstrumentProfile& profile)
        : sr_(sample_rate), profile_(profile), detector_(sample_rate),
          validator_(sample_rate, kFinalWindow) {
        detector_.set_profile(profile_);
        history_.assign(kHistory, 0.0f);
        pitch_scratch_.assign(kFinalWindow, 1.0f);
        bucket_samples_ = size_t(0.01f * sample_rate + 0.5f);
        if (bucket_samples_ == 0) bucket_samples_ = 1;
    }

    /// Fan-out subscription (fixed slot array; setup path).
    bool subscribe(NoteEventStream& stream) noexcept {
        for (size_t i = 0; i < kMaxSubscribers; ++i) {
            if (subscribers_[i] == &stream) return true;
            if (subscribers_[i] == nullptr) {
                subscribers_[i] = &stream;
                return true;
            }
        }
        return false;
    }

    /// Caller-supplied hypotheses for one attack (fixed-slot copy, no
    /// allocation). Applies to the named note's finalize measurement; call
    /// any time after the HIT and before that note finalizes. `note_id == 0`
    /// prepares the set for the next note to open.
    bool set_candidates(const CandidateSet& set) noexcept {
        if (set.count > kMaxCandidates) return false;
        if (set.note_id == 0) {
            next_cands_ = set;
            have_next_cands_ = true;
            return true;
        }
        for (auto& n : notes_) {
            if (n.live && n.id == set.note_id && !n.finalized) {
                n.cands = set;
                n.have_cands = true;
                return true;
            }
        }
        return false;
    }

    /// ANALYSIS FAST PATH (consumer thread, allocation-free): one block of
    /// stage-0-conditioned audio in, zero or more events out (HIT, PITCH
    /// provisional). Final measurements come from finalize_due().
    void process_block(const float* samples, size_t n) noexcept {
        double acc = 0.0;
        for (size_t i = 0; i < n; ++i) acc += double(samples[i]) * double(samples[i]);
        block_strength_ = n > 0 ? float(std::sqrt(acc / double(n))) : 0.0f;
        if (detector_.process_onset_block(samples, n)) {
            open_note(sample_pos_ + n); // block-quantized, like kan_analyze
        }
        const size_t mask = history_.size() - 1;
        for (size_t off = 0; off < n; ++off) {
            history_[hist_pos_] = samples[off];
            hist_pos_ = (hist_pos_ + 1) & mask;
            if (hist_fill_ < history_.size()) ++hist_fill_;
            const double s = double(samples[off]);
            bucket_acc_ += s * s;
            if (++bucket_n_ >= bucket_samples_) {
                close_bucket();
            }
            ++sample_pos_;
        }
    }

    /// SCHEDULED WORK: performs at most one pending finalize (long-window
    /// measurement) per call, in note order. Returns true when one was
    /// performed — call repeatedly (a worker thread may own this loop).
    /// `process_block` never blocks on it.
    bool finalize_due() noexcept {
        NoteSlot* pick = nullptr;
        for (auto& n : notes_) {
            if (!n.live || n.finalized || !n.final_due) continue;
            if (pick == nullptr || n.id < pick->id) pick = &n;
        }
        if (pick == nullptr) return false;
        finalize_note(*pick);
        return true;
    }

    /// End of stream: close every open note (finalizes + CLOSE facts).
    /// Deterministic; may perform all pending work.
    void flush() noexcept {
        while (finalize_due()) {}
        for (auto& n : notes_) {
            if (n.live) close_note(n, last_bucket_ms_);
        }
    }

    /// Id of the most recently opened note (0 = none yet).
    uint32_t last_note_id() const noexcept { return last_note_id_; }

private:
    struct NoteSlot {
        uint32_t id = 0;
        bool live = false;
        bool provisional_sent = false;
        bool finalized = false;
        bool final_due = false;
        bool have_cands = false;
        uint64_t onset_sample = 0;
        uint64_t close_sample = 0;
        float onset_ms = 0.0f;
        float peak_rms = 0.0f;
        float quiet_ms = 0.0f;
        float last_bucket_ms = 0.0f;
        // blind reading bookkeeping (stable estimate = mean deviation from a
        // fixed per-note reference; never wraps).
        float ref_midi = 0.0f;
        bool have_ref = false;
        float dev_sum = 0.0f;
        float prev_dev = 0.0f;
        float max_dev = 0.0f;
        size_t readings = 0;
        size_t valid_readings = 0;
        size_t hops = 0;
        size_t next_reading_ms = 50;
        // long-window blind estimate at finalize time
        bool final_est_valid = false;
        float final_midi = 0.0f;
        float final_cents = 0.0f;
        TrajectoryBuffer traj;
        // envelope series (10 ms hop, fixed capacity)
        float rms_series[kRmsSeriesMax] = {0};
        size_t rms_count = 0;
        CandidateSet cands = {};
    };

    // ---- emission ---------------------------------------------------------
    void emit(const NoteEvent& e) noexcept {
        for (size_t i = 0; i < kMaxSubscribers; ++i) {
            if (subscribers_[i] != nullptr) subscribers_[i]->push(e);
        }
    }

    /// Opens a note record on the attack and emits its HIT (fixed forever).
    void open_note(uint64_t onset_sample) noexcept {
        NoteSlot* slot = nullptr;
        for (auto& n : notes_) {
            if (!n.live) { slot = &n; break; }
        }
        if (slot == nullptr) {
            // Table full: close the oldest open note to make room.
            for (auto& n : notes_) {
                if (n.live && (slot == nullptr || n.id < slot->id)) slot = &n;
            }
            if (slot != nullptr) {
                close_note(*slot, 1000.0f * float(onset_sample) / sr_);
            }
        }
        if (slot == nullptr) return;
        *slot = NoteSlot{};
        slot->id = next_note_id_++;
        slot->live = true;
        slot->onset_sample = onset_sample;
        slot->onset_ms = 1000.0f * float(onset_sample) / sr_;
        last_note_id_ = slot->id;
        if (have_next_cands_) {
            slot->cands = next_cands_;
            slot->have_cands = true;
            have_next_cands_ = false;
        }
        emit(note_event::make_hit(slot->id, slot->onset_ms, slot->onset_ms, block_strength_));
    }

    // ---- per-bucket bookkeeping (10 ms grid, block-size independent) ------
    void close_bucket() noexcept {
        const float rms = float(std::sqrt(bucket_acc_ / double(bucket_n_)));
        const float t_ms = 1000.0f * float(sample_pos_) / sr_;
        last_bucket_ms_ = t_ms;
        bucket_acc_ = 0.0;
        bucket_n_ = 0;

        for (auto& n : notes_) {
            if (!n.live) continue;
            if (rms > n.peak_rms) n.peak_rms = rms;
            if (n.rms_count < kRmsSeriesMax) n.rms_series[n.rms_count++] = rms;
            n.last_bucket_ms = t_ms;

            // Blind readings every 50 ms after blanking (stability, hops,
            // vibrato trajectory) — cheap, deterministic on the bucket grid.
            const float since = t_ms - n.onset_ms;
            if (since >= profile_.blanking_ms && since + 0.5f >= float(n.next_reading_ms)) {
                take_reading(n, t_ms);
                n.next_reading_ms += 50;
            }

            // Provisional fact as soon as its window is complete.
            if (!n.provisional_sent && since >= profile_.blanking_ms
                + 1000.0f * float(kProvisionalWindow) / sr_) {
                emit_provisional(n, t_ms);
            }

            // Decay-relative end: 20 dB below the note's own peak, sustained.
            const bool quiet = rms <= 0.1f * n.peak_rms;
            n.quiet_ms = quiet ? n.quiet_ms + 1000.0f * float(bucket_samples_) / sr_ : 0.0f;
            if (n.quiet_ms >= 80.0f) {
                close_note(n, t_ms - n.quiet_ms);
            }
        }

        // Final measurements become due with the long window (or at close).
        for (auto& n : notes_) {
            if (n.live && !n.finalized && !n.final_due) {
                const float since = t_ms - n.onset_ms;
                if (since >= profile_.blanking_ms
                    + 1000.0f * float(kFinalWindow) / sr_) {
                    n.final_due = true;
                }
            }
        }
    }

    // ---- measurement steps ----------------------------------------------
    void take_reading(NoteSlot& n, float t_ms) noexcept {
        float window[kReadingWindow];
        if (!copy_history_tail(window, kReadingWindow)) return;
        const PitchEstimate est =
            detector_.detect_pitch_into(window, kReadingWindow,
                                        pitch_scratch_.data(), pitch_scratch_.size());
        ++n.readings;
        if (!est.valid) return;
        ++n.valid_readings;
        if (!n.have_ref) {
            n.have_ref = true;
            n.ref_midi = est.midi;
        }
        const float rel = 100.0f * (est.midi - n.ref_midi) + est.cents_offset;
        if (n.readings > 1 && std::fabs(rel - n.prev_dev) > 200.0f) ++n.hops;
        n.prev_dev = rel;
        n.dev_sum += rel;
        n.max_dev = std::max(n.max_dev, std::fabs(rel));
        n.traj.add(t_ms, rel);
    }

    void emit_provisional(NoteSlot& n, float t_ms) noexcept {
        n.provisional_sent = true;
        float window[kProvisionalWindow];
        if (!copy_history_tail(window, kProvisionalWindow)) {
            emit(note_event::make_pitch_unresolved(n.id, t_ms, PitchStage::Provisional,
                                                   kFlagMixed));
            return;
        }
        const PitchEstimate est =
            detector_.detect_pitch_into(window, kProvisionalWindow,
                                        pitch_scratch_.data(), pitch_scratch_.size());
        if (!est.valid) {
            // Display-only fact: no estimate yet, honestly labeled.
            emit(note_event::make_pitch_unresolved(n.id, t_ms, PitchStage::Provisional,
                                                   kFlagMixed));
            return;
        }
        emit(note_event::make_pitch(n.id, t_ms, PitchStage::Provisional,
                                    est.midi, est.cents_offset, kFlagNone));
    }

    void finalize_note(NoteSlot& n) noexcept {
        n.finalized = true;
        n.final_due = false;

        // The finalize window is fixed by the note's own timeline (first
        // kFinalWindow clean samples after blanking, truncated at the note's
        // end) — never by when finalize_due() happened to run. The event
        // time is that evidence window's end.
        const uint64_t start = n.onset_sample + uint64_t(profile_.blanking_ms / 1000.0f * sr_);
        const uint64_t end_limit = n.close_sample > 0 ? n.close_sample : sample_pos_;
        uint64_t end = start + kFinalWindow;
        if (end > end_limit) end = end_limit;
        const size_t len = end > start ? size_t(end - start) : 0;
        const float t_ms = 1000.0f * float(end) / sr_;

        float window[kFinalWindow];
        const bool have_window = len >= 256 && copy_history_range(start, len, window);

        // Blind classification (candidate-free) — the CONTENT class fact.
        BlindContent blind;
        if (have_window) {
            blind = classify_content(window, len, sr_, profile_);
        }
        const uint32_t poly_flag =
            (blind.klass == ContentClass::Polyphonic) ? kFlagPolyphonic : kFlagNone;
        emit(note_event::make_content_class(
            n.id, t_ms, blind.klass, blind.n_series, blind.capture, poly_flag));

        // Conditioned measurement (caller-supplied candidates).
        bool have_final_estimate = false;
        float final_midi = 0.0f, final_cents = 0.0f;
        uint32_t final_flags = poly_flag;
        const size_t cap = size_t(profile_.max_simultaneous_notes);
        if (n.have_cands && n.cands.count > 0 && have_window && cap > 0) {
            const size_t n_cand = std::min<size_t>(n.cands.count, std::min(cap, kMaxCandidates));
            float midis[kMaxCandidates] = {0};
            for (size_t k = 0; k < n_cand; ++k) midis[k] = n.cands.entries[k].midi;
            if (validator_.set_expected_chord(midis, n_cand)) {
                const ChordResult r = validator_.validate(window, len);
                const uint32_t sim_flag =
                    (r.similarity < 0.80f) ? kFlagUnexplainedEnergy : kFlagNone;
                size_t best_k = 0, second_k = 0;
                for (size_t k = 1; k < n_cand; ++k) {
                    if (r.note_presence[k] > r.note_presence[best_k]) {
                        second_k = best_k;
                        best_k = k;
                    } else if (k != best_k && r.note_presence[k] > r.note_presence[second_k]) {
                        second_k = k;
                    }
                }
                for (size_t k = 0; k < n_cand; ++k) {
                    uint32_t f = kFlagNone | sim_flag;
                    const float refined = validator_.refined_cents(k);
                    if (std::fabs(refined) >= 29.0f) f |= kFlagSearchSaturated;
                    emit(note_event::make_content_candidate(
                        n.id, t_ms, uint8_t(k), n.cands.entries[k].cand_note_id,
                        midis[k], r.note_presence[k], refined, validator_.note_rms(k),
                        r.similarity, f));
                }
                if (r.similarity >= 0.80f) {
                    have_final_estimate = true;
                    final_midi = midis[best_k];
                    final_cents = validator_.refined_cents(best_k);
                    const bool dominant = n_cand == 1
                        || r.note_presence[best_k] >= 3.0f * r.note_presence[second_k];
                    if (dominant) final_flags |= kFlagDominant;
                    else if (blind.klass == ContentClass::Polyphonic) {
                        // Chord with no clear leader: honest "unresolved".
                        have_final_estimate = false;
                    }
                } else {
                    final_flags |= kFlagUnexplainedEnergy;
                }
            }
        }

        if (!have_final_estimate) {
            // Blind fallback: one consistent estimate only when the material
            // is monophonic and the tracker was stable; otherwise the honest
            // "unresolved" (never a number the tool distrusts).
            if (!n.have_cands && blind.klass == ContentClass::Mono && have_window) {
                const PitchEstimate est = detector_.detect_pitch_into(
                    window, len, pitch_scratch_.data(), pitch_scratch_.size());
                const bool stable = n.readings > 1
                    && float(n.hops) / float(n.readings - 1) <= 0.2f && n.max_dev <= 200.0f;
                if (est.valid && stable) {
                    have_final_estimate = true;
                    final_midi = est.midi;
                    final_cents = est.cents_offset;
                } else if (stable && n.have_ref && n.valid_readings >= 3) {
                    // One consistent estimate from the stable readings: the
                    // fixed per-note reference + mean deviation (the Phase-0
                    // estimator). Used when the long window itself is
                    // pitch-ambiguous (e.g. an early estimate that later
                    // corrected — the window spans both).
                    have_final_estimate = true;
                    final_midi = n.ref_midi;
                    final_cents = n.dev_sum / float(n.valid_readings);
                }
            }
            if (!have_final_estimate) {
                uint32_t f = final_flags;
                if (n.readings >= 2 && float(n.valid_readings) / float(n.readings) < 0.5f) {
                    f |= kFlagMixed;
                }
                if (n.readings > 1
                    && float(n.hops) / float(n.readings - 1) > 0.2f) {
                    f |= kFlagHop;
                }
                emit(note_event::make_pitch_unresolved(n.id, t_ms, PitchStage::Unresolved, f));
                return;
            }
        }

        if (final_midi < 48.0f && len < kFinalWindow) final_flags |= kFlagLowRegister;
        emit(note_event::make_pitch(n.id, t_ms, PitchStage::Final,
                                    final_midi, final_cents, final_flags));
    }

    void close_note(NoteSlot& n, float end_ms) noexcept {
        if (!n.live) return;
        n.live = false;
        n.close_sample = uint64_t(double(end_ms) * double(sr_) / 1000.0);
        if (!n.finalized) {
            n.final_due = true;
            finalize_note(n);
        }

        // EXPRESSION: envelope + vibrato (measurements only; dynamics tiers
        // are Phase 3 — the raw level in dBFS is reported now).
        const float hop_ms = 1000.0f * float(bucket_samples_) / sr_;
        EnvelopeAnalyzer env;
        const EnvelopeMeasurement m =
            n.rms_count >= 4 ? env.measure(n.rms_series, n.rms_count, hop_ms)
                             : EnvelopeMeasurement{};
        VibratoAnalyzer vib;
        const VibratoMeasurement v = vib.measure(n.traj);
        const float dyn_db = (m.peak > 0.0f) ? 20.0f * std::log10(m.peak) : -120.0f;
        emit(note_event::make_expression(n.id, end_ms, dyn_db, m.attack_ms, m.release_ms,
                                         m.sustain_ratio, v.rate_hz, v.depth_cents));
        emit(note_event::make_close(n.id, end_ms, end_ms,
                                    end_ms - n.onset_ms, m.peak));
    }

    // ---- history access ---------------------------------------------------
    bool copy_history_tail(float* out, size_t len) noexcept {
        if (len > hist_fill_ || len > history_.size()) return false;
        const size_t mask = history_.size() - 1;
        size_t pos = (hist_pos_ + history_.size() - len) & mask;
        for (size_t i = 0; i < len; ++i) out[i] = history_[(pos + i) & mask];
        return true;
    }

    bool copy_history_range(uint64_t start_sample, size_t len, float* out) noexcept {
        if (len > hist_fill_ || len > history_.size()) return false;
        const uint64_t oldest = (sample_pos_ > hist_fill_) ? (sample_pos_ - hist_fill_) : 0;
        if (start_sample < oldest) return false; // stale (finalize too delayed)
        const size_t mask = history_.size() - 1;
        size_t pos = (hist_pos_ + history_.size() - size_t(sample_pos_ - start_sample)) & mask;
        for (size_t i = 0; i < len; ++i) out[i] = history_[(pos + i) & mask];
        return true;
    }

    float sr_;
    InstrumentProfile profile_;
    HybridDetector detector_;
    HarmonicChordValidator validator_;

    NoteSlot notes_[kMaxOpenNotes];
    NoteEventStream* subscribers_[kMaxSubscribers] = {nullptr, nullptr, nullptr, nullptr};
    CandidateSet next_cands_ = {};
    bool have_next_cands_ = false;
    uint32_t next_note_id_ = 1;
    uint32_t last_note_id_ = 0;

    std::vector<float> history_;
    std::vector<float> pitch_scratch_;
    size_t hist_pos_ = 0;
    size_t hist_fill_ = 0;
    uint64_t sample_pos_ = 0;

    size_t bucket_samples_ = 480;
    double bucket_acc_ = 0.0;
    size_t bucket_n_ = 0;
    float last_bucket_ms_ = 0.0f;
    float block_strength_ = 0.0f;
};

} // namespace kan
