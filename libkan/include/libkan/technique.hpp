#pragma once
// Technique validators (platform-architecture.md §5): turn the detection
// stream into judgments about HOW a note is played, not just which note it
// is. Three detection families, mirroring the instrument-pack technique
// vocabulary:
//
//   Pitch gestures  (bend / vibrato / slide)  -> cents-trajectory tracking
//                                                vs expected curves
//   Articulation    (staccato / legato /      -> envelope shape + duration
//                    accent / mute)              vs expected
//   Extended        (harmonics, flutter-      -> spectral signature
//                    tongue, pedal...)            templates (slot amplitudes)
//
// Data flow (SPECIFICATION.md §2 + platform-architecture.md §5): libkan
// MEASURES (TrajectoryBuffer from the per-block cents stream,
// EnvelopeAnalyzer from the RMS stream, slot amplitudes from any window);
// the instrument pack SUPPLIES expectations (the plain *Spec structs below —
// same "plain data in, no format coupling" rule as InstrumentProfile); the
// game JUDGES via check() -> TechniqueResult.
//
// Real-time safety: all analyzers run on the consumer thread. Buffers are
// fixed-capacity (add() drops oldest when full); no allocation, no locks in
// any measure/check path.
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace kan {

/// Uniform judgment of one technique against its pack spec.
struct TechniqueResult {
    bool ok = false;
    float score = 0.0f;          ///< [0, 1] closeness to the expected shape
    float error = 0.0f;          ///< primary deviation (cents or ratio)
};

// ===========================================================================
// Cents trajectory (pitch gestures)
// ===========================================================================

/// One pitch observation: deviation from the charted pitch in cents.
struct TrajectoryPoint {
    float t_ms = 0.0f;
    float cents = 0.0f;
};

/// Fixed-capacity trajectory collector fed from the harmonic path once per
/// block/note. Oldest samples drop when full (rolling window).
class TrajectoryBuffer {
public:
    static constexpr size_t kMaxPoints = 512; // ~2.7 s at 5 ms hops

    void clear() noexcept { count_ = 0; head_ = 0; }

    /// Append one observation. Allocation-free.
    void add(float t_ms, float cents) noexcept {
        buf_[head_] = TrajectoryPoint{t_ms, cents};
        head_ = (head_ + 1) % kMaxPoints;
        if (count_ < kMaxPoints) ++count_;
    }

    size_t size() const noexcept { return count_; }
    bool empty() const noexcept { return count_ == 0; }

    /// Chronological access: point(i), i in [0, size()).
    TrajectoryPoint point(size_t i) const noexcept {
        return buf_[(head_ + kMaxPoints - count_ + i) % kMaxPoints];
    }

    float duration_ms() const noexcept {
        return empty() ? 0.0f : point(count_ - 1).t_ms - point(0).t_ms;
    }

private:
    TrajectoryPoint buf_[kMaxPoints];
    size_t head_ = 0;
    size_t count_ = 0;
};

// ---------------------------------------------------------------------------
// Vibrato (incl. Japanese-style wide/slow vibrato: yuri, koto/shamisen
// oscillation) — and its absence ("straight tone" passages).
// ---------------------------------------------------------------------------

struct VibratoSpec {
    float rate_min_hz = 3.0f;        ///< accepted oscillation rate band
    float rate_max_hz = 8.0f;
    float min_depth_cents = 20.0f;   ///< 0-to-peak deviation
    float max_depth_cents = 120.0f;  ///< above this it is a trill/tremolo, not vibrato
    float min_duration_ms = 150.0f;  ///< oscillation must persist this long
    float tolerance_cents = 10.0f;   ///< depth slack when scoring
    bool expect_straight = false;    ///< true = judge a STRAIGHT tone instead
    float straight_tolerance_cents = 8.0f;
};

struct VibratoMeasurement {
    bool periodic = false;      ///< enough full cycles to claim "vibrato"
    float rate_hz = 0.0f;
    float depth_cents = 0.0f;   ///< 0-to-peak (sinusoid fit of the detrended curve)
    float duration_ms = 0.0f;
    float center_cents = 0.0f;  ///< detrended mean (pitch offset around which it oscillates)
    float coherence = 0.0f;     ///< [0,1] variance explained by a sinusoid at
                                ///< rate_hz. Gate for `periodic`: fit-noise
                                ///< wobble (neighbor leakage on straight
                                ///< notes) crosses zeros and swings widely
                                ///< but is NOT coherent oscillation
                                ///< (dataset_bench selftest control).
    bool rate_stable = false;   ///< first/second half rates agree within 30%
};

class VibratoAnalyzer {
public:
    /// Measure the oscillation of a cents trajectory. Needs >= 3 full cycles
    /// to set `periodic`; call on the note's sustain region.
    VibratoMeasurement measure(const TrajectoryBuffer& traj) const noexcept {
        VibratoMeasurement m;
        const size_t n = traj.size();
        if (n < 8) return m;
        m.duration_ms = traj.duration_ms();
        if (m.duration_ms <= 1.0f) return m;

        // Detrend: subtract the mean to isolate the oscillation.
        float mean = 0.0f;
        for (size_t i = 0; i < n; ++i) mean += traj.point(i).cents;
        mean /= float(n);
        m.center_cents = mean;

        // Zero crossings of the detrended curve -> rate (sinusoid: 2
        // crossings per period). The number of half-periods between the
        // first and last crossing is crossings-1; using crossings/2 instead
        // biases the estimate high whenever the trajectory starts or ends
        // near a crossing.
        size_t crossings = 0;
        float first_cross_t = 0.0f, last_cross_t = 0.0f;
        float prev = traj.point(0).cents - mean;
        for (size_t i = 1; i < n; ++i) {
            const float cur = traj.point(i).cents - mean;
            if ((prev <= 0.0f && cur > 0.0f) || (prev >= 0.0f && cur < 0.0f)) {
                if (crossings == 0) first_cross_t = traj.point(i - 1).t_ms;
                last_cross_t = traj.point(i).t_ms;
                ++crossings;
            }
            prev = cur;
        }
        const float cycles = (crossings >= 2) ? float(crossings - 1) / 2.0f : 0.0f;
        const float span = last_cross_t - first_cross_t;
        if (span > 1.0f && cycles >= 1.5f) {
            m.rate_hz = cycles * 1000.0f / span;
        }

        // Depth: 0-to-peak of the detrended curve (sinusoid: ~2x RMS).
        double acc = 0.0;
        for (size_t i = 0; i < n; ++i) {
            const float d = traj.point(i).cents - mean;
            acc += double(d) * double(d);
        }
        const float rms = float(std::sqrt(acc / double(n)));
        m.depth_cents = rms * 1.41421356f;

        // Coherence: least-squares sinusoid at the estimated rate; how much
        // of the detrended variance it explains. Real vibrato is coherent;
        // fit-noise wobble is not.
        if (m.rate_hz > 0.0f) {
            const double w = 2.0 * 3.14159265358979 * double(m.rate_hz) / 1000.0;
            double s2 = 0.0, c2 = 0.0, sc = 0.0, sy = 0.0, cy = 0.0, sst = 0.0;
            for (size_t i = 0; i < n; ++i) {
                const double t = double(traj.point(i).t_ms);
                const double y = double(traj.point(i).cents) - double(mean);
                const double sn = std::sin(w * t), cs = std::cos(w * t);
                s2 += sn * sn; c2 += cs * cs; sc += sn * cs;
                sy += sn * y; cy += cs * y; sst += y * y;
            }
            const double det = s2 * c2 - sc * sc;
            if (det > 1e-12 && sst > 1e-12) {
                const double a = (sy * c2 - cy * sc) / det;
                const double b = (cy * s2 - sy * sc) / det;
                double sse = 0.0;
                for (size_t i = 0; i < n; ++i) {
                    const double t = double(traj.point(i).t_ms);
                    const double y = double(traj.point(i).cents) - double(mean);
                    const double e = y - (a * std::sin(w * t) + b * std::cos(w * t));
                    sse += e * e;
                }
                m.coherence = float(1.0 - sse / sst);
            }
        }

        // Rate stability: crossing rates of both halves must agree (a real
        // vibrato holds its rate; drift is noise or an uncontrolled swing).
        auto half_rate = [&](size_t i0, size_t i1) {
            size_t cr = 0;
            float ft = 0.0f, lt = 0.0f;
            float pv = traj.point(i0).cents - mean;
            for (size_t i = i0 + 1; i < i1; ++i) {
                const float cv = traj.point(i).cents - mean;
                if ((pv <= 0.0f && cv > 0.0f) || (pv >= 0.0f && cv < 0.0f)) {
                    if (cr == 0) ft = traj.point(i - 1).t_ms;
                    lt = traj.point(i).t_ms;
                    ++cr;
                }
                pv = cv;
            }
            return (cr >= 2 && lt > ft) ? float(cr - 1) * 1000.0f / (lt - ft) : 0.0f;
        };
        const float ra = half_rate(0, n / 2);
        const float rb = half_rate(n / 2, n);
        if (ra > 0.0f && rb > 0.0f) {
            m.rate_stable = std::fabs(ra - rb) <= 0.3f * std::max(ra, rb);
        }

        // Resolvability: a claimed rate must sit below the trajectory's own
        // Nyquist. The residual false claims on real piano sat at ~11 Hz --
        // above the 50 ms reading cadence's 10 Hz Nyquist, i.e. noise, not
        // measurable oscillation. 0.8 leaves margin for the rate estimate.
        float dt_ms = 0.0f;
        if (n >= 2) dt_ms = (traj.point(n - 1).t_ms - traj.point(0).t_ms) / float(n - 1);
        const float max_rate = (dt_ms > 0.0f) ? 0.8f * 500.0f / dt_ms : 1.0e9f;
        const bool resolvable = m.rate_hz > 0.0f && m.rate_hz < max_rate;

        // Periodic only with enough cycles, clear depth, a COHERENT
        // oscillation, a stable rate, and within the measurable band.
        m.periodic = cycles >= 3.0f && m.depth_cents >= 5.0f && resolvable
                  && m.coherence >= 0.5f && m.rate_stable;
        return m;
    }

    /// Judge a measurement against a pack spec (straight-tone aware).
    TechniqueResult check(const VibratoMeasurement& m, const VibratoSpec& spec) const noexcept {
        TechniqueResult r;
        if (spec.expect_straight) {
            // Straight tone: no periodic oscillation, or a very shallow one.
            const bool straight = !m.periodic || m.depth_cents <= spec.straight_tolerance_cents;
            r.ok = straight;
            r.score = straight ? 1.0f : std::max(0.0f, 1.0f - m.depth_cents / 100.0f);
            r.error = m.depth_cents;
            return r;
        }
        if (!m.periodic) return r;

        const bool rate_ok = m.rate_hz >= spec.rate_min_hz && m.rate_hz <= spec.rate_max_hz;
        const bool depth_ok = m.depth_cents >= spec.min_depth_cents
                           && m.depth_cents <= spec.max_depth_cents;
        const bool dur_ok = m.duration_ms >= spec.min_duration_ms;
        r.ok = rate_ok && depth_ok && dur_ok;

        // Score: normalized position inside the accepted bands (1 = centered).
        const float rate_mid = 0.5f * (spec.rate_min_hz + spec.rate_max_hz);
        const float rate_half = 0.5f * (spec.rate_max_hz - spec.rate_min_hz);
        const float rate_score = 1.0f - std::min(1.0f, std::fabs(m.rate_hz - rate_mid) / (rate_half + 1e-6f));
        const float depth_excess = std::max(0.0f, spec.min_depth_cents - m.depth_cents);
        const float depth_score = std::max(0.0f, 1.0f - (depth_excess + std::max(0.0f, m.depth_cents - spec.max_depth_cents)) / (spec.tolerance_cents + spec.min_depth_cents));
        r.score = 0.5f * rate_score + 0.5f * depth_score;
        r.error = depth_excess;
        return r;
    }
};

// ---------------------------------------------------------------------------
// Bend / slide (glissando): monotonic cents curves start -> target.
// ---------------------------------------------------------------------------

enum class CurveShape {
    Linear,   ///< constant slope (slide/glissando)
    EaseOut,  ///< fast start, settling into the target (string bend)
};

struct BendSpec {
    float start_cents = 0.0f;
    float end_cents = 200.0f;
    float duration_ms = 250.0f;
    CurveShape shape = CurveShape::Linear;
    float endpoint_tolerance_cents = 30.0f;
    float duration_tolerance_ms = 120.0f;
    float fit_tolerance_cents = 25.0f; ///< allowed deviation from the shape
    bool require_monotonic = true;
};

struct BendMeasurement {
    bool monotonic = false;
    float start_cents = 0.0f;
    float end_cents = 0.0f;
    float duration_ms = 0.0f;
    float fit_error_cents = 0.0f; ///< RMS deviation from the measured linear ramp
};

class BendAnalyzer {
public:
    BendMeasurement measure(const TrajectoryBuffer& traj) const noexcept {
        BendMeasurement m;
        const size_t n = traj.size();
        if (n < 4) return m;
        const TrajectoryPoint a = traj.point(0);
        const TrajectoryPoint b = traj.point(n - 1);
        m.start_cents = a.cents;
        m.end_cents = b.cents;
        m.duration_ms = b.t_ms - a.t_ms;
        if (m.duration_ms <= 1.0f) return m;

        // Monotonicity with a small noise band (2 cents).
        m.monotonic = true;
        const float span = b.cents - a.cents;
        for (size_t i = 1; i < n; ++i) {
            const float d = traj.point(i).cents - traj.point(i - 1).cents;
            if (span >= 0.0f && d < -2.0f) { m.monotonic = false; break; }
            if (span < 0.0f && d > 2.0f) { m.monotonic = false; break; }
        }

        // RMS deviation from the measured linear ramp (shape-neutral: the
        // spec's shape is scored in check()).
        double acc = 0.0;
        for (size_t i = 0; i < n; ++i) {
            const float x = (traj.point(i).t_ms - a.t_ms) / m.duration_ms;
            const float expected = a.cents + span * x;
            const double d = double(traj.point(i).cents) - double(expected);
            acc += d * d;
        }
        m.fit_error_cents = float(std::sqrt(acc / double(n)));
        return m;
    }

    /// Judge a measurement against a pack spec. `traj` is re-scanned for the
    /// configured shape score (pass the same buffer used for measure()).
    TechniqueResult check(const BendMeasurement& m, const BendSpec& spec,
                          const TrajectoryBuffer& traj = TrajectoryBuffer()) const noexcept {
        TechniqueResult r;
        const bool start_ok = std::fabs(m.start_cents - spec.start_cents)
                              <= spec.endpoint_tolerance_cents;
        const bool end_ok = std::fabs(m.end_cents - spec.end_cents)
                            <= spec.endpoint_tolerance_cents;
        const bool dur_ok = std::fabs(m.duration_ms - spec.duration_ms)
                            <= spec.duration_tolerance_ms;
        const bool mono_ok = !spec.require_monotonic || m.monotonic;
        r.ok = start_ok && end_ok && dur_ok && mono_ok;

        // Shape fit against the expected curve (not just the linear ramp).
        if (!traj.empty()) {
            const TrajectoryPoint a = traj.point(0);
            const float span = spec.end_cents - spec.start_cents;
            const float dur = std::max(1.0f, m.duration_ms);
            double acc = 0.0;
            for (size_t i = 0; i < traj.size(); ++i) {
                float x = (traj.point(i).t_ms - a.t_ms) / dur;
                x = std::clamp(x, 0.0f, 1.0f);
                float shape = x;
                if (spec.shape == CurveShape::EaseOut) shape = 1.0f - (1.0f - x) * (1.0f - x);
                const float expected = spec.start_cents + span * shape;
                const double d = double(traj.point(i).cents) - double(expected);
                acc += d * d;
            }
            const float fit = float(std::sqrt(acc / double(traj.size())));
            r.error = fit;
            r.ok = r.ok && fit <= spec.fit_tolerance_cents;
            r.score = std::max(0.0f, 1.0f - fit / (spec.fit_tolerance_cents + 1e-6f));
        } else {
            r.score = (r.ok ? 1.0f : 0.0f);
            r.error = m.fit_error_cents;
        }
        return r;
    }
};

// ---------------------------------------------------------------------------
// Articulation: staccato / legato / accent / mute via envelope shape.
// ---------------------------------------------------------------------------

enum class Articulation {
    Staccato,  ///< short, quick release
    Legato,    ///< long sustain, smooth
    Accent,    ///< loud onset peak vs sustain
    Mute,      ///< fast decay, little sustain
};

struct EnvelopeSpec {
    Articulation articulation = Articulation::Legato;
    float min_duration_ms = 100.0f;
    float max_duration_ms = 400.0f;
    float max_attack_ms = 30.0f;
    float max_release_ms = 40.0f;
    float min_sustain_ratio = 0.5f; ///< fraction of the note within 6 dB of peak
    float min_accent_peak = 0.3f;   ///< absolute peak for accent/mute levels
    float mute_decay_ms = 120.0f;   ///< peak -> -20 dB for Mute
};

struct EnvelopeMeasurement {
    float duration_ms = 0.0f;   ///< above -40 dBFS-equivalent threshold
    float attack_ms = 0.0f;     ///< 10% -> 90% of peak
    float release_ms = 0.0f;    ///< 90% -> 10% of peak
    float sustain_ratio = 0.0f; ///< fraction of the note within 6 dB of peak
    float peak = 0.0f;
    float decay_to_20db_ms = 0.0f; ///< peak -> -20 dB (mute signature)
};

class EnvelopeAnalyzer {
public:
    /// Measure the shape of an RMS envelope series (monotonic time, any hop).
    EnvelopeMeasurement measure(const float* rms, size_t n, float hop_ms) const noexcept {
        EnvelopeMeasurement m;
        if (n < 4 || hop_ms <= 0.0f) return m;

        size_t peak_i = 0;
        for (size_t i = 0; i < n; ++i) if (rms[i] > m.peak) { m.peak = rms[i]; peak_i = i; }
        if (m.peak <= 0.0f) return m;

        // Duration above -40 dB of peak (noise-bounded activity).
        const float floor_amp = m.peak * 0.01f;
        size_t first = 0, last = n - 1;
        while (first < n && rms[first] < floor_amp) ++first;
        while (last > first && rms[last] < floor_amp) --last;
        m.duration_ms = float(last - first) * hop_ms;

        // Attack: first crossing of 10% to 90% of peak.
        const float lo = m.peak * 0.1f, hi = m.peak * 0.9f;
        size_t a10 = first;
        while (a10 < peak_i && rms[a10] < lo) ++a10;
        size_t a90 = a10;
        while (a90 < peak_i && rms[a90] < hi) ++a90;
        m.attack_ms = float(a90 - a10) * hop_ms;

        // Release: 90% -> 10% after the peak.
        size_t r90 = peak_i;
        while (r90 + 1 < n && rms[r90] > hi) ++r90;
        size_t r10 = r90;
        while (r10 + 1 < n && rms[r10] > lo) ++r10;
        m.release_ms = float(r10 - r90) * hop_ms;

        // Sustain ratio: fraction of active samples within 6 dB of peak.
        const float sustain_floor = m.peak * 0.5f;
        size_t sustained = 0;
        for (size_t i = first; i <= last; ++i) if (rms[i] >= sustain_floor) ++sustained;
        m.sustain_ratio = float(sustained) / float(last - first + 1);

        // Mute signature: peak -> -20 dB decay time.
        const float d20 = m.peak * 0.1f;
        size_t d = peak_i;
        while (d + 1 < n && rms[d] > d20) ++d;
        m.decay_to_20db_ms = float(d - peak_i) * hop_ms;
        return m;
    }

    /// Judge a measurement against a pack spec.
    TechniqueResult check(const EnvelopeMeasurement& m, const EnvelopeSpec& spec) const noexcept {
        TechniqueResult r;
        r.error = 0.0f;
        switch (spec.articulation) {
        case Articulation::Staccato: {
            const bool dur_ok = m.duration_ms >= spec.min_duration_ms
                             && m.duration_ms <= spec.max_duration_ms;
            const bool rel_ok = m.release_ms <= spec.max_release_ms;
            const bool sus_ok = m.sustain_ratio < spec.min_sustain_ratio;
            r.ok = dur_ok && rel_ok && sus_ok;
            r.score = (float(dur_ok) + float(rel_ok) + float(sus_ok)) / 3.0f;
            r.error = std::max(0.0f, m.duration_ms - spec.max_duration_ms);
            break;
        }
        case Articulation::Legato: {
            const bool dur_ok = m.duration_ms >= spec.min_duration_ms;
            const bool sus_ok = m.sustain_ratio >= spec.min_sustain_ratio;
            r.ok = dur_ok && sus_ok;
            r.score = 0.5f * float(dur_ok) + 0.5f * std::min(1.0f, m.sustain_ratio / std::max(0.01f, spec.min_sustain_ratio));
            r.error = std::max(0.0f, spec.min_sustain_ratio - m.sustain_ratio);
            break;
        }
        case Articulation::Accent: {
            // Loud attack peak that clearly exceeds the sustain level.
            const bool peak_ok = m.peak >= spec.min_accent_peak;
            const bool att_ok = m.attack_ms <= spec.max_attack_ms;
            r.ok = peak_ok && att_ok;
            r.score = 0.5f * float(peak_ok) + 0.5f * float(att_ok);
            r.error = std::max(0.0f, spec.max_attack_ms - m.attack_ms);
            break;
        }
        case Articulation::Mute: {
            const bool peak_ok = m.peak >= spec.min_accent_peak;
            const bool dec_ok = m.decay_to_20db_ms <= spec.mute_decay_ms;
            const bool sus_ok = m.sustain_ratio < spec.min_sustain_ratio;
            r.ok = peak_ok && dec_ok && sus_ok;
            r.score = (float(peak_ok) + float(dec_ok) + float(sus_ok)) / 3.0f;
            r.error = m.decay_to_20db_ms;
            break;
        }
        }
        return r;
    }
};

// ---------------------------------------------------------------------------
// Dynamics tiers: how loud the note is played. Measurement only — the game
// decides what a tier means for reward. Two operating modes (SPEC §2):
//   - session-relative (default): tiers track the observed level range and
//     converge as notes accumulate; the first `min_notes` notes report
//     "warming up" instead of a guess;
//   - calibration: the player marks one soft and one loud note
//     (`calibrate(soft, loud)`), fixing the two anchors.
// ---------------------------------------------------------------------------

enum class DynamicsTier : uint8_t {
    WarmingUp, ///< not enough notes seen yet and no calibration anchors
    Pp,        ///< pianissimo — bottom of the range
    P,
    Mp,
    Mf,
    F,
    Ff,        ///< fortissimo — top of the range
};

/// Name of a tier for logs/JSON ("pp" ... "ff", "warming-up").
inline const char* dynamics_tier_name(DynamicsTier t) noexcept {
    switch (t) {
    case DynamicsTier::Pp: return "pp";
    case DynamicsTier::P: return "p";
    case DynamicsTier::Mp: return "mp";
    case DynamicsTier::Mf: return "mf";
    case DynamicsTier::F: return "f";
    case DynamicsTier::Ff: return "ff";
    default: return "warming-up";
    }
}

/// Classifies per-note attack peaks into pp..ff. Allocation-free; the
/// session anchors are plain running min/max of the observed peak levels
/// (dBFS), so the mapping converges and is deterministic given the note
/// sequence (live/offline equivalence).
class DynamicsClassifier {
public:
    struct Config {
        int min_notes = 8;    ///< notes before session tiers are trusted
        float floor_db = -60.0f; ///< levels below this are silence, not "soft"
    };

    DynamicsClassifier() = default;
    explicit DynamicsClassifier(const Config& cfg) noexcept : cfg_(cfg) {}

    /// One note's attack peak (linear amplitude) -> tier. Call once per
    /// note (its attack peak), in note order.
    DynamicsTier observe(float peak_rms) noexcept {
        const float db = (peak_rms > 0.0f) ? 20.0f * std::log10(peak_rms) : cfg_.floor_db;
        if (db >= cfg_.floor_db) {
            if (!have_level_) {
                have_level_ = true;
                lo_db_ = hi_db_ = db;
            } else {
                lo_db_ = std::min(lo_db_, db);
                hi_db_ = std::max(hi_db_, db);
            }
        }
        const DynamicsTier tier = classify(db); // warm-up counts this note
        ++notes_;
        return tier;
    }

    /// Optional calibration: the player's soft and loud reference notes
    /// fix the range ends (no more warming up).
    bool calibrate(float soft_rms, float loud_rms) noexcept {
        if (soft_rms <= 0.0f || loud_rms <= soft_rms) return false;
        lo_db_ = 20.0f * std::log10(soft_rms);
        hi_db_ = 20.0f * std::log10(loud_rms);
        calibrated_ = true;
        have_level_ = true;
        return true;
    }

    void reset() noexcept { *this = DynamicsClassifier(cfg_); }

    bool calibrated() const noexcept { return calibrated_; }
    int notes_seen() const noexcept { return notes_; }
    float soft_db() const noexcept { return lo_db_; }
    float loud_db() const noexcept { return hi_db_; }

private:
    DynamicsTier classify(float db) const noexcept {
        if (!calibrated_ && notes_ < cfg_.min_notes) return DynamicsTier::WarmingUp;
        const float span = hi_db_ - lo_db_;
        if (span <= 1e-3f) return DynamicsTier::Mf; // single-level session
        const float x = std::clamp((db - lo_db_) / span, 0.0f, 1.0f);
        return DynamicsTier(int(DynamicsTier::Pp) + int(std::lround(5.0f * x)));
    }

    Config cfg_;
    float lo_db_ = 0.0f;
    float hi_db_ = 0.0f;
    bool have_level_ = false;
    bool calibrated_ = false;
    int notes_ = 0;
};

// ---------------------------------------------------------------------------
// Extended techniques: spectral signature templates (platform-architecture
// §5 item 5 "sound-trick validation templates"). Slot amplitudes vs a pack
// template: e.g. string harmonic (weak fundamental, strong octave),
// flutter-tongue (broadband slot spread), pedal resonance.
// ---------------------------------------------------------------------------

struct SpectralSignature {
    static constexpr size_t kMaxSlots = 8;
    float slot_hz[kMaxSlots] = {0};
    float weight[kMaxSlots] = {0}; ///< expected relative amplitude per slot
    size_t n_slots = 0;
    float threshold = 0.80f;       ///< cosine similarity for "technique present"
};

struct SpectralMeasurement {
    float similarity = 0.0f;
    float slot_amp[SpectralSignature::kMaxSlots] = {0};
};

class SpectralSignatureValidator {
public:
    /// Set the pack template. Setup path.
    void set_signature(const SpectralSignature& sig) noexcept { sig_ = sig; }

    /// Measure slot amplitudes (single-frequency DFT magnitude, same
    /// primitive as the Goertzel bank) and score cosine similarity against
    /// the template. Allocation-free.
    SpectralMeasurement measure(const float* samples, size_t count,
                                float sample_rate) const noexcept {
        SpectralMeasurement m;
        if (count < 64 || sig_.n_slots == 0) return m;

        double norm_sig = 0.0, norm_obs = 0.0, dot = 0.0;
        for (size_t s = 0; s < sig_.n_slots && s < SpectralSignature::kMaxSlots; ++s) {
            const float amp = dft_magnitude(samples, count, sig_.slot_hz[s], sample_rate);
            m.slot_amp[s] = amp;
            const double w = double(sig_.weight[s]);
            dot += w * double(amp);
            norm_sig += w * w;
            norm_obs += double(amp) * double(amp);
        }
        if (norm_sig > 1e-18 && norm_obs > 1e-18) {
            m.similarity = float(dot / (std::sqrt(norm_sig) * std::sqrt(norm_obs)));
        }
        return m;
    }

    /// Judge a measurement against the template threshold.
    TechniqueResult check(const SpectralMeasurement& m) const noexcept {
        TechniqueResult r;
        r.ok = m.similarity >= sig_.threshold;
        r.score = m.similarity;
        r.error = std::max(0.0f, sig_.threshold - m.similarity);
        return r;
    }

private:
    static float dft_magnitude(const float* x, size_t count, float freq,
                               float sample_rate) noexcept {
        const double w = 2.0 * M_PI * double(freq) / double(sample_rate);
        const double cr = std::cos(w), ci = -std::sin(w);
        double re = 1.0, im = 0.0, xr = 0.0, xi = 0.0;
        for (size_t i = 0; i < count; ++i) {
            const double s = x[i];
            xr += s * re;
            xi += s * im;
            const double nre = re * cr - im * ci;
            im = re * ci + im * cr;
            re = nre;
        }
        return float(2.0 * std::sqrt(xr * xr + xi * xi) / double(count));
    }

    SpectralSignature sig_{};
};

} // namespace kan
