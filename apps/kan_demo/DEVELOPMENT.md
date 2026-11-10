# kan_demo: development context (for the agent polishing this app)

Read this before touching `kan_demo`. It records **what exists, why it is
shaped this way, and which direction further work must keep**, so polish
lands consistently with the project's goals instead of re-deciding design
on the fly. User-facing usage lives in [README.md](README.md); task state
in [TODO.md](TODO.md). The DSP contracts this app consumes are documented
authoritatively in [docs/dsp/README.md](../../docs/dsp/README.md).

## 1. Why this app exists

kan_demo is the **real-instrument reality check** for the Milestone-1
detection stack: grab an ocarina/piano/guitar, run one command, and *feel*
whether the engine delivers the project's core promise:

> **immediate game feel**: instant reward as the player correctly plays a
> real instrument.

It is also the live proving ground for the features the game will consume
(score-informed confirmation, detuning compensation, technique readout).
It is **not** the game: no scoring, no chart, no visuals beyond a terminal
line. Those belong to `game/` (Milestone 2).

## 2. Current feature inventory (exact behavior)

| flag | behavior |
|---|---|
| `--instrument <name>` | `ocarina` (default) · `flute` · `recorder` · `guitar` · `piano`. App-level presets map onto the plain-data `kan::InstrumentProfile` (family, attack, range, blanking), the same struct `charts/spec` will serialize. Ocarina = 480-1450 Hz wind profile (15 ms blanking). |
| (default) | Free tuner: onset flash, then live note / Hz / cents / stability bar / time-since-onset. |
| `--engine yin\|bacf` | Pitch engine for the tuner. **Default `yin`** (rolling 2048-window YIN, robust). `bacf` = Cycfi Q streaming detector. See §4.B. |
| `--expect <note>` | Score-informed mode: `BoundedPitchMatcher` confirms the charted note (`C5`, `F#5`, …) on the clean post-blanking window; reports confirmation latency and cents. This is the exact code path the game calls per chart note. |
| `--calibrate` | 2 s reference-note flow (`DetuneCalibrator`) → prints Δc and **applies** it (`TuningCompensation::set_global`): the tuner then shows residual vs the instrument's own tuning, and `--expect` targets shift by the same Δc. |
| `--vibrato` | Live technique readout: `VibratoAnalyzer` over the per-note cents trajectory → "vib 5.1 Hz +33 c" or "vib --". Trajectory is cleared at each onset. |
| `--csv <file>` | Per-**update** rows: `t_ms,note,hz,cents,vib_hz,vib_depth_c`. This is a raw live trace, NOT the aggregated per-note descriptor (that is `kan_analyze`'s job, same feature vocabulary). |
| `--events <f.jsonl>` | The **per-note description stream** (libkan `note_stream.hpp`): HIT (fixed at the onset) → provisional pitch (display-only) → CONTENT (mono/polyphonic + per-candidate presence/cents/amplitude when `--candidates` is given) → final pitch or honest `unresolved` → EXPRESSION → CLOSE. Progressive on screen; JSONL on disk with the same schema as `kan_analyze --events` (live/offline diffable). |
| `--input <wav>` | **Offline validation of the live path**: the exact live consumer runs over a recording (no audio device), same events, same schema, same code path as a live session. Fulfils the old `--loopback` wish with the demo's own pipeline. |
| `--candidates <f>` | Candidate hypotheses per attack (`@<onset_ms> midi ...` binds to the attack at that time; plain `midi ...` for the next attack; include notes still sustaining from earlier attacks). Conditions the CONTENT measurements; absent hypotheses get low presence, never invented facts. In `--expect` mode the expected note is supplied automatically (the game's score-informed candidate path). Worked example: `apps/kan_demo/examples/hallelujah-piano-tutorial.candidates`. |
| `--no-gate` / `--noise-cal <ms>` | Stage-0 noise gate bypass / room-silence calibration window (default 500 ms at startup). |
| `--selftest` | Offline pipeline check on a synthesized ocarina note (wired into ctest as `kan_demo_selftest`). |

Pipeline (consumer thread, fed by `LockFreeRingBuffer` from the capture
callback):

```
capture callback (push only, zero allocs)
  → Preconditioner (35 Hz HPF + adaptive noise gate, room-calibrated)
  → HybridDetector::process_onset_block (fast path, ~65 ms debounce)
  → stage-2 transient blanking (profile-driven: 6/8/15 ms)
  → YIN rolling window | BacfDetector | BoundedPitchMatcher (--expect)
  → display / CSV / DetuneCalibrator / VibratoAnalyzer
```

## 3. Design direction (keep these; polish must not break them)

1. **Instant-feel first.** The onset event is the instant reward; pitch,
   cents, vibrato are follow-up layers. Never add work that delays the
   onset path (the audio callback only pushes samples; that is
   non-negotiable, CONTRIBUTING §3).
2. **Thin consumer.** Detection logic lives in `libkan/`. If a polish needs
   new DSP (a better note-boundary rule, a stability metric…), implement it
   as a libkan API with an offline test, then consume it here. Never
   duplicate detector math inside the app.
3. **Honest reporting.** The demo must never fake or hide readings:
   calibration is *applied*, uncertainty is shown (BACF lock-in is
   documented rather than papered over), and numbers are measured, not
   claimed. If a corner case can't be handled, document it in README's
   "Known caveats".
4. **Profile-driven instrument behavior.** Blanking, ranges, judgment
   offsets come from `InstrumentProfile` (plain data = future instrument
   packs). Add new instruments as preset data, not new code branches.
5. **Score-informed where the game is score-informed.** `--expect` uses the
   bounded matcher because the chart knows the note. Don't add blind
   chord/polyphony analysis to the demo (out of scope by design,
   docs/dsp/README.md L2/L10).
6. **Terminal-first.** No graphics dependencies. The visual language of the
   game is `game/`'s Milestone-2 work.
7. **Everything reproducible offline.** Any behavior change must keep
   `kan_demo_selftest` green and add coverage when it changes observable
   output.

## 4. Known rough corners (polish targets, with context)

**A. Note-boundary behavior in the free tuner (top priority).**
Today the display "holds the reading until silence": the note name snaps on
change (±0.7 semitone hysteresis) and the line persists between pitch
updates. For runs of notes (sweeps, scales) the update rate is fine but the
*identity* of "the note being judged" is implicit; there is no explicit
note-start/note-end event in the display or CSV. Direction: expose the
note boundary (onset already fires per note; latch first-onset semantics
per docs/dsp/README.md §3 rule 1) and make the CSV emit per-note events
aligned with the `kan_analyze` descriptor schema so live traces diff
cleanly against reference descriptors. Do **not** change the detector
debounce to "fix" this; the boundary is bookkeeping, not DSP.

**B. BACF free-tuner lock-in.** `--engine bacf` can latch a wrong frequency
right after an onset (+80…+130 cents) and hold it for the note. Known
since the A/B work (docs/dsp/bacf-vs-yin.md); YIN is the default because of
it. Direction: investigate Cycfi Q's `median3`/bias warmup and/or gate BACF
with the YIN estimate until agreement; if fixed, keep YIN as default until
BACF wins the A/B bench again. Don't remove the `--engine` switch; the
comparison is valuable.

**C. Per-string calibration UI.** `TuningCompensation` supports Δc1..Δc6
(3 s guided open-string strum). The demo only drives the global Δc.
Direction: a guided `--calibrate-strings` flow (prompt per string, reuse
`DetuneCalibrator` per note). Keep the output format consistent with
`docs/dsp/calibration.md`.

**D. CSV richness.** Current rows are per pitch update (session time,
note, Hz, cents, last vibrato values). Wanted: per-note rows with duration,
attack/release, sustain ratio, reliability, i.e. the `kan_analyze`
descriptor fields (docs/dsp/README.md §4). Prefer emitting the same JSON
schema as `kan_analyze --json` from live sessions over inventing a second
format.

**E. Soak / CPU readout.** `--soak <minutes>` with underrun counter
(miniaudio counters) and CPU %: feeds the 60-min stability gate
(docs/dsp/latency-measurements.md, [H] item). Report, don't assert, and
mark hardware-vs-offline provenance in the output.

**F. Platform smoke.** Linux is the tested platform. Windows/macOS need
smoke runs (device init, latency profile) before any platform-specific
code is added; miniaudio already abstracts the backends.

**G. Latency display honesty.** The `(+X ms)` readout is time from onset to
confirmation as measured in the demo loop. Do not present it as the
hardware→event gate number; that measurement procedure is
docs/dsp/latency-measurements.md.

## 5. Testing & verification

- `ctest --test-dir build -R kan_demo_selftest` must pass after every
  change; extend `run_selftest()` when adding flags that affect the
  pipeline (it runs the same `Pipeline` path the live loop uses).
- Real-instrument smoke: run with `--csv` and compare the trace against a
  `kan_analyze` descriptor of the same performance (the two should agree
  within the baselines of docs/dsp/README.md §6.4).
- When a corner is polished, record the measured change in
  `apps/kan_demo/TODO.md` (tick the item) and, if it moves a baseline, in
  the corresponding `docs/dsp/` table.

## 6. Component relationships

| component | relationship |
|---|---|
| `libkan/` | owns all DSP; kan_demo consumes `precondition`, `dsp_detector`, `instrument_profile`, `bounded_pitch`, `bacf_detector`, `calibration`, `technique` |
| `apps/kan_cli/` | the benchmark/gates tool (loopback fixtures, latency report, detector A/B). kan_demo = live feel; kan_cli = measurement. Keep the split. |
| `apps/kan_analyze/` | offline song descriptor + reference runs. Live CSV (kan_demo) and descriptors (kan_analyze) must stay in the same feature vocabulary. |
| `game/` | future consumer of the same pipeline; the patterns established here (profile presets, calibration application, expect-mode confirmation) are the intended game-side shape. |
| `kanfx/` | consumes the detection tap (`tap.hpp`); the demo's monitoring is plain output, no FX graph. |

## 7. Build & run

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j
./build/apps/kan_demo/kan_demo --instrument ocarina --vibrato --calibrate --csv session.csv
ctest --test-dir build
```

Keep the build warning-clean (`-Wall -Wextra`) and the suite green
(16 tests at the time of writing). English-only in all committed content
(root CONTRIBUTING.md).
