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

## 3b. Time anchors — the +250 ms question (decided 2026-10-12)

The MAESTRO "+250 ms" is NOT strike delay (user question, answered with
evidence):

- **Onsets (strikes): 5-20 ms error.** Physical anchor difference: the
  Disklavier MIDI labels the KEY event, the audio records the STRING
  sound (hammer travel ~10-20 ms) + block quantization (2.7 ms) + HFC
  build. Not a defect: two anchors.
- **Duration (ends): +210/234 ms bias, p99 = 250 ms.** This is OUR
  measurement window padding (ISS-6: looking stops at key-hold + 250 ms
  and the end pins there; p99 = 250 is stable because it is the method's
  constant).
- **Winds:** slow breath ramps are profile data (`judgment_offset_ms`,
  -20 ms for winds) — already modeled.

**Decision: correct at the SERIALIZER (chart_scribe), never with a fudge
factor in the game.** The game compares audio-anchored charts against
audio-anchored player input — anchor symmetry gives zero correction on
both sides. Chart serialization writes:

1. `dur = measured_end - calibrated_bias` (~210 ms, measured on 1.5M+
   notes), keeping the raw end and `end_faded` for the editor;
2. onset = the AUDIO attack (HIT semantics already);
3. per-profile `judgment_offset_ms` where the instrument's attack ramp
   shifts the audible anchor.

Consistency for sensitive ears comes from documented semantics ("anchored
to sound; duration bias-corrected by X") plus published error numbers —
not from pretending the anchors coincide.

## 3c. Layer 0: source separation — comparison & policy

### Mass-use policy (user decision, 2026-10-12)

The project targets MASSIVE use. Rules:

1. **Best experience available on each machine, zero configuration.**
   GPU when the user has one (CUDA or ROCm — both expose as `cuda` to
   torch), CPU otherwise. The device is probed automatically
   (`best_separator_device()`), never a user setting.
2. **Heavy AI dependencies are OPTIONAL layers, never core.** Core =
   pure C++20 + single-header miniaudio; nothing in `libkan/` requires
   Python or torch. AI helpers plug in behind detect-and-invoke
   boundaries (like `yt-dlp`), with `KAN_SEPARATOR` / `KAN_YTDL_ARGS`
   user overrides.
3. **Dependency size matters.** torch is gigabytes; lighter runtimes
   (ONNX Runtime, ~50 MB, with CPU/CUDA/ROCm/Metal/DirectML backends)
   fit mass distribution far better. Roadmap: accept an ONNX-based
   separator via `KAN_SEPARATOR` today; prefer one as default backend
   once a good separation model is verified.

### Comparison (user asked what to expect)

| project | stems | strengths | limitations | verdict |
|---|---|---|---|---|
| **Demucs 4.x** (Meta, MIT) | 4: drums/bass/vocals/other; **6s adds guitar+piano** | SOTA quality; pip-installable; CPU ok, GPU faster | **FIXED stem set** — no "ocarina" class: winds land in *vocals* (lead) or *other*; torch-sized dep | **adopted (optional layer)** |
| Spleeter (Deezer, MIT) | 2/4/5 | light, fast | older quality; TF stack; same fixed-class problem | rejected (quality) |
| Open-Unmix (UMX, MIT) | 4 | simple, torch | weaker separation | rejected |
| UVR / MDX-Net / RoFormer | per-model (karaoke, custom) | best-in-class vocal/instrumental splits | GUI ecosystem, model licensing varies | radar for vocal splits |
| ONNX-based separators | varies | ~50 MB runtime; every HW backend | model quality varies | **preferred future backend** |

**Capabilities to expect (honest):**

- Clean separation of *categories*, not instruments: for an ocarina song
  the lead usually lands in `vocals` or `other` — verify by ear with
  `--only vocals` / `--only other` (both generated for the sample song).
- Artifacts: bleed between stems, warble on dense passages. For
  transcription this helps a lot (the target instrument stops competing
  for the spectrum); for karaoke backing artifacts are usually masked by
  the player's own instrument.
- Everything separation outputs is a DRAFT (AI model, §4 weak-label
  rule). The remix math (minus/only) is ours and deterministic.

**ROCm note:** this environment runs `torch 2.12.0+cpu`; GPU use needs
the ROCm torch wheel matched to the installed ROCm release
(`pip install --user --break-system-packages torch --index-url
https://download.pytorch.org/whl/rocm6.x`). Wheel availability for the
Python version (3.14 here) is the compatibility risk — CPU works
regardless, and the auto-device probe means users never think about it.

## 4. Named TODOs

1. `tools/chart_scribe` (Layer 1): blind facts -> chart text with
   onset/duration/pitch + (vibrato)/(bend)/dynamics annotations;
   selftest = synthesize -> transcribe -> verify-score roundtrip.
2. Verify candidate licenses/versions (search outage) before any
   dependency lands; keep them optional (no default install).
3. Layer-2 proposer adapter (Basic Pitch first) emitting draft rows with
   a `propose` marker for the human.
4. Editor UX is out of scope here (authoring tool is a separate track).
5. chart_scribe duration serialization: bias-corrected duration + raw
   end + `end_faded` columns (see s3b).
