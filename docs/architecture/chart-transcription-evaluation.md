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

## 3d. Instrument-specific separation — can we section other instruments?

**User field calibration (2026-10-12, by ear on the sample song):**
ocarina lead lands in `other` (clean); `vocals` holds the human voice
with the ocarina attenuated ~-30 dB (bleed); `sin other` = piano + voice
(+ a little ocarina bleed) = the usable karaoke backing for ocarina
levels. Record this when choosing stems per instrument pack.

**Full project comparison** (search was unavailable; sources marked
[verify] for license/quality checks before adoption):

| project | separates | license | community adoption | bleeding edge (vs SOTA) | role |
|---|---|---|---|---|---|
| **Demucs / htdemucs_6s** (Meta) | 6 fixed categories | **MIT** | very high — de facto standard for years (~10k+ stars, used everywhere) | htdemucs was SOTA ~2022-23; **still strong but RoFormer models have edged past it** on recent benchmarks | **adopted** (default: robust + permissive) |
| **UVR** (model zoo: MDX-Net/VR/RoFormer) | per-model: vocals, karaoke, instrument-specific | code MIT [verify]; **model zoo mixed** [verify] | **highest** in the practical scene (~30k+ stars; the karaoke/transcriber standard) | the PLATFORM tracks SOTA — hosts competition-winning models | via `KAN_SEPARATOR` for specialized splits |
| **Mel/BS-RoFormer** (ZFTurbo impls.) | instrument-HQ variants, vocals | code MIT [verify]; **training-data-derived models vary** [verify] | high and rising in the separation community (MDX competition lineage) | **current top of leaderboards** for instrument/vocal splits [verify] | preferred for guitar/piano/violin splits |
| **MDX23 / Bandit** (competition models, Microsoft lineage) | instrument stems | mixed / research [verify] | moderate (research code, competition circles) | competition-grade SOTA at their release; RoFormer now rivals | radar |
| **Cassette-type zero-shot** (Sony) | ANY instrument by text query | research [verify] | small (young research) | **unique capability** (zero-shot names), raw quality below specialized models | the future of arbitrary names |
| Spleeter (Deezer) | 2/4/5 fixed | **MIT** | historically huge (~10k+), **declining/aged** | outdated (2019-era quality) | rejected |
| Open-Unmix (SIGSEP) | 4 fixed | **MIT** | moderate (research) | dated | rejected |
| **Score-informed extraction (OURS)** | exactly the chart's instrument | **project's own** | n/a (ours) | not a blind-separator competitor: **uniquely exact when the chart exists** (deterministic, zero AI deps) | **build (differentiator)** |

Reading guide: for MASS use the license column decides distribution (MIT
rows can ship; model-zoo/data-derived weights need per-model review —
the weak-label rule also applies to model provenance). Adoption tells you
how battle-tested the tool is on real-world messy audio; bleeding-edge
tells you the quality ceiling. Search was unavailable at writing
(stars/leaderboard positions are from prior knowledge: verify before
adoption).

### Verified license reality (2026-10-12, HF + GitHub API checks)

The MIT story is more nuanced than the table's code column suggests:

- **Architecture code:** `lucidrains/BS-RoFormer` = **MIT** (Phil Wang,
  2023) — verified from LICENSE.
- **Community weights (HuggingFace):** `pcunwa/Mel-Band-Roformer-Inst`,
  `empz/mel_band_roformers`, `becruily/mel-band-roformer-karaoke` —
  **license UNSPECIFIED** (no README, no card metadata).
  `noblebarkrr/all_models_for_mel_band_roformer` (the big collection:
  dereverb, denoise, karaoke...) declares **GPL-3.0**.
- **Channel count is per-checkpoint, not architectural:** karaoke models
  = 2 stems (vocals/instrumental); the `Inst` config inspected declares
  `num_stems: 1, target_instrument: other`; full instrument models in the
  ecosystem go up to ~6 (drums/bass/guitar/piano/strings/vocals). **No
  checkpoint has a wind/ocarina class.**
- **Leaderboards:** Music Demixing Challenges (MDX'21/'23, AIcrowd) with
  SDR tables, SiSEC MUS campaign, and community benchmark tables (e.g.
  ZFTurbo's SOTA list) [verify — search outage].

**Consequence for mass use (decided):** RoFormer-class models are an
OPTIONAL enthusiast layer (`KAN_SEPARATOR`) until each checkpoint's
provenance is cleared in `LICENSES.md` — MIT code + unspecified/GPL
weights cannot ship as a default in a mass-use product. Quality is
genuinely higher, so testing on our ocarina case is still worthwhile:
the test measures quality, the license gate governs distribution.

### Vulkan / GPU generalization — answered (2026-10-12)

**Yes, Vulkan inference is possible today.** The vehicle found and
verified: `chenmozhijin/BSRoformer.cpp` (MIT, 2026) — GGML-based C++
inference for BS/Mel-Band-RoFormer:

- **CPU + CUDA + Vulkan** acceleration via GGML backends (Vulkan 1.4
  detected on this machine; ggml-vulkan needs the `glslang` dev library
  to compile here — one package away).
- **GGUF model format with quantization** (FP32/FP16/Q8/Q5/Q4): the
  6-stem F16 model is **334 MB** vs the 1.5 GB PyTorch ckpt.
- Pre-converted GGUF zoo on HF (`chenmozhijin/BSRoformer-GGUF`,
  `studio409/BSRoformer-SW-6Stem-GGUF` — 6 stems!).
- **No torch at runtime** — the mass-use dream backend.

**Even better for this machine: GGML's HIP backend (ROCm)** — the user's
AMD GPU would accelerate without Vulkan or CUDA layers at all.

Verdict: `BSRoformer.cpp` becomes the **preferred RoFormer backend**
(quality + tiny deps + cross-vendor GPU); Demucs stays the robust
default; the PyTorch/roformer-separation path is demoted to a fallback
(it needed config-yaml surgery: `!!python/tuple` tags and a namespace
bug — integration friction worth avoiding).


**Vulkan VALIDATED on real hardware (2026-10-12, user-requested test):**
`bs_roformer-cli` built with `GGML_VULKAN=ON` (Vulkan 1.4.363; needed the
`spirv-headers` system package, installed by the user) processed the
full 3:40 song in **43.6 s (~5x real time)** producing 6 stems, GPU
kernel stable. vs CPU (multi-minute, OOM-killed in a parallel-build
scenario). The 10-15 min level-creator budget is exceeded by an order of
magnitude — the "fast Demucs now / quality rebuild later" tradeoff may
be unnecessary when the GGUF+Vulkan path is this fast. Stems delivered
as `roformer_stem0..5.wav` for ear comparison against Demucs outputs;
stem-name mapping to be identified by listening (model zoo names differ
from Demucs categories).

### Decisions from the user's listening session (2026-10-12)

- **Faithful recomposition (measured, not assumed):** the user verified
  by ear that RoFormer stems recombine to the original mix "to the
  millimeter" — an apparent level shift was a perception artifact. The
  proposed per-stem gain calibration (least-squares fit to the reference
  mix) is therefore NOT needed today; recorded as an optional capability
  should some future model normalize its stems.
- **Both backends stay (user decision):** RoFormer-GGUF/Vulkan is
  clearly "the way" for quality (vocals/piano clean, defects cut ~90%,
  even reconstructs noisy piano); **Demucs stays as the low-budget path
  so everyone can participate.** Distribution strategy: torch =
  training/preparation only; shipped inference = GGUF + Vulkan (bs_roformer-cli,
  code MIT + model MIT — the first fully distributable combination).
- **Official stem order (elicwhite/bs-roformer-sw-6stem-onnx, MIT):**
  0:bass 1:drums 2:other 3:vocals 4:guitar 5:piano — user's ear agrees
  (stem2 = winds/ocarina, stem3 = voice, stem5 = piano). `other` also
  holds unclassified accompaniment (a plucked string instrument heard in
  the sample song).
- **Game editor options (user ideas, game-side):** per-stem volume to
  taste; "enable the accompaniment stem only while the main instrument
  is silent"; and optional removal of unclassified accompaniment while
  keeping the rest.

### Roadmap (decided)
### Roadmap (decided)

1. **Now:** Demucs 6s; per-instrument stem mapping in the pack data
   (ocarina/flute -> `other`; piano -> `piano`; ...), calibrated by ear
   per instrument pack.
2. **Next:** `KAN_SEPARATOR` adapters for RoFormer/MDX instrument models
   (guitar, piano, violin splits) — optional layers per the mass-use
   policy.
3. **Our own path (unique):** *score-informed extraction* — once the
   chart exists (our transcription!), build a time-frequency mask from
   the transcribed pitch tracks and resynthesize the target instrument.
   Deterministic, zero AI dependencies, and it separates EXACTLY the
   instrument the level is for (the mash/bleed problems we measured
   become the mask's job). Best fit for winds/monophonic leads.
4. **Later:** custom-trained separation per instrument pack (fine-tune on
   our own controlled recordings — the InstrumentProfile concept extends
   to separation), and zero-shot text-query models when mature.

## 4. Named TODOs

1. `tools/chart_scribe` (Layer 1): blind facts -> chart text with
   onset/duration/pitch + (vibrato)/(bend)/dynamics annotations;
   selftest = synthesize -> transcribe -> verify-score roundtrip.
2. Verify candidate licenses/versions (search outage) before any
   dependency lands; keep them optional (no default install).
3. Layer-2 proposer adapter (Basic Pitch first) emitting draft rows with
   a `propose` marker for the human.
4. Editor UX is out of scope here (authoring tool is a separate track).
5. Score-informed extraction (s3d roadmap item 3): TF mask from our
   pitch facts -> deterministic target-instrument stems.
6. KAN_SEPARATOR adapter for bs_roformer-cli (GGUF backend) + HIP build
   for ROCm machines; PyTorch RoFormer as last-resort fallback.
5. chart_scribe duration serialization: bias-corrected duration + raw
   end + `end_faded` columns (see s3b).
