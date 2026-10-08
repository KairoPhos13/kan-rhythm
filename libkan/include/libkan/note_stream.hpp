#pragma once
// Per-note description stream ("register by note") — libkan describes what
// was played; it never judges (docs/architecture/note-description-stream.md).
//
// RECORD MODEL (refined by review): a record is a STRUCK NOTE — an attack
// execution times the notes it strikes. One attack with three notes struck
// opens three records, each with its own lifetime (a note may sustain
// longer than its neighbors, repeat, or be cut earlier). Instruments whose
// notes start WITHOUT strikes (wind/bowed slurs, transition notes) also
// open records on sustained pitch changes (profile data decides — libkan
// contains no instrument names). Facts:
//   HIT        when a record opens: onset/transition time + strength, plus
//              its strike_group (records struck together share it). Fixed
//              at the moment it happens, never revised.
//   PITCH      provisional estimate first, then one final estimate (or
//              "unresolved"). The final is the AMPLITUDE-WEIGHTED CENTER
//              of the note's trajectory — the center of a vibrato, not its
//              initial value. One consistent estimate per event (midi +
//              cents; Hz derived from the pair).
//   CONTENT    mono vs polyphonic attack (blind, candidate-free); when the
//              caller supplies candidate pitches, one fact per candidate:
//              presence, cents, amplitude, with the explained energy of
//              the set. Candidate facts live under the record the
//              candidate belongs to — a note sustaining under later
//              attacks accumulates facts in its own register.
//   EXPRESSION dynamics tier + level, envelope, vibrato (claimed only from
//              stable trajectories).
//   CLOSE      the note's OWN end (per-note amplitude trajectory, -20 dB
//              rule) + summary.
//
// Honesty rules (the fairness rule): the stream never emits a number the
// tool distrusts. Flags describe audio-true states only
// (MIX/HOP/POLYPHONIC/DOMINANT/UNEXPLAINED_ENERGY/LOW_REGISTER/
// SEARCH_SATURATED/TRANSITION). Unmeasurable means `unresolved` — "not
// measured", never "wrong".
//
// Threading/RT: the audio callback is untouched. `process_block` is the
// analysis fast path (allocation-free); `finalize_due()` performs at most
// one scheduled measurement per call (probe fit, long-window fit, or
// sustain fit — the expensive part; a worker thread may own the loop).
// All scratch is allocated in constructors. The event stream is lock-free
// SPSC per consumer. Everything instrument-specific is `InstrumentProfile`
// pack data: add a flute or a melodica by adding a profile, never code.
//
// Determinism: the stream is a pure function of (sample stream at a given
// block size, candidate sequence). All times derive from sample positions.
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstddef>
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
    kFlagTransition = 1u << 7,        ///< record opened by a legato pitch change,
                                      /// not by a strike
    kFlagRestrike = 1u << 8,          ///< the same pitch was struck AGAIN (level
                                      /// jump vs the sounding record) — not a
                                      /// mere extension of the previous strike
};

/// Fixed-size, trivially copyable per-note fact.
struct NoteEvent {
    uint32_t note_id = 0;         ///< record this fact describes (monotonic)
    uint32_t flags = 0;           ///< NoteFlags
    float t_ms = 0.0f;            ///< stream time of the fact (evidence window end)
    NoteKind kind = NoteKind::Hit;
    PitchStage stage = PitchStage::Provisional;
    ContentClass klass = ContentClass::Unknown;
    uint8_t candidate_index = 0;

    union Payload {
        struct { float onset_ms; float strength; float midi; uint32_t strike_group; } hit;
        struct { float midi; float cents; float hz; } pitch;
        struct { float midi; float presence; float cents; float amplitude;
                 float similarity; uint32_t strike_group; } candidate;
        struct { float n_unresolved; float dominant_share; } content;
        struct { float dynamics_db; float dynamics_tier; float attack_ms;
                 float release_ms; float sustain_ratio; float vib_rate_hz;
                 float vib_depth_c; float bend_c; float bend_err_c; } expression;
        struct { float end_ms; float duration_ms; float peak_rms; } close;
    } u;
};

static_assert(std::is_trivially_copyable<NoteEvent>::value,
              "NoteEvent must stay POD for the lock-free stream");

namespace note_event {

/// HIT — fixed at the moment the record opens (strike or transition);
/// never revised. `strike_group` ties records struck by the same attack.
inline NoteEvent make_hit(uint32_t note_id, uint32_t strike_group, float t_ms,
                          float onset_ms, float strength, float hypothesis_midi,
                          uint32_t flags) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.flags = flags;
    e.kind = NoteKind::Hit;
    e.u.hit.onset_ms = onset_ms;
    e.u.hit.strength = strength;
    e.u.hit.midi = hypothesis_midi; ///< the record's identity (0 = blind)
    e.u.hit.strike_group = strike_group;
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

/// CONTENT class — blind mono-vs-polyphonic statement of an attack
/// (`note_id` = the strike group's leader record).
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
/// `note_id` = the record the candidate belongs to (a sustaining note
/// accumulates facts in its own register); `strike_group` = the attack
/// whose window produced the measurement.
inline NoteEvent make_content_candidate(uint32_t note_id, uint32_t strike_group,
                                        float t_ms, uint8_t candidate_index,
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
    e.u.candidate.strike_group = strike_group;
    return e;
}

/// EXPRESSION — how the note was played (measurements only).
inline NoteEvent make_expression(uint32_t note_id, float t_ms, float dynamics_db,
                                 float dynamics_tier, float attack_ms, float release_ms,
                                 float sustain_ratio, float vib_rate_hz,
                                 float vib_depth_c, float bend_c = 0.0f,
                                 float bend_err_c = 0.0f) noexcept {
    NoteEvent e{};
    e.note_id = note_id;
    e.t_ms = t_ms;
    e.kind = NoteKind::Expression;
    e.u.expression.dynamics_db = dynamics_db;
    e.u.expression.dynamics_tier = dynamics_tier;
    e.u.expression.attack_ms = attack_ms;
    e.u.expression.release_ms = release_ms;
    e.u.expression.sustain_ratio = sustain_ratio;
    e.u.expression.vib_rate_hz = vib_rate_hz;
    e.u.expression.bend_c = bend_c;
    e.u.expression.bend_err_c = bend_err_c;
    e.u.expression.vib_depth_c = vib_depth_c;
    return e;
}

/// CLOSE — the note's own end; never revises earlier facts.
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
// JSONL serialization (schema v2). Deterministic key order so live and
// offline logs diff cleanly.

/// Writes one JSON line (including the trailing newline). Returns the number
/// of bytes written, or 0 if `cap` is too small. No allocation.
inline size_t note_event_jsonl(const NoteEvent& e, char* buf, size_t cap) noexcept {
    static const char* kFlagNames[9] = {"MIX", "HOP", "POLYPHONIC", "DOMINANT",
                                        "UNEXPLAINED_ENERGY", "LOW_REGISTER",
                                        "SEARCH_SATURATED", "TRANSITION",
                                        "RESTRIKE"};
    char flags[160];
    size_t fp = 0;
    flags[fp++] = '[';
    bool first = true;
    for (int i = 0; i < 9; ++i) {
        if ((e.flags & (1u << i)) == 0) continue;
        if (!first) flags[fp++] = ',';
        first = false;
        fp += size_t(std::snprintf(flags + fp, sizeof(flags) - fp, "\"%s\"", kFlagNames[i]));
        if (fp >= sizeof(flags) - 2) break;
    }
    flags[fp++] = ']';
    flags[fp] = '\0';

    char head[192];
    std::snprintf(head, sizeof(head), "{\"v\":3,\"note\":%u,\"kind\":\"%s\",\"t_ms\":%.2f",
                  e.note_id,
                  e.kind == NoteKind::Hit ? "HIT"
                      : e.kind == NoteKind::Pitch ? "PITCH"
                      : e.kind == NoteKind::Content ? "CONTENT"
                      : e.kind == NoteKind::Expression ? "EXPRESSION" : "CLOSE",
                  double(e.t_ms));

    char body[384];
    char hit_midi[16];
    if (e.u.hit.midi > 0.0f) {
        std::snprintf(hit_midi, sizeof(hit_midi), "%.0f", double(e.u.hit.midi));
    } else {
        std::snprintf(hit_midi, sizeof(hit_midi), "%s", "null");
    }
    const char* midi_cell = hit_midi;
    switch (e.kind) {
    case NoteKind::Hit:
        std::snprintf(body, sizeof(body),
                      ",\"onset_ms\":%.2f,\"strength\":%.4f,\"midi\":%s,"
                      "\"strike\":%u",
                      double(e.u.hit.onset_ms), double(e.u.hit.strength),
                      e.u.hit.midi > 0.0f ? midi_cell : "null",
                      e.u.hit.strike_group);
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
                          ",\"cand\":%u,\"midi\":%.0f,\"presence\":%.3f,"
                          "\"cents\":%.2f,\"amp\":%.5f,\"similarity\":%.3f,\"strike\":%u",
                          e.candidate_index, double(e.u.candidate.midi),
                          double(e.u.candidate.presence), double(e.u.candidate.cents),
                          double(e.u.candidate.amplitude), double(e.u.candidate.similarity),
                          e.u.candidate.strike_group);
        }
        break;
    case NoteKind::Expression:
        std::snprintf(body, sizeof(body),
                      ",\"dyn_db\":%.2f,\"dyn_tier\":\"%s\",\"attack_ms\":%.1f,"
                      "\"release_ms\":%.1f,\"sustain\":%.3f,\"vib_hz\":%.2f,"
                      "\"vib_depth_c\":%.1f,\"bend_c\":%.1f,\"bend_err_c\":%.1f",
                      double(e.u.expression.dynamics_db),
                      dynamics_tier_name(DynamicsTier(int(e.u.expression.dynamics_tier))),
                      double(e.u.expression.attack_ms), double(e.u.expression.release_ms),
                      double(e.u.expression.sustain_ratio),
                      double(e.u.expression.vib_rate_hz), double(e.u.expression.vib_depth_c),
                      double(e.u.expression.bend_c), double(e.u.expression.bend_err_c));
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
/// mono_poly_threshold` of the best series' energy. The class fact is
/// always AUDIO-DERIVED: a "mono" instrument profile caps the candidate
/// set, but never claims mono over polyphonic audio (a companion in the
/// recording must show up as polyphonic). Allocation-free.
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
    /// Allocates the buffer here (setup path). Capacity rounds up to a
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
// The describer: per-struck-note records with their own lifetimes.
//
// One attack execution (a "strike group") opens one record per STRUCK note;
// candidates that were already sounding keep their own earlier records and
// accumulate facts there. Per-note ends come from the amplitude trajectory
// of conditioned joint fits (-20 dB rule); the final pitch is the
// amplitude-weighted CENTER of that trajectory (the center of a vibrato,
// not its initial value). Instrument behavior is profile data only.

class NoteDescriber {
public:
    static constexpr size_t kMaxCandidates = 10;   // hard cap (profile caps below)
    static constexpr size_t kMaxRecords = 64;
    static constexpr size_t kMaxGroups = 16;
    static constexpr size_t kMaxSubscribers = 4;
    static constexpr size_t kProvisionalWindow = 2048;
    static constexpr size_t kFinalWindow = 8192;   // 170 ms at 48 kHz
    static constexpr size_t kReadingWindow = 2048;
    static constexpr size_t kSustainWindow = 4096; // 85 ms: separates neighbors
    static constexpr size_t kHistory = 32768;      // power of two (0.68 s)
    static constexpr size_t kRmsSeriesMax = 512;
    static constexpr size_t kTrajMax = 96;
    static constexpr float kSustainHopMs = 250.0f; ///< conditioned sustain fits
    static constexpr float kTransitionHoldMs = 250.0f; ///< sustained shift = legato
    static constexpr float kTransitionStepCents = 70.0f;
    static constexpr float kRestrikeRatio = 1.5f;  ///< level-jump test: the new
                            /// strike's energy vs the held note's CURRENT level
    static constexpr float kRestrikeFloor = 0.5f; ///< strike-like test: the new
                            /// strike's energy vs the previous strike's energy.
                            /// A repeated pitch is a RE-STRIKE if EITHER test
                            /// passes (an equal-dynamic repeat restarts at the
                            /// old strike's level but barely lifts the hold);
                            /// below both it is an EXTENSION of the held note
    static constexpr float kTransitionResetCents = 35.0f;

    struct CandidateEntry {
        uint32_t cand_note_id = 0; ///< 0 = anonymous (record assigned at the attack)
        float midi = 0.0f;
        uint8_t struck = 0;        ///< 1 = struck at this attack (own record + lifetime)
    };

    struct CandidateSet {
        uint32_t note_id = 0;      ///< attack/group leader record (0 = next attack)
        uint8_t count = 0;
        CandidateEntry entries[kMaxCandidates];
    };

    /// Setup path: allocates all scratch once. Instrument behaviour comes
    /// from `profile` (pack data) — never from instrument names.
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

    /// Share the calibrated onset energy floor with the pipeline's stage 0.
    void set_noise_floor(float energy) noexcept { detector_.set_noise_floor(energy); }

    /// Caller-supplied hypotheses for one attack (fixed-slot copy, no
    /// allocation). Applies to the named attack's group (note_id = the
    /// group's leader record, or 0 = the next attack). Call any time after
    /// the HIT and before that group's probe fit.
    bool set_candidates(const CandidateSet& set) noexcept {
        if (set.count > kMaxCandidates) return false;
        if (set.note_id == 0) {
            if (have_next_cands_) return false; // previous set not consumed yet
            next_cands_ = set;
            have_next_cands_ = true;
            return true;
        }
        for (auto& g : groups_) {
            if (g.live && g.leader == set.note_id && !g.probe_done) {
                install_candidates(g, set);
                return true;
            }
        }
        return false;
    }

    /// ANALYSIS FAST PATH (consumer thread, allocation-free): one block of
    /// stage-0-conditioned audio in, zero or more events out (HIT, PITCH
    /// provisional for blind records). Measurements come from
    /// finalize_due().
    void process_block(const float* samples, size_t n) noexcept {
        double acc = 0.0;
        for (size_t i = 0; i < n; ++i) acc += double(samples[i]) * double(samples[i]);
        block_strength_ = n > 0 ? float(std::sqrt(acc / double(n))) : 0.0f;
        if (detector_.process_onset_block(samples, n)) {
            open_strike(sample_pos_ + n); // block-quantized
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

    /// SCHEDULED WORK: at most one measurement per call (probe fit, sustain
    /// fit) — the expensive part; an app may own this loop on a worker
    /// thread. Returns true when one measurement was performed.
    bool finalize_due() noexcept {
        Group* g = nullptr;
        int stage = 0; // 1 = probe fit, 2 = sustain fit
        for (auto& c : groups_) {
            if (!c.live) continue;
            if (c.have_cands && !c.probe_done) {
                if (g == nullptr || c.id < g->id) { g = &c; stage = 1; }
            } else if (c.have_cands && c.live_records > 0 && c.sustain_due) {
                if (g == nullptr || c.id < g->id) { g = &c; stage = 2; }
            } else if (!c.have_cands && !c.class_done) {
                // Blind attacks still get their CONTENT class fact (the
                // mono/polyphonic statement is per attack, not per
                // candidate set).
                if (g == nullptr || c.id < g->id) { g = &c; stage = 3; }
            }
        }
        if (g == nullptr) return false;
        const auto t0 = std::chrono::steady_clock::now();
        const bool ok = (stage == 1) ? probe_fit(*g)
                      : (stage == 2) ? sustain_fit(*g)
                                     : class_fit(*g);
        if (ok) { // budget counts real measurements, not readiness retries
            ++fit_count_;
            fit_micros_ += std::chrono::duration<double, std::micro>(
                std::chrono::steady_clock::now() - t0).count();
        }
        return ok;
    }

    /// End of stream: close every open record. Deterministic.
    void flush() noexcept {
        while (finalize_due()) {}
        for (auto& n : records_) {
            if (n.live) close_record(n, last_bucket_ms_);
        }
    }

    /// Id of the most recently opened record (0 = none yet).
    uint32_t last_note_id() const noexcept { return last_note_id_; }

    /// Measurement budget counters (diagnostics / load shedding).
    size_t fit_count() const noexcept { return fit_count_; }
    double fit_micros() const noexcept { return fit_micros_; }
    size_t live_groups() const noexcept {
        size_t n = 0;
        for (const auto& g : groups_) if (g.live) ++n;
        return n;
    }

private:
    struct TrajPoint { float t_ms = 0.0f; float amp = 0.0f; float cents = 0.0f; };

    struct Record {
        uint32_t id = 0, strike_group = 0;
        bool live = false, provisional_sent = false;
        uint64_t onset_sample = 0, close_sample = 0;
        float onset_ms = 0.0f, strength = 0.0f;
        float last_rms = 0.0f;    ///< most recent mix level of the record
        int candidate_index = -1;      ///< index in its group's list (-1 = blind)
        float hypothesis_midi = 0.0f;
        // per-note amplitude trajectory (conditioned joint fits)
        TrajPoint traj[kTrajMax];
        size_t traj_n = 0;
        float peak_amp = 0.0f;
        float sim_wsum = 0.0f, sim_ww = 0.0f; ///< fit quality, amp-weighted mean
        float best_presence = 0.0f;           ///< strongest measured presence share
        size_t quiet_fits = 0;

        /// Fit quality of the claim: weighted by the note's own amplitude,
        /// the same weighting as the pitch center.
        float sim_mean() const noexcept {
            return sim_ww > 0.0f ? sim_wsum / sim_ww : 0.0f;
        }
        float last_audible_ms = -1.0f;
        // mix readings (blind finals, vibrato, transitions) — leader only
        float ref_midi = 0.0f;
        bool have_ref = false;
        float dev_sum = 0.0f, prev_dev = 0.0f, max_dev = 0.0f;
        size_t readings = 0, valid_readings = 0, hops = 0;
        size_t next_reading_ms = 50;
        float shift_start_ms = -1.0f;
        TrajectoryBuffer vib;
        // blind envelope (records without fits)
        float rms_series[kRmsSeriesMax] = {0};
        size_t rms_count = 0;
        float peak_rms = 0.0f, quiet_ms = 0.0f;
    };

    struct Group {
        uint32_t id = 0, leader = 0;
        bool live = false, probe_done = false, class_done = false;
        bool have_cands = false, sustain_due = false;
        size_t live_records = 0;
        uint64_t window_start = 0;
        uint64_t strike_sample = 0;     ///< this execution's strike anchor
        float strike_ms = 0.0f;
        uint64_t boundary = UINT64_MAX; ///< next strike: no measurement
                                        /// window may cross it
        uint8_t repeat_pending[kMaxCandidates] = {0}; ///< struck candidate
                                        /// repeats a live record's pitch:
                                        /// re-strike vs extension, decided
                                        /// at the probe by level comparison
        uint32_t repeat_owner[kMaxCandidates] = {0};
        uint8_t deferred_repeat = 0;    ///< mono path: the strike repeats a
                                        /// live record's pitch; decided at
                                        /// the first measurement slot
        float pre_rms = 0.0f;           ///< the hold's level at the strike
        uint32_t held_owner = 0;        ///< the record being held
        uint64_t sustain_end = 0;   ///< end of the next sustain window
        size_t next_sustain_ms = 0;
        size_t n_cand = 0;
        float midis[kMaxCandidates] = {0};
        uint32_t owners[kMaxCandidates] = {0};
        uint8_t struck[kMaxCandidates] = {0};
        uint8_t pruned[kMaxCandidates] = {0};  ///< dropped from later fits (silent)
        uint8_t quiet_hits[kMaxCandidates] = {0};
        float similarity = 0.0f;
    };

    // ---- emission ---------------------------------------------------------
    void emit(const NoteEvent& e) noexcept {
        for (size_t i = 0; i < kMaxSubscribers; ++i) {
            if (subscribers_[i] != nullptr) subscribers_[i]->push(e);
        }
    }

    Record* alloc_record() noexcept {
        for (auto& n : records_) {
            if (!n.live) return &n;
        }
        // Table full: close the oldest record to make room.
        Record* oldest = nullptr;
        for (auto& n : records_) {
            if (n.live && (oldest == nullptr || n.id < oldest->id)) oldest = &n;
        }
        if (oldest != nullptr) close_record(*oldest, last_bucket_ms_);
        return oldest;
    }

    void install_candidates(Group& g, const CandidateSet& set) noexcept {
        g.n_cand = std::min<size_t>(set.count, kMaxCandidates);
        for (size_t k = 0; k < g.n_cand; ++k) {
            g.midis[k] = set.entries[k].midi;
            g.struck[k] = set.entries[k].struck;
            g.owners[k] = set.entries[k].cand_note_id;
        }
        g.have_cands = true;
    }

    /// A strike (or the opening of the very first record): one record per
    /// struck candidate, or one blind record when no candidates.
    void open_strike(uint64_t onset_sample) noexcept {
        const float onset_ms = 1000.0f * float(onset_sample) / sr_;

        // Window discipline: this strike ends the measurement epoch of
        // every earlier group (no window may contain the new attack).
        for (auto& c : groups_) {
            if (c.live && c.boundary > onset_sample) c.boundary = onset_sample;
        }
        // Monophonic instrument physics (profile data): one note at a time,
        // so a live record's fate is decided by this strike — and the
        // decision is the energy-level comparison between the strikes:
        //   - level jump >= kRestrikeRatio x the record's own strike energy
        //     -> the pitch was STRUCK AGAIN: the old note ends here, a new
        //        record opens (flagged RESTRIKE);
        //   - below that -> the held note is EXTENDED (a retrigger-like
        //        event on a sustain): no new record, the old one continues.
        bool restrike = false;
        bool defer_repeat = false;
        float held_rms = 0.0f;
        uint32_t held_id = 0;
        if (profile_.max_simultaneous_notes <= 1) {
            Record* live = nullptr;
            for (auto& n : records_) {
                if (n.live) { live = &n; break; }
            }
            if (live != nullptr) {
                // A repeated pitch while a note holds: re-strike or
                // extension? The level evidence needs ~40 ms to settle (a
                // re-strike jumps the level up; a continuation decays on),
                // so the decision is deferred to the first measurement
                // slot. The HIT timestamp stays the strike time either way.
                defer_repeat = true;
                held_rms = live->last_rms;
                held_id = live->id;
            }
        }

        Group* g = nullptr;
        for (auto& c : groups_) {
            if (!c.live) { g = &c; break; }
        }
        if (g == nullptr) {
            for (auto& c : groups_) {
                if (g == nullptr || c.id < g->id) g = &c;
            }
            for (auto& n : records_) {
                if (n.live && n.strike_group == g->id) close_record(n, onset_ms);
            }
        }
        if (g == nullptr) return;
        *g = Group{};
        g->id = next_note_id_;
        g->live = true;
        g->strike_sample = onset_sample;
        g->window_start = onset_sample + uint64_t(profile_.blanking_ms / 1000.0f * sr_);
        if (have_next_cands_) {
            install_candidates(*g, next_cands_);
            have_next_cands_ = false;
        }

        // Repeated pitches: a struck candidate whose pitch already sounds
        // (a live record claims it) is an EXTENSION or a RE-STRIKE — decided
        // at the probe fit by comparing energy levels (the record's recent
        // fitted level vs the level after this strike). Until then it is
        // held by the existing record (deferred, not a new one).
        for (size_t k = 0; k < g->n_cand; ++k) {
            if (!g->struck[k]) continue;
            for (auto& n : records_) {
                if (!n.live || n.candidate_index < 0) continue;
                if (std::fabs(n.hypothesis_midi - g->midis[k]) < 0.5f) {
                    g->repeat_pending[k] = 1;
                    g->repeat_owner[k] = n.id;
                    g->owners[k] = n.id; // facts route there meanwhile
                    break;
                }
            }
        }

        // Records: one per struck candidate (or one blind record).
        size_t n_struck = 0;
        if (g->have_cands) {
            for (size_t k = 0; k < g->n_cand; ++k) {
                if (g->struck[k] && !g->repeat_pending[k]) ++n_struck;
            }
        }
        const size_t n_rec = (n_struck > 0 && n_struck <= kMaxCandidates) ? n_struck : 1;
        if (defer_repeat) {
            g->deferred_repeat = 1;
            g->pre_rms = held_rms;
            g->held_owner = held_id;
            g->strike_sample = onset_sample;
            g->strike_ms = onset_ms;
            return; // resolved at the first measurement slot
        }

        // Records are created bound to their struck candidates (in order),
        // so the HIT can carry the record's identity from the start.
        size_t k = 0;
        for (size_t r = 0; r < n_rec; ++r) {
            while (k < g->n_cand && (!g->struck[k] || g->repeat_pending[k])) ++k;
            Record* rec = alloc_record();
            if (rec == nullptr) return;
            *rec = Record{};
            rec->id = next_note_id_++;
            rec->strike_group = g->id;
            rec->live = true;
            rec->onset_sample = onset_sample;
            rec->onset_ms = onset_ms;
            rec->strength = block_strength_;
            if (n_struck > 0 && k < g->n_cand) {
                rec->candidate_index = int(k);
                rec->hypothesis_midi = g->midis[k];
                g->owners[k] = rec->id;
                ++k;
            }
            if (g->leader == 0) g->leader = rec->id;
            ++g->live_records;
            last_note_id_ = rec->id;
            emit(note_event::make_hit(rec->id, g->id, onset_ms, onset_ms,
                                      block_strength_, rec->hypothesis_midi,
                                      restrike ? kFlagRestrike : kFlagNone));
        }
    }

    /// A record opened by a sustained pitch change (legato / transition
    /// note) — profile data decides; no strike happened.
    void open_transition(uint64_t at_sample, float midi) noexcept {
        const float t_ms = 1000.0f * float(at_sample) / sr_;
        Group* g = nullptr;
        for (auto& c : groups_) {
            if (!c.live) { g = &c; break; }
        }
        if (g == nullptr) return;
        *g = Group{};
        g->id = next_note_id_;
        g->live = true;
        g->leader = next_note_id_;
        g->window_start = at_sample + uint64_t(profile_.blanking_ms / 1000.0f * sr_);
        Record* rec = alloc_record();
        if (rec == nullptr) return;
        *rec = Record{};
        rec->id = next_note_id_++;
        rec->strike_group = g->id;
        rec->live = true;
        rec->onset_sample = at_sample;
        rec->onset_ms = t_ms;
        rec->strength = block_strength_;
        ++g->live_records;
        last_note_id_ = rec->id;
        (void)midi;
        emit(note_event::make_hit(rec->id, g->id, t_ms, t_ms, block_strength_,
                                  midi, kFlagTransition));
    }

    // ---- per-bucket bookkeeping (10 ms grid, block-size independent) ------
    void close_bucket() noexcept {
        const float rms = float(std::sqrt(bucket_acc_ / double(bucket_n_)));
        const float t_ms = 1000.0f * float(sample_pos_) / sr_;
        last_bucket_ms_ = t_ms;
        bucket_acc_ = 0.0;
        bucket_n_ = 0;

        for (auto& n : records_) {
            if (!n.live) continue;
            if (rms > n.peak_rms) n.peak_rms = rms;
            n.last_rms = rms;
            if (n.rms_count < kRmsSeriesMax) n.rms_series[n.rms_count++] = rms;
        }

        // Mix readings per live group leader: stability, vibrato trajectory,
        // legato transitions (all bookkeeping on the bucket grid).
        for (auto& g : groups_) {
            if (!g.live || g.leader == 0) continue;
            Record* lead = find_record(g.leader);
            if (lead == nullptr || !lead->live) continue;
            const float since = t_ms - lead->onset_ms;
            if (since >= profile_.blanking_ms && since + 0.5f >= float(lead->next_reading_ms)) {
                take_reading(*lead, t_ms);
                lead->next_reading_ms += 50;
                if (profile_.accepts_legato_boundaries && profile_.max_simultaneous_notes <= 1
                    && g.live_records == 1 && !g.have_cands) {
                    check_transition(*lead, g, t_ms);
                }
            }
            // Blind provisional: first clean window of a no-candidate group.
            if (!g.have_cands && !lead->provisional_sent && since >= profile_.blanking_ms
                + 1000.0f * float(kProvisionalWindow) / sr_) {
                emit_blind_provisional(*lead, t_ms);
            }
            // Sustain fits become due on their own cadence.
            if (g.have_cands && g.probe_done && g.live_records > 0
                && since + 0.5f >= float(g.next_sustain_ms)) {
                // The window END is fixed by the cadence (bucket grid), not
                // by when the scheduler runs the fit — determinism.
                g.sustain_due = true;
                g.sustain_end = sample_pos_;
                // Adaptive cadence: fine through the attack and early life,
                // stretched once the note is established (end detection
                // precision degrades gracefully, facts never drop).
                const float age = since;
                // Cadence: fine through the attack and early life (the
                // center of the note), stretched for established notes and
                // long pedal tails — end detection precision degrades
                // gracefully; facts never drop.
                g.next_sustain_ms += (age < 2000.0f) ? size_t(kSustainHopMs)
                                    : (age < 5000.0f) ? size_t(kSustainHopMs * 2)
                                                     : size_t(kSustainHopMs * 4);
            }
        }

        // Re-strike vs extension resolution (mono repeated pitch): the
        // level after the strike vs the hold's level at the strike. A jump
        // = the pitch was struck again (old note ends at the strike, a new
        // record opens); no jump = the held note was merely extended (no
        // new record — the hold continues).
        for (auto& g : groups_) {
            if (!g.live || !g.deferred_repeat) continue;
            const float since = t_ms - g.strike_ms;
            if (since < profile_.blanking_ms
                + 1000.0f * float(kProvisionalWindow) / sr_) continue;
            g.deferred_repeat = 0;
            Record* old = find_record(g.held_owner);
            const float now = (old != nullptr && old->live) ? old->last_rms : 0.0f;
            const bool restrike = old == nullptr || !old->live
                || now >= g.pre_rms * kRestrikeRatio;
            if (!restrike) {
                g.live = false; // extension: the hold continues, no record
                continue;
            }
            if (old != nullptr && old->live) close_record(*old, g.strike_ms);
            Record* rec = alloc_record();
            if (rec == nullptr) { g.live = false; continue; }
            *rec = Record{};
            rec->id = next_note_id_++;
            rec->strike_group = g.id;
            rec->live = true;
            rec->onset_sample = g.strike_sample;
            rec->onset_ms = g.strike_ms;
            rec->strength = block_strength_;
            rec->last_rms = block_strength_;
            g.leader = rec->id;
            ++g.live_records;
            last_note_id_ = rec->id;
            emit(note_event::make_hit(rec->id, g.id, g.strike_ms, g.strike_ms,
                                      block_strength_, 0.0f, kFlagRestrike));
        }

        // Blind decay-relative end (records without fitted trajectories).
        for (auto& n : records_) {
            if (!n.live || n.candidate_index >= 0) continue;
            const bool quiet = rms <= 0.1f * n.peak_rms;
            n.quiet_ms = quiet ? n.quiet_ms + 1000.0f * float(bucket_samples_) / sr_ : 0.0f;
            if (n.quiet_ms >= 80.0f && profile_.max_simultaneous_notes > 1) {
                close_record(n, t_ms - n.quiet_ms);
            }
        }
    }

    Record* find_record(uint32_t id) noexcept {
        for (auto& n : records_) {
            if (n.id == id) return &n;
        }
        return nullptr;
    }

    void take_reading(Record& n, float t_ms) noexcept {
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
        n.prev_dev = rel;
        // While a legato shift is pending (or after it began), the readings
        // belong to the NEXT note — they must not pollute this record's
        // center or stability verdict.
        if (n.shift_start_ms >= 0.0f && t_ms >= n.shift_start_ms) return;
        if (n.readings > 1 && std::fabs(rel - n.prev_dev) > 200.0f) ++n.hops;
        n.dev_sum += rel;
        n.max_dev = std::max(n.max_dev, std::fabs(rel));
        n.vib.add(t_ms, rel);
    }

    /// Legato transition: a SUSTAINED pitch shift (not a vibrato swing —
    /// vibrato returns, a step does not). Opens a new record at the shift.
    void check_transition(Record& n, Group& g, float t_ms) noexcept {
        const float rel = n.prev_dev;
        if (std::fabs(rel) > kTransitionStepCents) {
            if (n.shift_start_ms < 0.0f) n.shift_start_ms = t_ms;
        } else if (std::fabs(rel) < kTransitionResetCents) {
            n.shift_start_ms = -1.0f;
        }
        if (n.shift_start_ms >= 0.0f
            && t_ms - n.shift_start_ms >= kTransitionHoldMs) {
            const uint64_t at = uint64_t(double(n.shift_start_ms) * double(sr_) / 1000.0);
            // Close the old record at the shift, open the transition record.
            close_record(n, n.shift_start_ms);
            g.live = false; // the old group ends with its record
            open_transition(at, n.ref_midi + n.prev_dev / 100.0f);
        }
    }

    void emit_blind_provisional(Record& n, float t_ms) noexcept {
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
            emit(note_event::make_pitch_unresolved(n.id, t_ms, PitchStage::Provisional,
                                                   kFlagMixed));
            return;
        }
        emit(note_event::make_pitch(n.id, t_ms, PitchStage::Provisional,
                                    est.midi, est.cents_offset, kFlagNone));
    }

    // ---- conditioned fits (scheduled work) --------------------------------
    /// Exact measurement window [group start, +len): the window must be
    /// complete — a call-time-truncated window would make the stream
    /// depend on WHEN the scheduler ran (breaking live/offline equality).
    bool group_window(Group& g, size_t len, float* out, float& t_ms) noexcept {
        // Window discipline: no measurement window crosses a strike
        // boundary — evidence after the next attack belongs to that
        // attack's group (which measures the sustaining notes too, when the
        // chart lists them). The boundary is strike-determined (audio), so
        // the clipped window stays deterministic.
        uint64_t end = g.window_start + len;
        if (end > g.boundary) end = g.boundary;
        if (end <= g.window_start + 256) return false; // nothing measurable
        if (end > sample_pos_) return false;           // not ready yet
        const size_t n = size_t(end - g.window_start);
        if (!copy_history_range(g.window_start, n, out)) return false;
        t_ms = 1000.0f * float(end) / sr_;
        return true;
    }

    /// CONTENT class for a blind group (no candidates): the mono/polyphonic
    /// statement is an attack fact. Window ready check keeps it
    /// deterministic (emitted at the first scheduled call where the
    /// classifier's evidence window exists).
    bool class_fit(Group& g) noexcept {
        float cbuf[kFinalWindow];
        float ct = 0.0f;
        if (!group_window(g, kFinalWindow, cbuf, ct)) return false;
        g.class_done = true;
        const BlindContent bc = classify_content(cbuf, kFinalWindow, sr_, profile_);
        const uint32_t pf = (bc.klass == ContentClass::Polyphonic)
            ? kFlagPolyphonic : kFlagNone;
        emit(note_event::make_content_class(g.leader, ct, bc.klass,
                                            bc.n_series, bc.capture, pf));
        return true;
    }

    /// Probe fit (first clean window, 1024 samples): per-record provisional
    /// pitch + first per-candidate CONTENT facts. Runs once per attack,
    /// when its window exists. Returns false while the window is not ready
    /// (the measurement stays pending and is retried).
    bool probe_fit(Group& g) noexcept {
        float window[kProvisionalWindow];
        float t_ms = 0.0f;
        if (!group_window(g, kProvisionalWindow, window, t_ms)) {
            return false; // window not ready yet — stay pending
        }
        g.probe_done = true;
        float set[kMaxCandidates] = {0};
        for (size_t k = 0; k < g.n_cand; ++k) set[k] = g.midis[k];
        if (!validator_.set_expected_chord(set, g.n_cand)) return true;
        validator_.set_search_range_cents(profile_.intonation_band_cents);
        validator_.set_passes(3); // attack window: full robust passes
        const ChordResult r = validator_.validate(window, kProvisionalWindow);
        g.similarity = r.similarity;

        // Re-strike vs extension (energy-level comparison, per repeated
        // pitch): the level right after this strike vs the sounding record's
        // recent level. A jump opens a new record (re-strike); a smooth
        // continuation keeps the facts in the existing register.
        for (size_t k = 0; k < g.n_cand; ++k) {
            if (!g.repeat_pending[k]) continue;
            g.repeat_pending[k] = 0;
            Record* old = find_record(g.repeat_owner[k]);
            const float amp_now = validator_.note_rms(k);
            if (old == nullptr || !old->live) {
                old = nullptr; // the old record is gone: this is a new note
            }
            const float old_level = (old != nullptr)
                ? ((old->traj_n > 0) ? old->traj[old->traj_n - 1].amp : old->peak_amp)
                : 0.0f;
            const bool restrike = (old == nullptr)
                || (amp_now >= old_level * kRestrikeRatio)
                || (amp_now >= old->peak_amp * kRestrikeFloor);
            if (!restrike) continue; // extension: facts stay in the old register
            Record* rec = alloc_record();
            if (rec == nullptr) continue;
            *rec = Record{};
            rec->id = next_note_id_++;
            rec->strike_group = g.id;
            rec->live = true;
            rec->onset_sample = g.strike_sample;
            rec->onset_ms = 1000.0f * float(g.strike_sample) / sr_;
            rec->strength = block_strength_;
            rec->candidate_index = int(k);
            rec->hypothesis_midi = g.midis[k];
            rec->best_presence = r.note_presence[k];
            g.owners[k] = rec->id;
            ++g.live_records;
            last_note_id_ = rec->id;
            emit(note_event::make_hit(rec->id, g.id, rec->onset_ms, rec->onset_ms,
                                      block_strength_, g.midis[k], kFlagRestrike));
        }
        for (size_t k = 0; k < g.n_cand; ++k) {
            Record* rec = find_record(g.owners[k]);
            if (rec != nullptr) {
                const float w = validator_.note_rms(k);
                rec->sim_wsum += r.similarity * w;
                rec->sim_ww += w;
                rec->best_presence = std::max(rec->best_presence, r.note_presence[k]);
            }
        }
        const uint32_t sim_flag =
            (r.similarity < 0.80f) ? kFlagUnexplainedEnergy : kFlagNone;
        for (size_t k = 0; k < g.n_cand; ++k) {
            Record* rec = find_record(g.owners[k]);
            const float refined = validator_.refined_cents(k);
            uint32_t f = sim_flag;
            if (std::fabs(refined) >= 59.0f) f |= kFlagSearchSaturated;
            emit(note_event::make_content_candidate(
                rec != nullptr ? rec->id : g.leader, g.id, t_ms, uint8_t(k),
                g.midis[k], r.note_presence[k], refined, validator_.note_rms(k),
                r.similarity, f));
            if (rec != nullptr && rec->live) {
                // Seed the per-note trajectory: short notes may close
                // before any sustain fit and still deserve a final.
                if (rec->traj_n < kTrajMax) {
                    rec->traj[rec->traj_n++] = {t_ms, validator_.note_rms(k), refined};
                }
                if (validator_.note_rms(k) > rec->peak_amp) {
                    rec->peak_amp = validator_.note_rms(k);
                }
                if (rec->last_audible_ms < 0.0f) rec->last_audible_ms = t_ms;
            }
            if (rec != nullptr && rec->live && !rec->provisional_sent) {
                rec->provisional_sent = true;
                // Display-only, but still honest: a candidate barely heard
                // (presence < 0.05 in a multi-note set) gets no pitch claim.
                const bool heard = (g.n_cand <= 1) || (r.note_presence[k] >= 0.05f);
                if ((f & kFlagSearchSaturated) != 0 || r.similarity < 0.40f || !heard) {
                    emit(note_event::make_pitch_unresolved(rec->id, t_ms,
                                                           PitchStage::Provisional,
                                                           f | (heard ? kFlagNone : kFlagMixed)));
                } else {
                    emit(note_event::make_pitch(rec->id, t_ms, PitchStage::Provisional,
                                                g.midis[k], refined, f));
                }
            }
        }
        return true;
    }

    /// Sustain fit (SLIDING window, 250 ms cadence): per-candidate amplitude
    /// trajectory -> per-note lifetimes + pitch centers. One fit measures
    /// every live candidate at once (the joint model). The window ends at
    /// the cadence point fixed on the bucket grid — never at call time.
    /// Performance (the resource budget of the design doc): candidates that
    /// have gone silent are pruned from later fits (cost falls
    /// quadratically with candidate count), clean sustain windows run one
    /// IRLS pass instead of three, and the window shrinks to 2048 once
    /// few candidates remain.
    bool sustain_fit(Group& g) noexcept {
        g.sustain_due = false;
        // Live candidates only: a fit over dead hypotheses is pure cost.
        size_t map[kMaxCandidates] = {0};
        float set[kMaxCandidates] = {0};
        size_t n_live = 0;
        for (size_t k = 0; k < g.n_cand; ++k) {
            if (g.pruned[k]) continue;
            map[n_live] = k;
            set[n_live] = g.midis[k];
            ++n_live;
        }
        if (n_live == 0) return false; // nothing left to measure
        const size_t win_len = 2048u; // sustain: 43 ms separates neighbours
                                      // at mid/bass register at half cost
        uint64_t wend = g.sustain_end;
        if (wend > g.boundary) wend = g.boundary; // never cross a strike
        if (wend < g.window_start + 256) return false;
        uint64_t wstart = (wend > win_len) ? wend - win_len : g.window_start;
        if (wstart < g.window_start) wstart = g.window_start;
        const size_t n_win = size_t(wend - wstart);
        if (n_win < 256) return false;
        float window[kSustainWindow];
        if (!copy_history_range(wstart, n_win, window)) return false;
        const float t_ms = 1000.0f * float(wend) / sr_;
        if (!validator_.set_expected_chord(set, n_live)) return true;
        validator_.set_search_range_cents(profile_.intonation_band_cents);
        validator_.set_passes(1);        // clean windows: one robust pass
        validator_.set_refine_rounds(2); // cents still searched, 2/3 cost
        const ChordResult r = validator_.validate(window, n_win);
        g.similarity = r.similarity;
        for (size_t idx = 0; idx < n_live; ++idx) {
            Record* rec = find_record(g.owners[map[idx]]);
            if (rec != nullptr) {
                const float w = validator_.note_rms(idx);
                rec->sim_wsum += r.similarity * w;
                rec->sim_ww += w;
                rec->best_presence = std::max(rec->best_presence, r.note_presence[idx]);
            }
        }

        // Class fact at the first fit where the classifier's evidence
        // window (as long as we can make it) actually exists — the cadence
        // is bucket-fixed, so which fit emits it is deterministic.
        if (!g.class_done) {
            float cbuf[kFinalWindow];
            float ct = 0.0f;
            if (group_window(g, kFinalWindow, cbuf, ct)) {
                g.class_done = true;
                const BlindContent bc = classify_content(cbuf, kFinalWindow, sr_, profile_);
                const uint32_t pf = (bc.klass == ContentClass::Polyphonic)
                    ? kFlagPolyphonic : kFlagNone;
                emit(note_event::make_content_class(g.leader, ct, bc.klass,
                                                    bc.n_series, bc.capture, pf));
            }
        }

        for (size_t idx = 0; idx < n_live; ++idx) {
            const size_t k = map[idx];
            Record* rec = find_record(g.owners[k]);
            if (rec == nullptr || !rec->live) continue;
            const float amp = validator_.note_rms(idx);
            const float refined = validator_.refined_cents(idx);
            // Pruning: a candidate below 5% of its own peak for two
            // consecutive fits leaves the joint set (cost drops
            // quadratically with the candidate count).
            if (rec->peak_amp > 0.0f && amp < rec->peak_amp * 0.05f) {
                if (++g.quiet_hits[k] >= 2) g.pruned[k] = 1;
            } else {
                g.quiet_hits[k] = 0;
            }
            if (rec->traj_n < kTrajMax) {
                rec->traj[rec->traj_n++] = {t_ms, amp, refined};
            }
            if (amp > rec->peak_amp) rec->peak_amp = amp;
            if (amp >= rec->peak_amp * 0.10f) {
                rec->quiet_fits = 0;
                rec->last_audible_ms = t_ms;
            } else if (++rec->quiet_fits >= 2) {
                // -20 dB sustained: the note's own end (an absent hypothesis
                // ends at its first fit time — it never sounded).
                const float end = rec->last_audible_ms >= 0.0f
                    ? rec->last_audible_ms
                    : (rec->traj_n > 0 ? rec->traj[0].t_ms : t_ms);
                close_record(*rec, end);
            }
        }
        return true;
    }

    // ---- record close: final facts from the note's own trajectory ---------
    void close_record(Record& n, float end_ms) noexcept {
        if (!n.live) return;
        n.live = false;
        n.close_sample = uint64_t(double(end_ms) * double(sr_) / 1000.0);
        for (auto& g : groups_) {
            if (g.id == n.strike_group && g.live && g.live_records > 0) {
                --g.live_records;
                if (g.live_records == 0) g.live = false;
            }
        }

        // PITCH final: the amplitude-weighted CENTER of the trajectory
        // (vibrato center, not initial value); blind records use the center
        // of their readings. Unstable / unexplained -> honest "unresolved".
        const bool stable = n.readings > 1
            && float(n.hops) / float(n.readings - 1) <= 0.2f && n.max_dev <= 200.0f;
        bool have_final = false;
        float final_midi = 0.0f, final_cents = 0.0f;
        uint32_t f = kFlagNone;
        // Presence is the share of the fitted set: trivially 1.0 for a
        // single-hypothesis group (its own register), discriminating inside
        // multi-note sets. Similarity gates the rest.
        const bool heard = n.best_presence >= 0.05f;
        const float fit_quality = n.sim_mean();
        if (n.candidate_index >= 0) {
            float wsum = 0.0f, csum = 0.0f;
            for (size_t i = 0; i < n.traj_n; ++i) {
                if (n.peak_amp > 0.0f && n.traj[i].amp >= n.peak_amp * 0.10f) {
                    csum += n.traj[i].cents * n.traj[i].amp;
                    wsum += n.traj[i].amp;
                }
            }
            // A final pitch is emitted only when the record was actually
            // heard (presence) and the set explains the window (similarity).
            // Anything less is an honest "unresolved" — never an invented
            // pitch (the fairness rule).
            if (wsum > 0.0f && fit_quality >= 0.80f && heard) {
                have_final = true;
                final_midi = n.hypothesis_midi;
                final_cents = csum / wsum;
                if (fit_quality < 0.80f) f |= kFlagUnexplainedEnergy;
            } else {
                f |= kFlagUnexplainedEnergy;
            }
        } else if (n.have_ref && n.valid_readings >= 3 && stable) {
            have_final = true;
            final_midi = n.ref_midi;
            final_cents = n.dev_sum / float(n.valid_readings); // vibrato center
        } else if (n.have_ref && n.valid_readings >= 1 && n.readings == 1) {
            have_final = true;
            final_midi = n.ref_midi;
            final_cents = n.dev_sum;
        }
        if (have_final) {
            // Canonical form: cents within +/-50 (nearest note carries the
            // name); the pair and the derived Hz never disagree.
            const int shift = int(std::lround(final_cents / 100.0f));
            final_midi += float(shift);
            final_cents -= 100.0f * float(shift);
        }
        if (!have_final) {
            if (n.readings >= 2 && float(n.valid_readings) / float(n.readings) < 0.5f) {
                f |= kFlagMixed;
            }
            if (n.readings > 1 && float(n.hops) / float(n.readings - 1) > 0.2f) {
                f |= kFlagHop;
            }
            if (n.candidate_index < 0 && n.max_dev <= 200.0f && n.valid_readings >= 3) {
                // wide vibrato makes readings swing; a stable swing is
                // still a measurable note — use its center.
                have_final = true;
                final_midi = n.ref_midi;
                final_cents = n.dev_sum / float(n.valid_readings);
                f &= ~uint32_t(kFlagHop | kFlagMixed);
            }
        }
        if (have_final) {
            if (final_midi < 48.0f && n.traj_n == 0) f |= kFlagLowRegister;
            emit(note_event::make_pitch(n.id, end_ms, PitchStage::Final,
                                        final_midi, final_cents, f));
        } else {
            emit(note_event::make_pitch_unresolved(n.id, end_ms, PitchStage::Unresolved, f));
        }

        // Final CONTENT fact from the trajectory (per-note center values).
        if (n.candidate_index >= 0 && n.traj_n > 0) {
            float wsum = 0.0f, csum = 0.0f, asum = 0.0f;
            size_t used = 0;
            for (size_t i = 0; i < n.traj_n; ++i) {
                if (n.peak_amp > 0.0f && n.traj[i].amp >= n.peak_amp * 0.10f) {
                    csum += n.traj[i].cents * n.traj[i].amp;
                    asum += n.traj[i].amp;
                    wsum += n.traj[i].amp;
                    ++used;
                }
            }
            const float center = wsum > 0.0f ? csum / wsum : 0.0f;
            const float mean_amp = used > 0 ? asum / float(used) : n.peak_amp;
            emit(note_event::make_content_candidate(
                n.id, n.strike_group, end_ms, uint8_t(n.candidate_index),
                n.hypothesis_midi, n.best_presence, center, mean_amp, fit_quality,
                kFlagNone));
        }

        // EXPRESSION: envelope from the trajectory (or the mix series),
        // dynamics tier from the attack peak, vibrato from stable readings.
        float attack_ms = 0.0f, release_ms = 0.0f, sustain = 0.0f;
        if (n.traj_n >= 3) {
            size_t peak_i = 0;
            for (size_t i = 1; i < n.traj_n; ++i) {
                if (n.traj[i].amp > n.traj[peak_i].amp) peak_i = i;
            }
            attack_ms = std::max(0.0f, n.traj[peak_i].t_ms - n.onset_ms);
            release_ms = std::max(0.0f, end_ms - n.traj[peak_i].t_ms);
            size_t above = 0;
            for (size_t i = 0; i < n.traj_n; ++i) {
                if (n.traj[i].amp >= n.traj[peak_i].amp * 0.5f) ++above;
            }
            sustain = n.traj_n > 0 ? float(above) / float(n.traj_n) : 0.0f;
        } else if (n.rms_count >= 4) {
            EnvelopeAnalyzer env;
            const EnvelopeMeasurement m =
                env.measure(n.rms_series, n.rms_count,
                            1000.0f * float(bucket_samples_) / sr_);
            attack_ms = m.attack_ms;
            release_ms = m.release_ms;
            sustain = m.sustain_ratio;
        }
        const float peak = n.peak_amp > 0.0f ? n.peak_amp : n.peak_rms;
        const float dyn_db = (peak > 0.0f) ? 20.0f * std::log10(peak) : -120.0f;
        const DynamicsTier tier = dynamics_.observe(peak);
        const bool stable_pitch = n.readings > 1
            && float(n.hops) / float(n.readings - 1) <= 0.2f && n.max_dev <= 200.0f;
        // Vibrato from the 50 ms pitch readings — ALL records (audit fix:
        // this used to be blind-only, so the game's score-informed path
        // emitted vib_hz 0.0 structurally). Non-leader chord voices take no
        // readings and honestly report no vibrato claim; the coherence and
        // rate-stability gates (technique.hpp) keep fit-noise wobble from
        // being claimed.
        VibratoMeasurement v;
        if (stable_pitch) {
            VibratoAnalyzer vib;
            v = vib.measure(n.vib);
        }
        // Bend / slide (schema v3): a monotonic glide within the record.
        // Disambiguated by exclusion: no vibrato claim on this record (that
        // is an oscillation, not a glide) and no legato transition began on
        // it (a step is a TRANSITION record, not a bend). Threshold 25 c =
        // presence-style measurement gate (below: drift, not a glide).
        float bend_c = 0.0f, bend_err = 0.0f;
        if (!v.periodic && n.shift_start_ms < 0.0f && n.vib.size() >= 4) {
            BendAnalyzer ben;
            const BendMeasurement bm = ben.measure(n.vib);
            if (bm.monotonic && std::fabs(bm.end_cents - bm.start_cents) >= 25.0f) {
                bend_c = bm.end_cents - bm.start_cents;
                bend_err = bm.fit_error_cents;
            }
        }
        emit(note_event::make_expression(n.id, end_ms, dyn_db, float(int(tier)),
                                         attack_ms, release_ms, sustain,
                                         v.rate_hz, v.depth_cents, bend_c, bend_err));
        emit(note_event::make_close(n.id, end_ms, end_ms, end_ms - n.onset_ms, peak));
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
    DynamicsClassifier dynamics_;

    Record records_[kMaxRecords];
    Group groups_[kMaxGroups];
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

    size_t fit_count_ = 0;
    double fit_micros_ = 0.0;
    size_t bucket_samples_ = 480;
    double bucket_acc_ = 0.0;
    size_t bucket_n_ = 0;
    float last_bucket_ms_ = 0.0f;
    float block_strength_ = 0.0f;
};

} // namespace kan
