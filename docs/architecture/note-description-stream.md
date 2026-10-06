# Per-note description stream ("register by note") — evidence & design

Status: **Phase 1 evidence + design.** Decisions resolved 2026-10-06 (§7);
Phase 2 implementation follows this document (§8). The measurement evidence
below is real (Phase 1a, this machine). Authoritative DSP contracts live in
[docs/dsp/README.md](../dsp/README.md); this document designs the layer that
turns those measurements into **per-note facts**.

Scope rule (non-negotiable): libkan **describes** what was played. No chart
pairing, no judgment windows, no correct/wrong verdicts, no practice/timed
modes — those belong to `game/`. Candidate pitches are an optional *input*
that conditions a measurement; every output is a measurement (presence 0..1,
cents, amplitude), never a verdict.

**Fairness is the design constraint behind the honesty rules.** The game
will reward the attack instantly and evaluate intonation / digitation later
from these facts (game-side decision). If a fact is wrong, a player receives
a wrong penalty, the game feels unfair, and the player quits instead of
looping the song again. So "one estimate per fact", "no numbers the tool
distrusts" and "`unresolved` instead of a guess" are not documentation
hygiene — they are the dopamine-loop protection.

## 1. What the stream is

Today `kan_analyze` collapses a performance into one record per note at the
*end* of the note. The game needs the opposite ordering: **each attack opens
a note record, and facts are added as soon as they become available**:

1. **HIT** — onset time + strength. Fixed at the moment of the onset, never
   revised. This is what the game's instant feel consumes.
2. **PITCH** — one provisional estimate, then one final estimate (or
   `unresolved`). One consistent estimate per event (note name, Hz and cents
   always describe the same reading — the Phase-0 rule, generalized).
3. **CONTENT** — mono vs polyphonic attack; when the caller supplies
   candidate pitches, one fact per candidate: presence, cents, amplitude.
4. **EXPRESSION** — dynamics, envelope (attack/release/sustain), vibrato.
5. **CLOSE** — note end + summary.

The same stream feeds `kan_demo`'s display, a JSONL log, and `kan_analyze`
(offline), using one schema. The live and offline paths must produce
identical streams (§5.3).

## 2. Evidence: score-informed validation on real piano material (Phase 1a)

Chord validation was proven only on synthesized guitar triads
([chord-validation.md](../dsp/chord-validation.md): F1 1.0000, 40 trials).
Before designing the stream around it, it was measured on a real piano take
with left-hand chords under a right-hand melody.

### 2.1 Method (reproducible; artifacts are gitignored)

- Recording: `test_results/audio/07-hallelujah-piano-tutorial_piano.wav`
  (254.3 s, 48 kHz, 244 attacks per `kan_analyze`).
- Annotation: **28 attacks, hand-written candidate lists**
  (`libkan/tests/harmonic_probe_notes.hpp`), derived from long-window
  spectral inspection (`libkan_harmonic_probe --dump`: 341 ms Hann windows
  zero-padded to 131072, harmonic-sum fundamental scoring + peak list).
  A candidate is included when its **f0 carries a real spectral line**, not
  when a harmonic-sum alias suggests it (the 588 Hz line under G3 is G3's
  3rd harmonic, not a D5). 22 attacks are polyphonic (4–8 sounding
  candidates; A05/A10/A14/A19/A21 exceed `kMaxNotes = 6`); 6 are mono
  moments found by scanning all 244 onsets for single-fundamental spectra.
  Each attack carries 2 negative **controls** (believed absent: easy
  off-chord pitches and hard chromatic neighbors).
- The annotation is a *hypothesis list*, not ground truth. The probe
  (`libkan_harmonic_probe --measure`, test-side, not a ctest) prints, per
  candidate, the validator's presence/refined-cents **and** independent
  f0-line evidence (line position and level) side by side. The evidence
  column caught two wrong entries in the annotation during curation (MIDI
  42 written where the spectra show 43) — it is a real cross-check.
- Reported "cents accuracy" = |refined − f0-line| for candidates whose f0
  line is ≥ −10 dB (n = 98). Reported "presence separation" comes from runs
  where the candidate set contains sounding notes **and** controls.
- Artifacts: `test_results/logs/07-harmonic-probe-{dump,measure,bench}.txt`.

### 2.2 Per-candidate presence separates sounding from absent — inside a
complete candidate set

Mixed runs (sounding + controls together, 1024-sample windows at +200 ms):

| | n | min | median | max | mean |
|---|---|---|---|---|---|
| sounding candidates (presence share) | 22 | 0.07 | 0.27 | 0.99 | 0.43 |
| control candidates (presence share) | 20 | 0.00 | 0.01 | 0.15 | 0.03 |

Similarity of these runs ≥ 0.88. A presence share below ~0.05 is a reliable
"absent"; 0.05–0.15 is ambiguous (hard neighbors); sounding notes sit above
that except for very soft chord tones (min 0.07).

### 2.3 Presence without similarity lies; near-miss candidates absorb energy

Runs with **only** controls as candidates (nothing real in the set):

| attack class | similarity | max presence handed to an absent note |
|---|---|---|
| poly attacks | 0.05–0.33 | 0.51–0.95 |
| mono attacks | 0.60–0.92 | 0.78–0.83 |

Two distinct effects: (a) at low similarity the share normalizes to 1 over
garbage — presence 0.8 at similarity 0.08 means nothing; (b) at 21 ms
windows the model's resolution is ~47 Hz, so a candidate **one semitone
away from the true note** (F4 model vs E4 signal, 19.6 Hz apart) explains
92% of the energy. **Consequence for the stream: a presence number is only
ever emitted together with the explained-energy (`similarity`) of the whole
set; the reader can never mistake share for sounding-probability.**
Absolute presence (share × similarity) is derivable but must not replace
the pair.

### 2.4 Cents accuracy: the long window halves the error, especially in the
bass

|refined − f0-line| over candidates with strong f0 lines (n = 98):

| window | all: mean / median | bass < C3 (n = 14): mean |
|---|---|---|
| 1024 samples (21 ms, gate budget) at +200 ms | 12.0 c / 10 c | 22.4 c |
| 8192 samples (170 ms) at +200 ms | **6.0 c / 4 c** | **13.9 c** |
| 1024 at +8 ms (inside the attack) | 12.9 c / 10 c | 26.2 c |

This extends L3 ("fast-estimate cents are confirmation-grade") to
candidate-conditioned fits and settles the low-register question: **final
pitch/cents below ~C3 must integrate ≥ 170 ms of audio**; the 21 ms window
is for provisional facts only. Refined-cents estimates that saturate at the
±30 c search bound (typical for absent candidates) are "no evidence", not a
measurement — the stream flags them instead of printing ±30 c.

### 2.5 More than 6 sounding notes (`kMaxNotes = 6`)

The five annotated attacks with 7–8 sounding candidates, validated with the
loudest 6 modeled (1024 samples at +200 ms):

| attack | sounding / modeled | similarity | note |
|---|---|---|---|
| A05 | 7 / 6 | 0.972 | unmodeled E4 (soft) |
| A10 | 8 / 6 | 0.997 | unmodeled C4, C3 (soft) |
| A14 | 8 / 6 | 0.981 | unmodeled F2, C4 (soft) |
| A19 | 8 / 6 | 0.949 | unmodeled C4, F2 (soft) |
| A21 | 8 / 6 | 0.966 | unmodeled D5, G4 (mid!) |

Unmodeled soft notes barely move the fit; unmodeled mid-register notes
(A21: D5, G4) redistribute the shares of the modeled set (C3's share rises
to 0.52). The measurement of the modeled notes stays *ordered* correctly
but is *biased* — and the stream must say so (`UNEXPLAINED_ENERGY`), not
pretend the set was complete.

### 2.6 Decay and pedal

Presence is a per-window snapshot and it *moves* as notes decay (all runs
1024 samples, presence shares):

| attack | +8 ms | +200 ms | +800 ms | +1500 ms |
|---|---|---|---|---|
| A01 (C3,G3,C4,G4) | 0.20/0.41/0.06/0.32 | 0.07/0.18/0.61/0.14 | 0.07/0.78/0.08/0.07 | 0.09/0.39/0.23/0.29 |
| A21 (8 notes) | 0.35/0.18/0.22/0.13/0.01/0.11 | 0.52/0.18/0.07/0.12/0.03/0.07 | 0.16/0.50/0.06/0.11/0.05/0.12 | 0.22/0.20/0.32/0.07/0.00/0.19 |

G4 dominates A01 at +200 ms, C3 at +800 ms. Pedal keeps bass alive at
+1500 ms (A10's F2 still carries 0.72 share there). **Consequences:
CONTENT facts carry their window time; "dominant pitch" is a per-window
statement (the stream emits it at a defined window, not as a note
property); CLOSE must use a decay-relative note end (the L7 follow-up), not
an absolute silence threshold.**

### 2.7 Blind mono-vs-polyphonic classification per attack

Features computed with **no candidate input** (170 ms window at +200 ms):
best-single-harmonic-series energy capture ("capture"), explained ratio of
the best single blind fit ("sim1"), count of spectral peaks. Best
single-threshold accuracy over the 28 annotated attacks (6 mono / 22 poly):

| feature | threshold | accuracy |
|---|---|---|
| capture | 0.944 | **26/28 (0.93)** |
| sim1 | 0.944 | 26/28 (0.93) |
| peak count | 7.5 | 26/28 (0.93) |

Today's reliability heuristic (MIX/HOP flags of `kan_analyze`) on the same
28 attacks: 20/28 (0.71) — it flags 5 of the 6 mono moments as unreliable
(over-flag) and calls 3 of the 22 poly attacks "reliable" (A01, A08, A17 —
false-mono; A01 is a 4-note chord the mono tracker "confidently" reports as
G4). Reliability ≠ mono. The stream's CONTENT class should use the
capture-based classifier; MIX/HOP stay as PITCH-reliability flags only.

### 2.8 `validate()` cost (the `kMaxNotes` decision needs numbers)

Synthetic chords at the expected pitches, mean of 200 calls, Release build
(`libkan_harmonic_probe --bench`):

| candidates | 1024 samples | 8192 samples |
|---|---|---|
| 1 | 2.0 ms | 16 ms |
| 3 | 10.8 ms | 85 ms |
| 6 | 34.8 ms | 280 ms |

Cost grows ~quadratic in candidate count (Gram build is
O(atoms²·window)) and ~linearly in window length. Extrapolated to the
per-instrument maxima: 10 candidates (piano) ≈ 90 ms / 730 ms. **This contradicts the "~2–4 ms per call"
CPU claim in docs/dsp/README.md §2.7, which had no backing measurement** —
that table is updated to the measured numbers in this change. The 29.3 ms
*verification latency* gate (chord-validation.md) is unaffected: it counts
audio window, not CPU.

## 3. Design: the `NoteEvent` POD

Fixed-size, trivially copyable, no allocation, safe for an SPSC ring.
Proposal for `libkan/include/libkan/note_stream.hpp` (Phase 2):

```cpp
enum class NoteKind : uint8_t { Hit, Pitch, Content, Expression, Close };
enum class PitchStage : uint8_t { Provisional, Final, Unresolved };
enum class ContentClass : uint8_t { Unknown, Mono, Polyphonic };

// Audio-true states only — never judgment (no correct/miss/good).
enum NoteFlags : uint32_t {
    kFlagNone = 0,
    kFlagMixed = 1u << 0,             // mono pitch validity < 0.5 (overlapped)
    kFlagHop = 1u << 1,               // tracker hopping between sources
    kFlagPolyphonic = 1u << 2,        // attack classified polyphonic
    kFlagDominant = 1u << 3,          // one candidate clearly dominant this window
    kFlagUnexplainedEnergy = 1u << 4, // candidate set does not explain the energy
    kFlagLowRegister = 1u << 5,       // final estimate needed the long window
    kFlagSearchSaturated = 1u << 6,   // refined cents hit the search bound: no evidence
};

struct NoteEvent {
    uint32_t note_id;        // unique per describe() session, monotonic
    NoteKind kind;
    PitchStage stage;        // Pitch events only
    ContentClass klass;      // Content class events only
    uint8_t candidate_index; // Content candidate events only
    uint32_t flags;          // NoteFlags
    float t_ms;              // stream time of the fact (end of its evidence window)
    union Payload {
        struct { float onset_ms; float strength; } hit;
        struct { float midi; float cents; float hz; } pitch;             // one estimate
        struct { float midi; float presence; float cents; float amplitude;
                 float similarity; uint32_t cand_note_id; } candidate;   // Content
        struct { float n_sounding_lines; float dominant_share; } content; // Content class
        struct { float dynamics_db; float attack_ms; float release_ms;
                 float sustain_ratio; float vib_rate_hz; float vib_depth_c; } expression;
        struct { float end_ms; float duration_ms; float peak_rms; } close;
    } u;
};
```

Semantics per kind:

- **Hit** — `onset_ms` (block-quantized) + `strength` (attack energy).
  Emitted from the onset path; **never revised or superseded**.
- **Pitch** — `Provisional`: earliest post-blanking estimate (short window;
  may carry `MIX`/`HOP`). `Final`: one consistent estimate (midi + cents,
  Hz derived from the pair) from the longest available evidence (§2.4), or
  `Unresolved` with the flags that say why (polyphonic without candidates,
  hop, similarity too low). Dominant-pitch case: `Final` with
  `kFlagDominant`, stated at a defined window.
- **Content** — one *class* event (`Mono`/`Polyphonic` +
  `n_sounding_lines` = the blind f0-line count, the honest "N unresolved")
  plus, when candidates were supplied, one *candidate* event per candidate
  (`presence` share, `cents`, `amplitude` = fitted component RMS,
  `similarity` of the whole set, `cand_note_id`).
- **Expression** — dynamics (Phase 3), envelope, vibrato. Facts only; the
  spec/verdict layer of `technique.hpp` stays where it is.
- **Close** — `end_ms` (decay-relative), duration, peak. Never revises
  earlier facts.

## 4. Design: the candidate-pitch input API

The caller (game/chart side) supplies hypotheses per attack; libkan
measures them. Hypotheses include **notes still sustaining from earlier
attacks** (LH chord under a new RH melody note).

```cpp
struct NoteCandidateSet {
    uint32_t note_id;          // attack whose record receives the facts
    uint8_t count;             // <= kMaxCandidates (10 = hard cap; the
                               // per-instrument limit comes from the
                               // InstrumentProfile: wind 1, guitar 6, piano 10)
    struct Entry {
        uint32_t cand_note_id; // 0 = anonymous hypothesis, else the earlier
                               // note record this hypothesis belongs to
        float midi;            // hypothesis, equal-tempered; measured cents returned
    } entries[kMaxCandidates];
};

class NoteDescriber {
public:
    explicit NoteDescriber(float sample_rate);        // allocates all scratch once
    void set_candidates(const NoteCandidateSet&) noexcept; // copies into fixed storage
    void process_block(const float* samples, size_t n) noexcept; // pushes events
    NoteEventStream& stream() noexcept;
};
```

- **No allocation on the hot path.** The describer preallocates its fit
  scratch at construction (as `HarmonicChordValidator` does); candidate
  sets are copied into fixed slots. In the canonical pipeline the caller
  runs on the same consumer thread as `process_block`, so
  `set_candidates` is a plain copy — no lock, no cross-thread protocol.
- **Attribution — nothing to decide.** Every candidate fact is one event
  carrying **both** ids: `note_id` = the attack whose window produced the
  measurement, `cand_note_id` = the note record the candidate belongs to
  (0 = anonymous hypothesis). No duplication, and consumers can group
  either way: "what did this attack hear" (by `note_id`) or "what happened
  to this held chord note" (by `cand_note_id`).
- **Candidates are inputs, not truth.** A candidate absent from the audio
  gets a low presence — the stream reports that measurement. A note nobody
  hypothesized shows up as `UNEXPLAINED_ENERGY`, never as a guess.
- Without candidates, CONTENT still emits the class fact
  (`Polyphonic, N unresolved`) and PITCH falls back to dominant-or-
  unresolved — no blind polyphonic transcription (L2/L10).

## 5. Design: stream, JSONL schema, live/offline equivalence

### 5.1 Stream

`NoteEventStream` = `LockFreeRingBuffer<NoteEvent, Capacity>` semantics:
SPSC, fixed capacity (power of two), `push` (analysis path) / `pop`
(consumer), **drop-on-full with a `dropped()` counter** — a lossy display
may drop, a JSONL log may not; the counter makes loss visible instead of
silent. The describer holds a fixed array of subscriber streams (≤ 4) and
pushes each event to all — `tap.hpp`-style fan-out, but push-based
(completeness-oriented, not overwrite/freshness). The audio callback stays
out of this entirely: it only feeds the existing sample ring buffer.

### 5.2 JSONL schema (v1)

One JSON object per line, one per event, deterministic key order so live
and offline logs diff cleanly:

```json
{"v":1,"note":17,"kind":"HIT","t_ms":3320.0,"onset_ms":3320.0,"strength":0.42,"flags":[]}
{"v":1,"note":17,"kind":"CONTENT","t_ms":3341.0,"class":"polyphonic","n_unresolved":3,"flags":["POLYPHONIC"]}
{"v":1,"note":17,"kind":"CONTENT","t_ms":3341.0,"cand":12,"cand_note":9,"midi":67,"presence":0.61,"cents":-5.0,"amp":0.11,"similarity":0.96,"flags":[]}
{"v":1,"note":17,"kind":"PITCH","t_ms":3341.0,"stage":"provisional","midi":67,"cents":-4.2,"hz":392.1,"flags":["MIX"]}
{"v":1,"note":17,"kind":"EXPRESSION","t_ms":3661.0,"dyn_db":-12.4,"attack_ms":37,"release_ms":205,"sustain":0.58,"vib_hz":5.1,"vib_depth_c":33.0,"flags":[]}
{"v":1,"note":17,"kind":"PITCH","t_ms":3661.0,"stage":"final","midi":67,"cents":-4.0,"hz":391.9,"flags":[]}
{"v":1,"note":17,"kind":"CLOSE","t_ms":3661.0,"end_ms":3661.0,"dur_ms":341.0,"flags":[]}
```

Schema rules (the honesty rules of Phase 0, generalized):

1. A `PITCH` event carries **one** estimate: `hz == 440·2^((midi−69)/12 +
   cents/1200)` exactly, or the three fields are `null` together
   (`stage: "unresolved"`) with flags explaining why.
2. Numbers the tool flagged unreliable are never emitted: `search-
   saturated` refinement reports the flag, not ±30 c.
3. Presence is always emitted with `similarity`; `amp` is the fitted
   component RMS (an absolute measure), `presence` the explained-energy
   share (a relative one).
4. `flags` lists only audio-true states (§3). No verdict vocabulary ever
   appears in the stream.

### 5.3 Live/offline equivalence

`describe()` is a **pure function of (audio blocks, candidate sequence)**:
time fields derive from sample positions, not wall clocks; all estimation
is deterministic (fixed scratch, fixed refinement rounds). Therefore:

> Feeding a WAV offline and feeding the same samples as a live block stream
> (any block size) must produce **byte-identical JSONL**.

Enforced by a Phase 2 test (`live-vs-offline stream equality on the same
file`) — this is what makes `kan_demo --events`, the JSONL log and
`kan_analyze` interchangeable for regression diffing.

## 6. Changes the experiment forces (decided here, implemented in Phase 2)

1. **Window length for the low register.** Final pitch/cents integrate
   ≥ 170 ms (8192 samples) whenever the estimate is below ~C3 or the
   lowest candidate is; short windows are provisional-only (§2.4). Flags:
   `kFlagLowRegister`.
2. **The candidate cap is per instrument (hard cap 10).** Wind
   instruments model 1 note (mono physics), guitar 6 (strings), piano 10
   (fingers) — `InstrumentProfile` / instrument-pack field
   `max_simultaneous_notes`; `HarmonicChordValidator`'s compile-time cap
   rises 6 → 10 (`kMaxAtoms` 120) and the profile value is enforced on
   `set_expected_chord`. Cost grows ~quadratic in candidate count
   (§2.8: measured 34.8 ms for 6 candidates at 21 ms, 280 ms at 170 ms;
   extrapolated 10 candidates ≈ 90 ms / 730 ms) — so (3) below is not
   optional. When more candidates than the instrument's cap are supplied,
   the loudest N are described and the fact is flagged
   `UNEXPLAINED_ENERGY` (§2.5).
3. **Final fits are scheduled work, never inline.** `NoteDescriber` splits
   the pipeline: `process_block` (HIT + provisional + classification,
   bounded, inline with the audio service loop) and `finalize_due()` (at
   most one long-window candidate fit per call, movable to a worker
   thread by the app). libkan owns no threads; it only bounds the work
   per call. The game rewards on HIT and evaluates intonation later —
   a final fact arriving 100–500 ms after the attack is by design.
4. **`unresolved` fallback is the default for blind polyphony**:
   `CONTENT: polyphonic, N unresolved` (N = blind f0-line count) plus
   timing/dynamics, plus `Final` with `kFlagDominant` only when one
   candidate dominates at the stated window (§2.6). "Unresolved" must
   never become a penalty: the game treats it as *not measured*, not as
   *wrong* (fairness rule, §1).
5. **Presence ships with similarity** (§2.3) — the pair
   (`presence` share, `similarity` of the set); the game derives any
   combined score. `HarmonicChordValidator`'s template-level `valid`
   verdict (fair-share presence floor) is *not* carried into the stream —
   it fails on real uneven piano chords and is a judgment-shaped concept.
6. **Mono/poly class** comes from the blind capture classifier, not from
   MIX/HOP (§2.7). The threshold is instrument-pack data (measured ~0.94
   on piano; wind attacks are mono by physics and bypass the classifier).
7. **CLOSE is decay-relative** (L7) — pedal tails must not inflate
   durations (§2.6).

## 7. Decisions (resolved 2026-10-06)

| # | question | resolution |
|---|---|---|
| 1 | where candidate facts are written | nothing to decide: one event carries both ids (`note_id` + `cand_note_id`); consumers group as they wish |
| 2 | final-pitch latency vs game feel | attack-first: HIT is the instant reward; provisional is display-only and **never penalty-grade**; scoring-grade facts are final facts only (game-side evaluation comes later) |
| 3 | heavy final fits on the consumer thread | scheduled work: `finalize_due()` runs at most one long-window fit per call; apps may move it to a worker thread; no threads inside libkan |
| 4 | presence semantics downstream | ship the pair (`presence`, `similarity`); the game derives any combined score; no pre-mixed number |
| 5 | `kMaxNotes` | per instrument via `InstrumentProfile::max_simultaneous_notes` (wind 1, guitar 6, piano 10); libkan hard cap 10 |
| 6 | mono/poly threshold | per instrument pack (piano starts at the measured 0.94; wind bypasses the classifier — mono by physics) |

Kept deliberately on the **game** side (not decided here): how HIT /
provisional / final facts feed reward and penalties, what `unresolved`
means for scoring (the stream only guarantees it is never a wrong number),
and the dynamics tiers' mapping to rewards (Phase 3 delivers measurements
only).

## 8. Phase 2 implementation plan (decisions resolved, §7)

- `libkan/include/libkan/instrument_profile.hpp`: add
  `max_simultaneous_notes` (wind 1 / guitar 6 / piano 10) and the
  mono/poly classification threshold as plain profile data (the
  instrument-pack schema mirrors these fields).
- `libkan/include/libkan/note_stream.hpp` (`NoteEvent`, `NoteEventStream`,
  `NoteDescriber` with the `process_block` / `finalize_due()` split) +
  offline ctests on synthesized fixtures:
  mono note with an early wrong-semitone estimate corrected later; chord
  with all candidates present; chord with one candidate absent; sustained
  LH chord under a new RH note; unexplained extra note; live-vs-offline
  stream equality on the same file.
- `kan_analyze` emits the JSONL (`--events`), keeping the summary
  descriptor; `kan_demo --events <f.jsonl>` with progressive display
  (provisional → final pitch; "polyphonic, N unresolved" instead of
  garbage) and an optional `--candidates` input exercising the conditioned
  measurement.
- Phase 3 (dynamics) plugs in as an EXPRESSION fact
  (`libkan/technique.hpp`-adjacent, tiers pp..ff relative to session or
  calibration).

## 9. Where the evidence lives

| artifact | contents | committed |
|---|---|---|
| `libkan/tests/harmonic_probe.cpp` | --dump / --measure / --bench evidence probe | yes (test-side) |
| `libkan/tests/harmonic_probe_notes.hpp` | hand-written 28-attack annotation + method | yes |
| `test_results/logs/07-harmonic-probe-*.txt` | measured outputs of this run | no (gitignored) |
| `test_results/audio/07-hallelujah-piano-tutorial_piano.wav` | the recording | no (gitignored) |
