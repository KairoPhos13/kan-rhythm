#pragma once
#include <cmath>

namespace kan {

/// Instrument family, mirroring the instrument-pack profile schema
/// (docs/architecture/platform-architecture.md §5; family enum matches the
/// server data model's instrument_packs.family).
enum class InstrumentFamily {
    Plucked,     ///< guitar, bass, harp, pizzicato, ukulele...
    Bowed,       ///< violin, viola, cello...
    Wind,        ///< flute, recorder, clarinet, brass...
    Keyboard,    ///< piano, electric piano, organ...
    Percussion,  ///< melodic percussion (xylophone, glockenspiel...)
    Other,
};

/// Attack shape of the instrument's notes.
enum class AttackType {
    Fast,    ///< plucked / hammered: full amplitude in < 5 ms
    Medium,  ///< keyboard with soft action, some percussion
    Slow,    ///< wind / bowed: 20-40 ms air/bow ramp
};

/// Per-instrument detection profile (SPECIFICATION.md §2 stage 2 + §3).
///
/// Plain data only — no format coupling. Instrument packs (charts/spec)
/// will serialize exactly these fields; apps construct them directly or
/// from pack data. All times in milliseconds, frequencies in Hz.
struct InstrumentProfile {
    InstrumentFamily family = InstrumentFamily::Other;
    AttackType attack_type = AttackType::Fast;

    /// Expected fundamental range; bounds the pitch search (faster + fewer
    /// octave errors). Defaults cover the full detector range.
    float min_freq_hz = 60.0f;
    float max_freq_hz = 1200.0f;

    /// Stage-2 transient blanking: pitch evaluation is frozen for this long
    /// after an onset, because attack transients are inharmonic noise that
    /// fools pitch algorithms (8 ms plucked / 15 ms wind-bowed per spec).
    float blanking_ms = 8.0f;

    /// Judgment-window offset: expected latency between the physical note
    /// start and the point where the HFC onset detector fires. Fast attacks
    /// fire ~0 ms; slow air/bow ramps fire late, so rhythmic judgment for
    /// those instruments evaluates the ramp start (negative offset applied
    /// to the onset timestamp by the judgment layer).
    float judgment_offset_ms = 0.0f;

    /// Typical attack ramp duration (informational; used by envelope-shape
    /// technique validators and slow-attack judgment).
    float attack_ramp_ms = 2.0f;

    // ---- Presets -----------------------------------------------------------

    /// Plucked strings (guitar, bass, harp): fast attack, 8 ms blanking.
    static InstrumentProfile plucked() noexcept {
        InstrumentProfile p;
        p.family = InstrumentFamily::Plucked;
        p.attack_type = AttackType::Fast;
        p.min_freq_hz = 80.0f;    // E2 (bass guitar / guitar low E ~82 Hz)
        p.max_freq_hz = 1400.0f;  // above E6, covers bends + harmonics
        p.blanking_ms = 8.0f;
        p.judgment_offset_ms = 0.0f;
        p.attack_ramp_ms = 2.0f;
        return p;
    }

    /// Wind and bowed instruments (flute, recorder, violin, brass): slow
    /// air/bow ramp, 15 ms blanking, judgment pulled back to ramp start.
    static InstrumentProfile wind_bowed() noexcept {
        InstrumentProfile p;
        p.family = InstrumentFamily::Wind;
        p.attack_type = AttackType::Slow;
        p.min_freq_hz = 190.0f;   // G3 (violin low G ~196 Hz)
        p.max_freq_hz = 2700.0f;  // above E7 (violin/vocal extremes)
        p.blanking_ms = 15.0f;
        p.judgment_offset_ms = -20.0f; // judge the air/bow ramp start
        p.attack_ramp_ms = 30.0f;
        return p;
    }

    /// Keyboard (piano, e-piano): hammered fast attack with a short
    /// inharmonic strike, 6 ms blanking.
    static InstrumentProfile keyboard() noexcept {
        InstrumentProfile p;
        p.family = InstrumentFamily::Keyboard;
        p.attack_type = AttackType::Medium;
        p.min_freq_hz = 27.0f;    // A0 (piano low A ~27.5 Hz)
        p.max_freq_hz = 4300.0f;  // C8 (~4186 Hz)
        p.blanking_ms = 6.0f;
        p.judgment_offset_ms = 0.0f;
        p.attack_ramp_ms = 4.0f;
        return p;
    }

    /// Profile by name for CLI/UI selection. Returns false on unknown name.
    static bool from_name(const char* name, InstrumentProfile& out) noexcept {
        // Tiny hand-rolled compare to keep this header dependency-free.
        auto eq = [](const char* a, const char* b) {
            for (; *a && *b; ++a, ++b) if (*a != *b) return false;
            return *a == *b;
        };
        if (eq(name, "plucked"))       { out = plucked();    return true; }
        if (eq(name, "wind") || eq(name, "bowed") || eq(name, "wind_bowed")) {
            out = wind_bowed();
            return true;
        }
        if (eq(name, "keyboard") || eq(name, "piano")) {
            out = keyboard();
            return true;
        }
        return false;
    }
};

} // namespace kan
