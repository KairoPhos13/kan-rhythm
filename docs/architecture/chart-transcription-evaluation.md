# Chart transcription — helper tool evaluation & design

**Status:** design + candidate evaluation (2026-10-12). Web research was
unavailable at writing time (integration outage); candidate facts are
from prior knowledge and marked **[verify]** for license/version checks.
The requirements analysis and architecture are grounded in the project's
measured evidence (TEST_LOG, validation-status).

**Goal (user):** a transcriber helper — NOT part of the real-time
library — that generates level drafts (times, notes, and playing
characteristics where possible), because authoring every level by hand
does not scale.

---

## 1. Requirements (from everything we measured)

1. **Timing at judgment grade.** The game judges rhythm against these
   times; our own evidence says attack times are measurable to ~5-20 ms
   on real material. A transcriber whose onsets drift 50-100 ms would
   bake unfair charts (and the game's G3 gate depends on timing facts).
2. **Playing characteristics are OURS.** Dynamics tier, vibrato, bend,
   articulation, re-strike vs extension — the EXPRESSION facts — exist
   only in libkan (validated: PASS-18..23). No external model emits
   them. A chart that wants "(vibrato)" annotations must come from our
   stream.
3. **Instrument extensibility** (ocarina/winds first, per the user's
   field tests): most external models are piano-trained. Our pipeline is
   profile-driven (I10) and already tested on winds.
4. **Honesty rule (validation-status §4):** AI-model output is a *weak
   label* — fine as a DRAFT for human review, never as ground truth.
   This decides the architecture: the model proposes, the human confirms,
   libkan measures the result.
5. **Offline, fast, batchable** (dataset_bench-style): transcription is
   authoring-time, not play-time. Our core runs 1.5-15x real time.
6. **Text-based chart format we already have** (`onset_ms duration note`
   + technique annotations) — a transcriber that emits it plugs straight
   into `--verify-score`/`--guide`.

## 2. Architecture recommendation: layered, ours at the core

```
audio ──► libkan blind pipeline (facts) ──► chart draft (.score)
        │   HIT/PITCH/CONTENT/EXPRESSION          │
        │   profile-driven (any instrument)       ▼
        └──► optional external proposer ──► merge into draft
             (polyphonic piano: model notes)      │
                                                  ▼
                                     human review (editor) ──► final chart
                                                  │
                                                  ▼
                                     --verify-score regression (us)
```

- **Layer 1 (build first): `tools/chart_scribe`** — run the blind stream
  over a recording, serialize the per-note facts into chart text:
  onset (strike time), duration (key-hold estimator + `end_faded`),
  pitch (trajectory center), and annotations from EXPRESSION (dynamics
  tier, `(vibrato)` with rate/depth, `(bend)`, articulation). This is
  mostly wiring: the facts already exist and are validated.
- **Layer 2 (optional): external polyphonic proposer** — only for dense
  piano material where our blind hypothesis generator over-proposes
  (known: the `--emit-candidates` noise). Its notes enter the draft as
  *proposals* with a confidence column the human sees.
- **Layer 3: human-in-the-loop** — the chart is reviewed/edited; then
  `--verify-score` measures the final chart against the recording
  (the tooling exists and is validated on 1.5M+ notes).

## 3. Candidate evaluation (verify licenses before adoption)

| candidate | strengths | weaknesses for us | fit |
|---|---|---|---|
| **Basic Pitch** (Spotify) **[verify: Apache-2.0, ONNX]** | polyphonic; note onsets+offsets AND pitch bends (bends match our bend facts!); lightweight; ONNX runs offline | no dynamics/technique facts; trained general (piano-strong); AI-intrinsic error | **best Layer-2 proposer** for polyphonic drafts |
| **piano_transcription_inference** (Kong et al.) **[verify: license, torch dep]** | state-of-art piano onsets/offsets, pedal | piano-only (breaks requirement 3); heavy torch stack; no technique | piano-only alternative to Basic Pitch |
| **MT3** (Magenta) **[verify]** | generalizes across instruments, symbolic text output | heavy model, 30 s windows, GPU-oriented, slow; AI-intrinsic error | poor fit (overkill; we already emit text facts) |
| **CREPE / pYIN-style pitch trackers** | excellent monophonic tracking | monophonic only | redundant — our YIN/BACF stream already does this, validated |
| **omnizart / librosa pipelines** | all-in-one | aging stacks, tensorflow1-era, mixed licenses | avoid |

**Recommendation:** adopt **no external model as core**. Build Layer 1 on
our validated facts (it delivers what no model can: technique
annotations + instrument profiles + honest unresolved semantics). Add
Basic Pitch (or piano_transcription_inference for piano-only corpora) as
an OPTIONAL proposer behind a flag, feeding human-reviewed drafts. This
respects the §4 honesty rule and keeps judgment and transcription on the
same measurement physics — no systematic mismatch between how a chart is
made and how it is judged.

## 4. Named TODOs

1. `tools/chart_scribe` (Layer 1): blind facts -> chart text with
   onset/duration/pitch + (vibrato)/(bend)/dynamics annotations;
   selftest = synthesize -> transcribe -> verify-score roundtrip.
2. Verify candidate licenses/versions (search outage) before any
   dependency lands; keep them optional (no default install).
3. Layer-2 proposer adapter (Basic Pitch first) emitting draft rows with
   a `propose` marker for the human.
4. Editor UX is out of scope here (authoring tool is a separate track).
