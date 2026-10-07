// Offline tests for the per-note description stream (note_stream.hpp) —
// docs/architecture/note-description-stream.md. Known synthesized input ->
// expected facts. The record model: a record is a STRUCK NOTE (an attack
// strikes N notes -> N records, each with its own lifetime); wind/bowed
// profiles also open records on sustained pitch changes (transitions).
//
//   1. mono note with an early wrong-semitone estimate, corrected by the
//      final estimate (provisional is display-only; final is the center of
//      the note)
//   2. chord with all candidates present (3 struck notes -> 3 records,
//      per-candidate presence/cents/amplitude)
//   3. chord with one candidate absent (false-presence bound; the absent
//      note's record ends early and invents nothing)
//   4. sustained LH chord under a new RH melody note (sustaining
//      candidates accumulate facts in their OWN registers)
//   5. an unexplained extra note (UNEXPLAINED_ENERGY, never invented)
//   6. live-vs-offline stream equality (byte-identical JSONL)
//   7. fast passage — every attack opens its own record (125 ms apart)
//   8. monophonic legato strikes — each note ends at the next attack
//   9. legato TRANSITIONS — pitch changes without attacks open records
//  10. vibrato center — the final pitch is the center of the oscillation
#include "libkan/note_stream.hpp"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

namespace {

constexpr float kSr = 48000.0f;
constexpr size_t kBlock = 128;

bool g_pass = true;

void check(bool ok, const char* what) {
    std::printf("  [%s] %s\n", ok ? " ok " : "FAIL", what);
    if (!ok) g_pass = false;
}

// ---------------------------------------------------------------------------
// Synthesis: harmonic notes with a short attack transient (deterministic).
// Realistic excitation: chord notes start spread by a few ms (hammer),
// independent partial phases, ~cents detune, unequal decay. Phase-locked
// simultaneous sums beat coherently and their crests retrigger onset
// detectors — an artifact no real instrument produces (docs/dsp/README.md
// §6.3), so fixtures must not synthesize it.
void add_note(std::vector<float>& sig, float t0, float dur, float freq, float amp,
              uint32_t seed) {
    const float detune_cents = float(int(seed % 5u) - 2) * 1.5f;
    const float f = freq * std::pow(2.0f, detune_cents / 1200.0f);
    const float start_ms = float(seed % 3u) * 6.0f; // hammer spread 0/6/12 ms
    const float decay0 = 1.05f + 0.22f * float(seed % 3u); // per-note decay rate
    const size_t at = size_t((t0 + start_ms / 1000.0f) * kSr);
    const size_t n = size_t(dur * kSr);
    for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
        const float t = float(i) / kSr;
        float s = 0.0f;
        if (t < 0.005f) {
            const uint32_t h = uint32_t(i) * 2654435761u + seed;
            s = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.005f);
        }
        for (int h = 1; h <= 5; ++h) {
            if (f * float(h) > 15000.0f) break;
            // Piano-like decay: higher harmonics die first.
            const float decay = std::exp(-(decay0 + 0.25f * float(h)) * t);
            // Struck strings excite partials with uncorrelated phases.
            const uint32_t ph = seed * 2654435761u + uint32_t(h) * 40503u;
            const float phase_h = float((ph >> 8) % 6283u) / 1000.0f;
            s += (0.5f / float(h)) * decay
               * std::sin(2.0f * float(M_PI) * f * float(h) * t + phase_h);
        }
        sig[at + i] += amp * s;
    }
}

/// Room noise floor (-46 dBFS): real recordings always have one.
void add_noise_floor(std::vector<float>& sig, float amp, uint32_t seed) {
    uint32_t rng = seed;
    for (size_t i = 0; i < sig.size(); ++i) {
        rng = rng * 1664525u + 1013904223u;
        sig[i] += amp * (float(int(rng >> 8 & 0xFFFF)) / 32767.5f - 1.0f);
    }
}

/// Continuous tone with smooth pitch steps and NO attack transients after
/// the first (the legato-transition case: wind players change pitch without
/// re-striking).
void add_legato_steps(std::vector<float>& sig, float t0, const float* freqs,
                      const float* hold_s, int n_steps, float amp) {
    size_t at = size_t(t0 * kSr);
    for (int s = 0; s < n_steps; ++s) {
        const size_t n = size_t(hold_s[s] * kSr);
        for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
            const float t = float(i) / kSr;
            float v = 0.0f;
            if (s == 0 && t < 0.025f) { // breathy attack on the first step only
                const uint32_t h = uint32_t(i) * 2654435761u + 7u;
                v = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
                  * (1.0f - t / 0.025f);
            }
            const float ramp = (s == 0 || t > 0.02f) ? 1.0f : t / 0.02f; // 20 ms glide
            for (int h = 1; h <= 4; ++h) {
                v += ramp * (0.5f / float(h)) * std::exp(-0.35f * t)
                   * std::sin(2.0f * float(M_PI) * freqs[s] * float(h) * t);
            }
            sig[at + i] += amp * v;
        }
        at += n;
    }
}

/// Wind-style note with vibrato centered at `center_hz` (phase-integrated
/// FM — the t*vib(t) shortcut injects a huge spurious FM term).
void add_vibrato_note(std::vector<float>& sig, float t0, float dur, float center_hz,
                      float depth_c, float rate_hz, float amp) {
    const size_t at = size_t(t0 * kSr);
    const size_t n = size_t(dur * kSr);
    double phase = 0.0;
    for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
        const float t = float(i) / kSr;
        float v = 0.0f;
        if (t < 0.025f) {
            const uint32_t h = uint32_t(i) * 2654435761u + 11u;
            v = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.025f);
        }
        const float mod = (t > 0.15f)
            ? std::pow(2.0f, (depth_c / 1200.0f)
                     * std::sin(2.0f * float(M_PI) * rate_hz * (t - 0.15f)))
            : 1.0f;
        phase += 2.0 * M_PI * double(center_hz) * double(mod) / kSr;
        v += 0.5f * std::exp(-0.25f * t) * (std::sin(float(phase))
             + 0.2f * std::sin(float(2.0 * phase)));
        sig[at + i] += amp * v;
    }
}

// ---------------------------------------------------------------------------
// Stream harness: canonical consumer loop over fixed-size blocks.
struct Harness {
    std::vector<kan::NoteEvent> events;
    std::string jsonl;

    void drain(kan::NoteEventStream& stream) {
        kan::NoteEvent batch[64];
        for (;;) {
            const size_t got = stream.pop(batch, 64);
            if (got == 0) break;
            for (size_t i = 0; i < got; ++i) {
                events.push_back(batch[i]);
                char buf[512];
                const size_t n = kan::note_event_jsonl(batch[i], buf, sizeof(buf));
                if (n > 0) jsonl.append(buf, n);
            }
        }
    }
};

size_t count_kind(const Harness& h, kan::NoteKind kind);

void run(const std::vector<float>& sig, const kan::InstrumentProfile& profile,
         const std::vector<kan::NoteDescriber::CandidateSet>& cands,
         size_t inject_modulo, Harness& out) {
    kan::NoteDescriber desc(kSr, profile);
    kan::NoteEventStream stream(4096);
    desc.subscribe(stream);
    std::vector<bool> injected(cands.size(), false);
    size_t block_idx = 0;
    for (size_t off = 0; off + kBlock <= sig.size(); off += kBlock, ++block_idx) {
        desc.process_block(&sig[off], kBlock);
        if (block_idx % inject_modulo == 0) {
            // "Next attack" sets are consumed strictly in order (each is
            // eaten by one attack): inject set c only once c attacks have
            // already opened records.
            const size_t hits = count_kind(out, kan::NoteKind::Hit);
            for (size_t c = 0; c < cands.size(); ++c) {
                if (injected[c] || c > hits) continue;
                if (desc.set_candidates(cands[c])) injected[c] = true;
                break;
            }
        }
        while (desc.finalize_due()) {}
        out.drain(stream);
    }
    desc.flush();
    out.drain(stream);
}

size_t count_kind(const Harness& h, kan::NoteKind kind) {
    size_t n = 0;
    for (const auto& e : h.events) if (e.kind == kind) ++n;
    return n;
}


size_t count_transitions(const Harness& h) {
    size_t n = 0;
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Hit && (e.flags & kan::kFlagTransition) != 0) ++n;
    }
    return n;
}

const kan::NoteEvent* find_pitch(const Harness& h, uint32_t note_id,
                                 kan::PitchStage stage) {
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Pitch && e.note_id == note_id && e.stage == stage) {
            return &e;
        }
    }
    return nullptr;
}

/// Candidate facts about a hypothesis (`note_id` = the record the candidate
/// belongs to — its own register). `last` selects the final fact (emitted at
/// the record's close) instead of the first measurement.
const kan::NoteEvent* find_cand(const Harness& h, uint32_t note_id, float midi,
                                bool last = false) {
    const kan::NoteEvent* found = nullptr;
    for (const auto& e : h.events) {
        if (e.kind != kan::NoteKind::Content || e.note_id != note_id) continue;
        if (e.klass == kan::ContentClass::Unknown && e.u.candidate.midi == midi) {
            found = &e;
            if (!last) return &e;
        }
    }
    return found;
}

const kan::NoteEvent* find_close(const Harness& h, uint32_t note_id) {
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Close && e.note_id == note_id) return &e;
    }
    return nullptr;
}

kan::NoteDescriber::CandidateEntry entry(uint32_t owner, float midi, bool struck) {
    kan::NoteDescriber::CandidateEntry e;
    e.cand_note_id = owner;
    e.midi = midi;
    e.struck = struck ? 1 : 0;
    return e;
}

kan::InstrumentProfile piano_profile() {
    kan::InstrumentProfile p = kan::InstrumentProfile::keyboard();
    p.max_simultaneous_notes = 10; // ten fingers (decision 5)
    return p;
}

kan::InstrumentProfile mono_profile() {
    kan::InstrumentProfile p = kan::InstrumentProfile::wind_bowed();
    p.max_simultaneous_notes = 1; // one air column / one bow
    return p;
}

// ---------------------------------------------------------------------------
void fixture_1_mono_corrected() {
    std::printf("fixture 1: mono note, early wrong-semitone estimate corrected later\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    // G#4 for the first 44 ms of the note, then A4 — the provisional window
    // sees the wrong semitone; the final (center of the note) is A4.
    const size_t at = size_t(0.3f * kSr);
    for (size_t i = 0; at + i < sig.size() && i < size_t(1.1f * kSr); ++i) {
        const float t = float(i) / kSr;
        const float f = (t < 0.044f) ? 415.30f : 440.0f;
        float s = 0.0f;
        if (t < 0.005f) {
            const uint32_t h = uint32_t(i) * 2654435761u + 7u;
            s = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.005f);
        }
        for (int h = 1; h <= 5; ++h) {
            s += (0.5f / float(h)) * std::exp(-(1.0f + 0.25f * float(h)) * t)
               * std::sin(2.0f * float(M_PI) * f * float(h) * t);
        }
        sig[at + i] += 0.5f * s;
    }
    add_noise_floor(sig, 0.005f, 111u);

    Harness h;
    run(sig, piano_profile(), {}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 1, "exactly one HIT (onset never revised)");
    const kan::NoteEvent* prov = find_pitch(h, 1, kan::PitchStage::Provisional);
    const kan::NoteEvent* fin = find_pitch(h, 1, kan::PitchStage::Final);
    check(prov != nullptr, "provisional pitch emitted");
    check(fin != nullptr, "final pitch emitted (not unresolved)");
    if (prov != nullptr && fin != nullptr) {
        check(std::fabs(prov->u.pitch.midi - 68.0f) < 0.51f,
              "provisional saw the wrong semitone (G#4)");
        check(std::fabs(fin->u.pitch.midi - 69.0f) < 0.51f, "final corrected to A4");
        const float want_hz = 440.0f * std::pow(2.0f, fin->u.pitch.cents / 1200.0f);
        check(std::fabs(fin->u.pitch.hz - want_hz) < 0.5f,
              "Hz derived from the (midi, cents) estimate");
        check(fin->t_ms > prov->t_ms, "facts arrive in evidence order");
    }
}

// ---------------------------------------------------------------------------
void fixture_2_chord_all_present() {
    std::printf("fixture 2: chord with all candidates present (3 struck notes)\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.1f, 261.63f, 0.30f, 11u); // C4
    add_note(sig, 0.3f, 1.1f, 329.63f, 0.23f, 12u); // E4
    add_note(sig, 0.3f, 1.1f, 392.00f, 0.27f, 13u); // G4
    add_noise_floor(sig, 0.005f, 112u);

    kan::NoteDescriber::CandidateSet cs;
    cs.note_id = 0;
    cs.count = 3;
    cs.entries[0] = entry(0, 60.0f, true);
    cs.entries[1] = entry(0, 64.0f, true);
    cs.entries[2] = entry(0, 67.0f, true);

    Harness h;
    run(sig, piano_profile(), {cs}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 3,
          "three struck notes -> three records (attack x notes)");
    bool same_group = true;
    uint32_t g = 0;
    for (const auto& e : h.events) {
        if (e.kind != kan::NoteKind::Hit) continue;
        if (g == 0) g = e.u.hit.strike_group;
        else if (e.u.hit.strike_group != g) same_group = false;
    }
    check(same_group, "the three records share one strike group (one attack)");

    const kan::NoteEvent* cls = nullptr;
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Content && e.klass != kan::ContentClass::Unknown) {
            cls = &e;
        }
    }
    check(cls != nullptr && cls->klass == kan::ContentClass::Polyphonic,
          "CONTENT class says polyphonic");
    if (cls == nullptr || cls->klass != kan::ContentClass::Polyphonic) {
        for (const auto& e : h.events) {
            char buf[512];
            if (kan::note_event_jsonl(e, buf, sizeof(buf)) > 0) std::fputs(buf, stdout);
        }
    }

    bool all_present = true, cents_ok = true, amp_ok = true;
    float sim = 0.0f;
    for (size_t k = 0; k < 3; ++k) {
        const float midi = float(60 + 4 * int(k));
        const kan::NoteEvent* c = find_cand(h, uint32_t(k + 1), midi == 67.0f ? 67.0f : midi);
        const kan::NoteEvent* real = (k == 0) ? find_cand(h, 1, 60.0f)
                                   : (k == 1) ? find_cand(h, 2, 64.0f)
                                              : find_cand(h, 3, 67.0f);
        (void)c;
        if (real == nullptr) { all_present = false; continue; }
        if (real->u.candidate.presence < 0.08f) all_present = false;
        if (std::fabs(real->u.candidate.cents) > 15.0f) cents_ok = false;
        if (real->u.candidate.amplitude <= 0.0f) amp_ok = false;
        sim = real->u.candidate.similarity;
    }
    check(all_present, "every struck note measured with presence >= 0.08");
    check(cents_ok, "per-note refined cents within 15 c of the hypothesis");
    check(amp_ok, "per-note fitted amplitude > 0");
    check(sim >= 0.80f, "the joint set explains the window (similarity >= 0.8)");

    bool finals_ok = true;
    for (uint32_t id = 1; id <= 3; ++id) {
        const kan::NoteEvent* f = find_pitch(h, id, kan::PitchStage::Final);
        if (f == nullptr || f->u.pitch.hz <= 0.0f) finals_ok = false;
    }
    check(finals_ok, "each struck note has its own final estimate");
}

// ---------------------------------------------------------------------------
void fixture_3_chord_one_absent() {
    std::printf("fixture 3: chord with one candidate absent\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.1f, 261.63f, 0.27f, 21u); // C4
    add_note(sig, 0.3f, 1.1f, 329.63f, 0.30f, 22u); // E4
    add_note(sig, 0.3f, 1.1f, 392.00f, 0.23f, 23u); // G4
    add_noise_floor(sig, 0.005f, 222u);

    kan::NoteDescriber::CandidateSet cs;
    cs.note_id = 0;
    cs.count = 4;
    cs.entries[0] = entry(0, 60.0f, true);
    cs.entries[1] = entry(0, 64.0f, true);
    cs.entries[2] = entry(0, 67.0f, true);
    cs.entries[3] = entry(0, 69.0f, true); // A4: not sounding

    Harness h;
    run(sig, piano_profile(), {cs}, 1, h);

    const kan::NoteEvent* absent = find_cand(h, 4, 69.0f, true);
    check(absent != nullptr, "absent hypothesis is measured in its own record");
    if (absent != nullptr) {
        check(absent->u.candidate.presence <= 0.10f,
              "absent: final presence <= 0.10 (joint fit, 85 ms window)");
        const kan::NoteEvent* fin = find_pitch(h, 4, kan::PitchStage::Final);
        const kan::NoteEvent* unres = find_pitch(h, 4, kan::PitchStage::Unresolved);
        check((fin == nullptr || fin->u.pitch.hz <= 0.0f) && unres != nullptr,
              "absent note: no invented final pitch (honest unresolved)");
    }
    for (uint32_t id = 1; id <= 3; ++id) {
        const kan::NoteEvent* c = (id == 1) ? find_cand(h, 1, 60.0f, true)
                                : (id == 2) ? find_cand(h, 2, 64.0f, true)
                                            : find_cand(h, 3, 67.0f, true);
        check(c != nullptr && absent != nullptr
                  && c->u.candidate.amplitude > 3.0f * absent->u.candidate.amplitude,
              "sounding notes out-amplitude the absent one (>3x)");
    }
    const kan::NoteEvent* c4 = find_close(h, 4);
    const kan::NoteEvent* c1 = find_close(h, 1);
    check(c4 != nullptr && c1 != nullptr && c4->u.close.end_ms <= c1->u.close.end_ms,
          "the absent note's record ends early (own lifetime)");
}

// ---------------------------------------------------------------------------
void fixture_4_sustained_under_new_note() {
    std::printf("fixture 4: sustained LH chord under a new RH melody note\n");
    std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.5f, 130.81f, 0.30f, 31u); // C3 (LH chord, sustains)
    add_note(sig, 0.3f, 1.5f, 164.81f, 0.23f, 32u); // E3
    add_note(sig, 0.3f, 1.5f, 196.00f, 0.27f, 33u); // G3
    add_noise_floor(sig, 0.005f, 333u);
    // A4 (69), not G4: G4's fundamental is G3's 2nd harmonic (a collision
    // that splits the LS energy arbitrarily — fixtures stay measurable).
    add_note(sig, 0.9f, 0.6f, 440.00f, 0.60f, 34u); // A4 (RH melody)

    kan::NoteDescriber::CandidateSet chord; // strike group 1 (records 1..3)
    chord.note_id = 0;
    chord.count = 3;
    chord.entries[0] = entry(0, 48.0f, true);
    chord.entries[1] = entry(0, 52.0f, true);
    chord.entries[2] = entry(0, 55.0f, true);

    // Melody attack (strike group 4): the new note struck + the chord notes
    // still sustaining from the earlier attack (their OWN records 1..3).
    kan::NoteDescriber::CandidateSet melody;
    melody.note_id = 0;
    melody.count = 4;
    melody.entries[0] = entry(0, 69.0f, true);
    melody.entries[1] = entry(1, 48.0f, false);
    melody.entries[2] = entry(2, 52.0f, false);
    melody.entries[3] = entry(3, 55.0f, false);

    Harness h;
    run(sig, piano_profile(), {chord, melody}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 4,
          "chord (3 struck) + melody (1 struck) -> 4 records");
    const kan::NoteEvent* m = find_cand(h, 4, 69.0f);
    check(m != nullptr, "melody measured in its own record");
    // The sustaining notes' facts from the MELODY attack (strike group 4)
    // live in the notes' own registers (note_id 1..3).
    const kan::NoteEvent* s1 = nullptr;
    size_t sustain_facts = 0;
    for (const auto& e : h.events) {
        if (e.kind != kan::NoteKind::Content || e.klass != kan::ContentClass::Unknown) {
            continue;
        }
        if (e.u.candidate.strike_group != 4) continue;
        ++sustain_facts;
        if (e.note_id == 1 && e.u.candidate.midi == 48.0f) s1 = &e;
    }
    check(sustain_facts >= 4,
          "melody attack measured its 4 candidates (1 struck + 3 sustaining)");
    check(s1 != nullptr,
          "sustaining chord facts live in their OWN registers (note_id 1..3)");
    const kan::NoteEvent* m2 = nullptr;
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Content && e.klass == kan::ContentClass::Unknown
            && e.note_id == 4 && e.u.candidate.midi == 69.0f) {
            m2 = &e;
        }
    }
    if (m2 != nullptr && s1 != nullptr) {
        check(m2->u.candidate.presence > s1->u.candidate.presence,
              "melody dominates the window it attacks");
    }
    const kan::NoteEvent* fin4 = find_pitch(h, 4, kan::PitchStage::Final);
    check(fin4 != nullptr && fin4->u.pitch.hz > 0.0f
              && std::fabs(fin4->u.pitch.midi - 69.0f) < 0.51f,
          "melody final estimate = A4");
}

// ---------------------------------------------------------------------------
void fixture_5_unexplained_extra() {
    std::printf("fixture 5: unexplained extra note (never invented)\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.1f, 261.63f, 0.25f, 41u); // C4
    add_note(sig, 0.3f, 1.1f, 329.63f, 0.30f, 42u); // E4
    add_note(sig, 0.3f, 1.1f, 392.00f, 0.23f, 43u); // G4: NOT hypothesized
    add_noise_floor(sig, 0.005f, 444u);

    kan::NoteDescriber::CandidateSet cs;
    cs.note_id = 0;
    cs.count = 2;
    cs.entries[0] = entry(0, 60.0f, true);
    cs.entries[1] = entry(0, 64.0f, true);

    Harness h;
    run(sig, piano_profile(), {cs}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 2, "two hypotheses -> two records");
    bool found_67 = false, flagged = false;
    float sim = 1.0f;
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Content && e.klass == kan::ContentClass::Unknown
            && e.u.candidate.midi == 67.0f) {
            found_67 = true;
        }
        if ((e.flags & kan::kFlagUnexplainedEnergy) != 0) flagged = true;
        if (e.kind == kan::NoteKind::Content && e.klass == kan::ContentClass::Unknown) {
            sim = e.u.candidate.similarity;
        }
    }
    check(!found_67, "no fact invented for the un-hypothesized note");
    check(flagged, "UNEXPLAINED_ENERGY flagged on the notes' facts");
    check(sim < 0.80f, "similarity reports the unexplained energy");
    for (uint32_t id = 1; id <= 2; ++id) {
        const kan::NoteEvent* fin = find_pitch(h, id, kan::PitchStage::Final);
        check(fin == nullptr || fin->u.pitch.hz <= 0.0f,
              "no confident final pitch from an incomplete candidate set");
    }
}

// ---------------------------------------------------------------------------
void fixture_6_live_vs_offline() {
    std::printf("fixture 6: live-vs-offline stream equality (byte-identical JSONL)\n");
    std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.5f, 130.81f, 0.30f, 51u);
    add_note(sig, 0.3f, 1.5f, 196.00f, 0.25f, 53u);
    add_noise_floor(sig, 0.005f, 555u);
    add_note(sig, 0.9f, 0.6f, 392.00f, 0.60f, 54u);

    kan::NoteDescriber::CandidateSet chord;
    chord.note_id = 0;
    chord.count = 2;
    chord.entries[0] = entry(0, 48.0f, true);
    chord.entries[1] = entry(0, 55.0f, true);
    kan::NoteDescriber::CandidateSet melody;
    melody.note_id = 0;
    melody.count = 2;
    melody.entries[0] = entry(0, 69.0f, true);
    melody.entries[1] = entry(1, 48.0f, false);
    const std::vector<kan::NoteDescriber::CandidateSet> cands = {chord, melody};

    Harness offline, live, offline2;
    run(sig, piano_profile(), cands, 1, offline);   // candidates ASAP
    run(sig, piano_profile(), cands, 7, live);      // late injection, same blocks
    run(sig, piano_profile(), cands, 1, offline2);  // determinism

    check(!offline.jsonl.empty(), "offline run produced events");
    check(offline.jsonl == live.jsonl,
          "late candidate injection + drain batching -> byte-identical stream");
    check(offline.jsonl == offline2.jsonl, "two identical runs -> byte-identical stream");
}

// ---------------------------------------------------------------------------
void fixture_7_fast_passage() {
    std::printf("fixture 7: fast passage — every attack opens its own record\n");
    std::vector<float> sig(size_t(1.2f * kSr), 0.0f);
    add_note(sig, 0.30f, 0.4f, 440.00f, 0.5f, 61u);
    add_note(sig, 0.425f, 0.4f, 493.88f, 0.5f, 62u);
    add_note(sig, 0.55f, 0.4f, 554.37f, 0.5f, 63u);
    add_noise_floor(sig, 0.005f, 666u);

    Harness h;
    run(sig, piano_profile(), {}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 3,
          "three attacks 125 ms apart -> three HITs");
    check(count_kind(h, kan::NoteKind::Close) == 3, "three records closed");
}

// ---------------------------------------------------------------------------
void fixture_8_mono_legato_strikes() {
    std::printf("fixture 8: monophonic strikes — each note ends at the next attack\n");
    std::vector<float> sig(size_t(1.8f * kSr), 0.0f);
    add_note(sig, 0.30f, 0.45f, 440.00f, 0.5f, 71u); // A4
    add_note(sig, 0.70f, 0.45f, 493.88f, 0.5f, 72u); // B4
    add_note(sig, 1.10f, 0.45f, 523.25f, 0.5f, 73u); // C5
    add_noise_floor(sig, 0.005f, 777u);

    Harness h;
    run(sig, mono_profile(), {}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 3, "three attacks -> three HITs");
    const kan::NoteEvent* c1 = find_close(h, 1);
    const kan::NoteEvent* c2 = find_close(h, 2);
    const kan::NoteEvent* f2 = find_pitch(h, 2, kan::PitchStage::Final);
    (void)f2;
    check(c1 != nullptr && std::fabs(c1->u.close.end_ms - 700.0f) < 60.0f,
          "note 1 ends at the next attack (~700 ms), not at the mix decay");
    check(c2 != nullptr && std::fabs(c2->u.close.end_ms - 1100.0f) < 60.0f,
          "note 2 ends at the next attack (~1100 ms)");
    if (c1 != nullptr) {
        check(c1->u.close.duration_ms > 300.0f && c1->u.close.duration_ms < 500.0f,
              "note 1 duration is its own (~400 ms)");
    }
}

// ---------------------------------------------------------------------------
void fixture_9_legato_transitions() {
    std::printf("fixture 9: legato transitions — pitch changes without attacks\n");
    std::vector<float> sig(size_t(2.6f * kSr), 0.0f);
    const float freqs[3] = {440.00f, 493.88f, 523.25f}; // A4 -> B4 -> C5
    const float holds[3] = {0.7f, 0.7f, 0.7f};
    add_legato_steps(sig, 0.3f, freqs, holds, 3, 0.5f);
    add_noise_floor(sig, 0.005f, 888u);

    Harness h;
    run(sig, mono_profile(), {}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit) == 3,
          "two transitioned notes + one strike -> three records");
    check(count_transitions(h) == 2, "two records opened by legato transitions");
    const kan::NoteEvent* c1 = find_close(h, 1);
    check(c1 != nullptr && std::fabs(c1->u.close.end_ms - 1000.0f) < 120.0f,
          "record 1 ends at its own transition (~1000 ms)");
    const kan::NoteEvent* f3 = find_pitch(h, 3, kan::PitchStage::Final);
    check(f3 != nullptr && f3->u.pitch.hz > 0.0f
              && std::fabs(f3->u.pitch.midi - 72.0f) < 1.51f,
          "transitioned record 3 reports its own pitch (C5)");

}

// ---------------------------------------------------------------------------
void fixture_10_vibrato_center() {
    std::printf("fixture 10: vibrato center — final pitch is the CENTER\n");
    std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
    // Center = A4 + 10 c, depth 38 c at 5 Hz (musical vibrato).
    add_vibrato_note(sig, 0.3f, 1.5f, 440.0f * std::pow(2.0f, 10.0f / 1200.0f),
                     38.0f, 5.0f, 0.5f);
    add_noise_floor(sig, 0.005f, 999u);

    Harness h;
    run(sig, mono_profile(), {}, 1, h);

    const kan::NoteEvent* fin = find_pitch(h, 1, kan::PitchStage::Final);
    check(fin != nullptr && fin->u.pitch.hz > 0.0f, "final estimate emitted");
    if (fin != nullptr && fin->u.pitch.hz > 0.0f) {
        check(std::fabs(fin->u.pitch.cents - 10.0f) < 15.0f,
              "final cents track the vibrato CENTER (+10 c), not its start");
        check(fin->u.pitch.midi == 69.0f, "final midi = A4");
    }
    const kan::NoteEvent* expr = nullptr;
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Expression && e.note_id == 1) expr = &e;
    }
    check(expr != nullptr && expr->u.expression.vib_rate_hz > 3.0f
              && expr->u.expression.vib_rate_hz < 7.0f,
          "vibrato rate claimed from the stable trajectory (~5 Hz)");
    check(count_transitions(h) == 0, "vibrato does NOT open transition records");
}

} // namespace

int main() {
    std::printf("=== libkan note_stream_test ===\n");
    fixture_1_mono_corrected();
    fixture_2_chord_all_present();
    fixture_3_chord_one_absent();
    fixture_4_sustained_under_new_note();
    fixture_5_unexplained_extra();
    fixture_6_live_vs_offline();
    fixture_7_fast_passage();
    fixture_8_mono_legato_strikes();
    fixture_9_legato_transitions();
    fixture_10_vibrato_center();
    std::printf("%s\n", g_pass ? "ALL PASS" : "FAILURES");
    return g_pass ? 0 : 1;
}
