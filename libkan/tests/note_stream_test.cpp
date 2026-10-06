// Offline tests for the per-note description stream (note_stream.hpp) —
// docs/architecture/note-description-stream.md. Known synthesized input ->
// expected facts, one fixture per designed behavior:
//   1. mono note with an early wrong-semitone estimate, corrected by the
//      final estimate (provisional is display-only, final is the one
//      consistent estimate)
//   2. chord with all candidates present (per-candidate presence/cents)
//   3. chord with one candidate absent (false-presence bound)
//   4. sustained LH chord under a new RH melody note (candidates referencing
//      an earlier note's record)
//   5. an unexplained extra note (UNEXPLAINED_ENERGY, never invented)
//   6. live-vs-offline stream equality (byte-identical JSONL; candidate
//      timing and drain batching must not change the stream)
#include "libkan/note_stream.hpp"

#include <cmath>
#include <cstdio>
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
void add_note(std::vector<float>& sig, float t0, float dur, float freq, float amp,
              uint32_t seed) {
    const size_t at = size_t(t0 * kSr);
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
            if (freq * float(h) > 15000.0f) break;
            // Piano-like decay: higher harmonics die first (without it the
            // equal-rate decay keeps HF beating alive and beat crests can
            // retrigger the onset detector — docs/dsp/README.md limitation).
            const float decay = std::exp(-(1.2f + 0.25f * float(h)) * t);
            s += (0.5f / float(h)) * decay
               * std::sin(2.0f * float(M_PI) * freq * float(h) * t);
        }
        sig[at + i] += amp * s;
    }
}

// Fixture 1 signal: one attack that starts a semitone flat and corrects.
void add_correcting_note(std::vector<float>& sig, float t0, float f_wrong,
                         float wrong_ms, float f_right, float dur,
                         float amp_wrong, float amp_right) {
    const size_t at = size_t(t0 * kSr);
    const size_t nw = size_t(wrong_ms / 1000.0f * kSr);
    const size_t n = size_t(dur * kSr);
    for (size_t i = 0; at + i < sig.size() && i < n; ++i) {
        const float t = float(i) / kSr;
        const float f = (i < nw) ? f_wrong : f_right;
        const float amp = (i < nw) ? amp_wrong : amp_right;
        float s = 0.0f;
        if (t < 0.005f) {
            const uint32_t h = uint32_t(i) * 2654435761u + 7u;
            s = 0.25f * (float(int((h ^ (h >> 13)) % 2000)) / 1000.0f - 1.0f)
              * (1.0f - t / 0.005f);
        }
        for (int h = 1; h <= 5; ++h) {
            const float decay = std::exp(-(1.0f + 0.25f * float(h)) * t);
            s += (0.5f / float(h)) * decay
               * std::sin(2.0f * float(M_PI) * f * float(h) * t);
        }
        sig[at + i] += amp * s;
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
            for (size_t c = 0; c < cands.size(); ++c) {
                if (injected[c]) continue;
                if (desc.set_candidates(cands[c])) injected[c] = true;
            }
        }
        while (desc.finalize_due()) {}
        out.drain(stream);
    }
    desc.flush();
    out.drain(stream);
}

size_t count_kind(const Harness& h, kan::NoteKind kind, uint32_t note_id) {
    size_t n = 0;
    for (const auto& e : h.events) {
        if (e.kind == kind && (note_id == 0 || e.note_id == note_id)) ++n;
    }
    return n;
}

const kan::NoteEvent* find(const Harness& h, kan::NoteKind kind, uint32_t note_id,
                           kan::PitchStage stage) {
    for (const auto& e : h.events) {
        if (e.kind != kind || e.note_id != note_id) continue;
        if (kind == kan::NoteKind::Pitch && e.stage != stage) continue;
        return &e;
    }
    return nullptr;
}

const kan::NoteEvent* find_cand(const Harness& h, uint32_t note_id, float midi) {
    for (const auto& e : h.events) {
        if (e.kind != kan::NoteKind::Content || e.note_id != note_id) continue;
        // Class events carry klass != Unknown; candidate events carry the midi.
        if (e.klass == kan::ContentClass::Unknown && e.u.candidate.midi == midi) return &e;
    }
    return nullptr;
}

constexpr bool kDump = false;

void dump(const Harness& h) {
    if (!kDump) return;
    for (const auto& e : h.events) {
        char buf[512];
        if (kan::note_event_jsonl(e, buf, sizeof(buf)) > 0) std::fputs(buf, stdout);
    }
}

kan::InstrumentProfile piano_profile() {
    kan::InstrumentProfile p = kan::InstrumentProfile::keyboard();
    p.max_simultaneous_notes = 10; // ten fingers (decision 5)
    return p;
}

// ---------------------------------------------------------------------------
void fixture_1_mono_corrected() {
    std::printf("fixture 1: mono note, early wrong-semitone estimate corrected later\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    // G#4 for the first 44 ms of the note, then A4 — the provisional window
    // sees the wrong semitone, the final window the corrected pitch.
    add_correcting_note(sig, 0.3f, 415.30f, 44.0f, 440.0f, 1.1f, 0.5f, 0.7f);

    Harness h;
    run(sig, piano_profile(), {}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit, 1) == 1, "exactly one HIT (onset never revised)");
    const kan::NoteEvent* prov = find(h, kan::NoteKind::Pitch, 1, kan::PitchStage::Provisional);
    const kan::NoteEvent* fin = find(h, kan::NoteKind::Pitch, 1, kan::PitchStage::Final);
    check(prov != nullptr, "provisional pitch emitted");
    check(fin != nullptr, "final pitch emitted (not unresolved)");
    if (prov != nullptr && fin != nullptr) {
        check(std::fabs(prov->u.pitch.midi - 68.0f) < 0.51f,
              "provisional saw the wrong semitone (G#4)");
        check(std::fabs(fin->u.pitch.midi - 69.0f) < 0.51f, "final corrected to A4");
        check(std::fabs(fin->u.pitch.cents) < 25.0f, "final cents sane");
        const float want_hz = 440.0f * std::pow(2.0f, fin->u.pitch.cents / 1200.0f);
        check(std::fabs(fin->u.pitch.hz - want_hz) < 0.5f,
              "Hz derived from the (midi, cents) estimate");
        check(fin->t_ms > prov->t_ms, "facts arrive in evidence order");
    }
    dump(h);
}

// ---------------------------------------------------------------------------
void fixture_2_chord_all_present() {
    std::printf("fixture 2: chord with all candidates present\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.1f, 261.63f, 0.30f, 11u); // C4
    add_note(sig, 0.3f, 1.1f, 329.63f, 0.30f, 12u); // E4
    add_note(sig, 0.3f, 1.1f, 392.00f, 0.30f, 13u); // G4

    kan::NoteDescriber::CandidateSet cs;
    cs.note_id = 1;
    cs.count = 3;
    cs.entries[0] = {0, 60.0f};
    cs.entries[1] = {0, 64.0f};
    cs.entries[2] = {0, 67.0f};

    Harness h;
    run(sig, piano_profile(), {cs}, 1, h);

    const kan::NoteEvent* cls = nullptr;
    for (const auto& e : h.events) {
        if (e.kind == kan::NoteKind::Content && e.note_id == 1
            && e.klass != kan::ContentClass::Unknown) {
            cls = &e;
        }
    }
    check(cls != nullptr && cls->klass == kan::ContentClass::Polyphonic,
          "CONTENT class says polyphonic");
    check(cls != nullptr && cls->u.content.n_unresolved >= 2.5f, "N unresolved counts 3 series");

    bool all_present = true, cents_ok = true, amp_ok = true;
    float similarity = 0.0f;
    for (const float midi : {60.0f, 64.0f, 67.0f}) {
        const kan::NoteEvent* c = find_cand(h, 1, midi);
        if (c == nullptr) { all_present = false; continue; }
        if (c->u.candidate.presence < 0.08f) all_present = false;
        if (std::fabs(c->u.candidate.cents) > 15.0f) cents_ok = false;
        if (c->u.candidate.amplitude <= 0.0f) amp_ok = false;
        similarity = c->u.candidate.similarity;
    }
    check(all_present, "every sounding candidate carries presence >= 0.08");
    check(cents_ok, "per-candidate refined cents within 15 c");
    check(amp_ok, "per-candidate fitted amplitude > 0");
    check(similarity >= 0.80f, "candidate set explains the window (similarity >= 0.8)");
    check(count_kind(h, kan::NoteKind::Content, 1) == 4, "one class + one fact per candidate");
}

// ---------------------------------------------------------------------------
void fixture_3_chord_one_absent() {
    std::printf("fixture 3: chord with one candidate absent (false-presence bound)\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.1f, 261.63f, 0.30f, 21u); // C4
    add_note(sig, 0.3f, 1.1f, 329.63f, 0.30f, 22u); // E4
    add_note(sig, 0.3f, 1.1f, 392.00f, 0.30f, 23u); // G4

    kan::NoteDescriber::CandidateSet cs;
    cs.note_id = 1;
    cs.count = 4;
    cs.entries[0] = {0, 60.0f};
    cs.entries[1] = {0, 64.0f};
    cs.entries[2] = {0, 67.0f};
    cs.entries[3] = {0, 69.0f}; // A4: not sounding

    Harness h;
    run(sig, piano_profile(), {cs}, 1, h);

    const kan::NoteEvent* absent = find_cand(h, 1, 69.0f);
    check(absent != nullptr, "absent candidate is still measured (not dropped)");
    if (absent != nullptr) {
        check(absent->u.candidate.presence <= 0.05f, "absent candidate: presence <= 0.05");
        check(absent->u.candidate.similarity >= 0.80f, "set similarity reported alongside");
        check((absent->flags & kan::kFlagSearchSaturated) != 0
                  || std::fabs(absent->u.candidate.cents) < 15.0f,
              "absent candidate: search saturated or no false detune claim");
        for (const float midi : {60.0f, 64.0f, 67.0f}) {
            const kan::NoteEvent* c = find_cand(h, 1, midi);
            check(c != nullptr && c->u.candidate.presence > absent->u.candidate.presence,
                  "sounding candidates out-presence the absent one");
        }
    }
}

// ---------------------------------------------------------------------------
void fixture_4_sustained_under_new_note() {
    std::printf("fixture 4: sustained LH chord under a new RH melody note\n");
    std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.5f, 130.81f, 0.30f, 31u); // C3 (LH chord, sustains)
    add_note(sig, 0.3f, 1.5f, 164.81f, 0.30f, 32u); // E3
    add_note(sig, 0.3f, 1.5f, 196.00f, 0.30f, 33u); // G3
    // A4 (69), not G4: G4's fundamental is G3's 2nd harmonic — a harmonic
    // collision that splits the LS energy arbitrarily between two notes at
    // the same frequency (the fixture must stay measurable).
    add_note(sig, 0.9f, 0.6f, 440.00f, 0.60f, 34u); // A4 (RH melody)

    kan::NoteDescriber::CandidateSet chord;
    chord.note_id = 1;
    chord.count = 3;
    chord.entries[0] = {0, 48.0f};
    chord.entries[1] = {0, 52.0f};
    chord.entries[2] = {0, 55.0f};

    kan::NoteDescriber::CandidateSet melody;
    melody.note_id = 2;
    melody.count = 4;
    melody.entries[0] = {0, 69.0f}; // the new note
    melody.entries[1] = {1, 48.0f}; // still sounding from note 1's attack
    melody.entries[2] = {1, 52.0f};
    melody.entries[3] = {1, 55.0f};

    Harness h;
    run(sig, piano_profile(), {chord, melody}, 1, h);

    check(count_kind(h, kan::NoteKind::Hit, 0) == 2, "two attacks -> two records");
    check(find_cand(h, 2, 69.0f) != nullptr, "melody candidate measured");
    const kan::NoteEvent* s1 = find_cand(h, 2, 48.0f);
    const kan::NoteEvent* s2 = find_cand(h, 2, 52.0f);
    const kan::NoteEvent* s3 = find_cand(h, 2, 55.0f);
    check(s1 != nullptr && s2 != nullptr && s3 != nullptr,
          "sustaining chord candidates measured under the new attack");
    check(s1 != nullptr && s1->u.candidate.cand_note_id == 1
              && s2->u.candidate.cand_note_id == 1 && s3->u.candidate.cand_note_id == 1,
          "sustaining facts reference the earlier note's record");
    const kan::NoteEvent* mel = find_cand(h, 2, 69.0f);
    if (mel != nullptr) {
        check(mel->u.candidate.presence > 0.10f, "melody present at its own attack");
        check(mel->u.candidate.presence > s1->u.candidate.presence
                  && mel->u.candidate.presence > s2->u.candidate.presence
                  && mel->u.candidate.presence > s3->u.candidate.presence,
              "melody dominates the window it attacks");
    }
    const kan::NoteEvent* fin2 = find(h, kan::NoteKind::Pitch, 2, kan::PitchStage::Final);
    check(fin2 != nullptr, "melody final estimate present");
    if (fin2 != nullptr && fin2->u.pitch.hz > 0.0f) {
        check(std::fabs(fin2->u.pitch.midi - 69.0f) < 0.51f, "melody final pitch = A4");
    }
    dump(h);
}

// ---------------------------------------------------------------------------
void fixture_5_unexplained_extra() {
    std::printf("fixture 5: unexplained extra note (never invented, honestly flagged)\n");
    std::vector<float> sig(size_t(1.6f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.1f, 261.63f, 0.30f, 41u); // C4
    add_note(sig, 0.3f, 1.1f, 329.63f, 0.30f, 42u); // E4
    add_note(sig, 0.3f, 1.1f, 392.00f, 0.30f, 43u); // G4: NOT in the candidates

    kan::NoteDescriber::CandidateSet cs;
    cs.note_id = 1;
    cs.count = 2;
    cs.entries[0] = {0, 60.0f};
    cs.entries[1] = {0, 64.0f};

    Harness h;
    run(sig, piano_profile(), {cs}, 1, h);

    check(find_cand(h, 1, 67.0f) == nullptr, "no fact invented for the un-hypothesized note");
    bool flagged = false;
    float reported_similarity = 1.0f;
    for (const auto& e : h.events) {
        if (e.note_id != 1) continue;
        if ((e.flags & kan::kFlagUnexplainedEnergy) != 0) flagged = true;
        if (e.kind == kan::NoteKind::Content && e.klass == kan::ContentClass::Unknown) {
            reported_similarity = e.u.candidate.similarity;
        }
    }
    check(flagged, "UNEXPLAINED_ENERGY flagged on the note's facts");
    check(reported_similarity < 0.80f, "similarity reports the unexplained energy");
    const kan::NoteEvent* fin = find(h, kan::NoteKind::Pitch, 1, kan::PitchStage::Final);
    check(fin == nullptr || fin->u.pitch.hz <= 0.0f,
          "no confident final pitch from an incomplete candidate set");
}

// ---------------------------------------------------------------------------
void fixture_6_live_vs_offline() {
    std::printf("fixture 6: live-vs-offline stream equality (byte-identical JSONL)\n");
    std::vector<float> sig(size_t(2.2f * kSr), 0.0f);
    add_note(sig, 0.3f, 1.5f, 130.81f, 0.30f, 51u);
    add_note(sig, 0.3f, 1.5f, 196.00f, 0.30f, 53u);
    add_note(sig, 0.9f, 0.6f, 392.00f, 0.60f, 54u);

    kan::NoteDescriber::CandidateSet chord;
    chord.note_id = 1;
    chord.count = 2;
    chord.entries[0] = {0, 48.0f};
    chord.entries[1] = {0, 55.0f};
    kan::NoteDescriber::CandidateSet melody;
    melody.note_id = 2;
    melody.count = 2;
    melody.entries[0] = {0, 67.0f};
    melody.entries[1] = {1, 48.0f};
    const std::vector<kan::NoteDescriber::CandidateSet> cands = {chord, melody};

    Harness offline, live, offline2;
    run(sig, piano_profile(), cands, 1, offline);   // offline: candidates ASAP
    run(sig, piano_profile(), cands, 7, live);      // live: late injection, same blocks
    run(sig, piano_profile(), cands, 1, offline2);  // determinism

    check(!offline.jsonl.empty(), "offline run produced events");
    check(offline.jsonl == live.jsonl,
          "late candidate injection + drain batching -> byte-identical stream");
    check(offline.jsonl == offline2.jsonl, "two identical runs -> byte-identical stream");
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
    std::printf("%s\n", g_pass ? "ALL PASS" : "FAILURES");
    return g_pass ? 0 : 1;
}
