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
/// from pack data. All times in milliseconds, frequencies in Hz. Every
/// instrument-specific behaviour of libkan (a flute, a melodica, a guitar)
/// is expressed through this struct — never through instrument names in
/// code.
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
    /// to the onset timestamp by the judgment layer — a GAME-side consumer;
    /// libkan's detection does not read this field).
    float judgment_offset_ms = 0.0f;

    /// Typical attack ramp duration. Pack data for the game's judgment
    /// layer (slow-attack timing expectations); no libkan consumer yet.
    float attack_ramp_ms = 2.0f;

    /// Maximum number of simultaneously sounding notes the instrument can
    /// produce (player physics, never inferred from audio): wind/bowed 1,
    /// plucked 6 (strings), keyboard 10 (fingers). Caps the
    /// candidate-conditioned per-note description
    /// (docs/architecture/note-description-stream.md §6/§7).
    int max_simultaneous_notes = 6;

    /// Blind mono/polyphonic classification threshold: a second spectral
    /// series must carry at least this fraction of the best series' energy
    /// to count as a sounding note (per-instrument pack data;
    /// note-description-stream.md §2.7/§6).
    float mono_poly_threshold = 0.25f;

    /// Legato pitch changes open note records without an attack (wind /
    /// bowed slurs and transition notes). Strikes (keyboard, plucked) keep
    /// attack-only boundaries. Pack data — a flute and a melodica differ
    /// here only through their profile, never through code.
    bool accepts_legato_boundaries = false;

    /// Cadence of the pitch readings that track a sounding note (ms): the
    /// vibrato trajectory and the legato boundaries are measured on them.
    float reading_interval_ms = 50.0f;

    /// A legato pitch change opens its record once the new pitch has held
    /// this long (ms) — long enough that a vibrato swing (which returns) is
    /// not taken for a step (which stays). Fast slurred passages need it
    /// short: a 16th note at 100 bpm lasts 150 ms.
    float transition_hold_ms = 250.0f;

    /// Articulation by level on one-note-at-a-time instruments (tonguing,
    /// bow changes, breaths): a brief dip of the sound below the release
    /// level that comes back opens a new record at the dip; a longer one
    /// ends the note at its release. Winds articulate this way — often with
    /// no attack transient for the onset detector to find.
    bool rearticulates_by_level = false;

    /// Acceptance band for intonation evaluation (+/- cents around the
    /// written/expected pitch): real instruments record with systematic
    /// detune (measured up to ~50 c on field takes). The game judges the
    /// residual within this band (calibration-aware), never raw A440.
    float intonation_band_cents = 60.0f;

    /// Pitch-refinement search range (+/- cents around the expected pitch).
    /// Estimates that land at the cap are flagged "saturated" and are never
    /// used as pitch facts (measurement-invariants.md I2/I6): a number at
    /// the cap means "ran out of range", not "this detuned". Measured on
    /// MAESTRO batch 01: deep piano bass saturates a 60 c range (ISS-1).
    float pitch_search_cents = 60.0f;

    /// Wider range toward the bass, interpolated smoothly with register
    /// (user decision: no hard breakpoint). Inharmonicity and fundamental
    /// weakness grow *continuously* toward the deep register (stiff piano
    /// strings, low guitar strings), so the search/trust band is a linear
    /// curve between (pitch_search_low_midi, pitch_search_cents_low) and
    /// (pitch_search_high_midi, pitch_search_cents) -- see
    /// pitch_search_cents_at(). Pack data per instrument; for instruments
    /// without a bass problem set both values equal (flat band).
    float pitch_search_cents_low = 120.0f;
    float pitch_search_low_midi = 33.0f;   ///< A1: full wide band at/below
    float pitch_search_high_midi = 57.0f;  ///< A3: full base band at/above

    /// The trust/search band at a given register: linear interpolation
    /// between the two anchor points (flat outside them).
    float pitch_search_cents_at(int midi) const noexcept {
        const float lo = pitch_search_low_midi;
        const float hi = pitch_search_high_midi;
        if (hi <= lo) return pitch_search_cents;
        const float m = float(midi);
        if (m <= lo) return pitch_search_cents_low;
        if (m >= hi) return pitch_search_cents;
        const float x = (m - lo) / (hi - lo);
        return pitch_search_cents_low + x * (pitch_search_cents - pitch_search_cents_low);
    }

    /// Release detection for the note-end measurement: a note ends at its
    /// release -- the terminal drop of end_drop_db below its own sustained
    /// (plateau) level. Peak-relative rules truncate decaying piano notes
    /// (MAESTRO ISS-2) and misread swelling winds; this is envelope-shape
    /// agnostic (piano decays, winds swell: both end in a release), and
    /// only the strength is pack data.
    float end_drop_db = 12.0f;

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
        p.max_simultaneous_notes = 6; // six strings
        p.accepts_legato_boundaries = false;
        p.intonation_band_cents = 45.0f;
        p.pitch_search_cents = 60.0f;
        p.pitch_search_cents_low = 90.0f; // low strings are inharmonic too
        p.pitch_search_low_midi = 28.0f;
        p.pitch_search_high_midi = 52.0f;
        p.end_drop_db = 18.0f;            // plucked decay ends near silence
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
        p.max_simultaneous_notes = 1; // one air column / one bow
        p.accepts_legato_boundaries = true; // slurs / transition notes
        p.reading_interval_ms = 20.0f;     // slurred runs: boundaries to ~20 ms
        p.transition_hold_ms = 80.0f;      // vibrato returns within ~90 ms; steps stay
        p.rearticulates_by_level = true;   // tonguing dips, breaths
        p.intonation_band_cents = 60.0f;
        p.pitch_search_cents = 60.0f;  // no inharmonic bass: flat band
        p.pitch_search_cents_low = 60.0f;
        p.end_drop_db = 10.0f;         // clear breath/bow release
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
        p.max_simultaneous_notes = 10; // ten fingers
        p.accepts_legato_boundaries = false;
        p.intonation_band_cents = 60.0f;
        p.pitch_search_cents = 60.0f;
        p.pitch_search_cents_low = 120.0f; // piano bass saturates 60 c (ISS-1)
        p.pitch_search_low_midi = 33.0f;   // smooth band curve A1..A3
        p.pitch_search_high_midi = 57.0f;
        p.end_drop_db = 12.0f;
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
