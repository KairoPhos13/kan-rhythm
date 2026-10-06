# Range coverage — A0 to C8 (the complete piano range)

Status: Milestone 1 range gate (measured offline, `libkan/tests/range_test.cpp`;
reproduce: `ctest --test-dir build -R range`). All 88 keys MIDI 21–108
(27.5–4186.5 Hz) through the full stack: stage 0 → onset → profile blanking →
score-informed confirmation → YIN sustain → BACF.

## Coverage table (8-partial 1/h plucked model, 8 ms attack)

| octave band | keys | onset | confirmed | YIN | confirm avg/max ms | confirm err max (c) | YIN err max (c) | physics floor ms |
|---|---|---|---|---|---|---|---|---|
| 27.5–51.9 Hz | 12 | 12 | 12 | 12 | 64.0 / 88.0 | 5.6 | 6.1 | 59.5 |
| 55.0–103.8 Hz | 12 | 12 | 12 | 12 | 32.2 / 42.7 | 10.1 | 6.7 | 37.5 |
| 110.0–207.7 Hz | 12 | 12 | 12 | 12 | 22.0 / 24.0 | 35.5 | 8.5 | 22.8 |
| 220.0–415.3 Hz | 12 | 12 | 12 | 12 | 17.1 / 18.7 | 9.0 | 3.3 | 15.4 |
| 440.0–830.6 Hz | 12 | 12 | 12 | 12 | 13.8 / 16.0 | 16.1 | 5.7 | 11.9 |
| 880.0–1661.2 Hz | 12 | 12 | 12 | 12 | 13.3 / 13.3 | 5.2 | 5.9 | 10.6 |
| 1760.0–3322.4 Hz | 12 | 12 | 12 | 12 | 12.2 / 13.3 | 15.1 | 8.8 | 10.0 |
| 3520.0–4186.0 Hz | 4 | 4 | 4 | 4 | 10.7 / 10.7 | 24.6 | 9.8 | 9.7 |

**Verdict: the complete piano range is covered** — every key detected,
every key confirmed with correct semitone identity, YIN sustains within
10 cents everywhere. This includes bass ocarinas, bass guitars and
counter-bass instruments (27.5–60 Hz) and the extreme top (C8).

## The immediate-feel question

Instant reward = the onset event (rhythm judgment) + fast semitone
confirmation. Onset fires in 1–2 blocks (block-quantized +1.33 ms measured).
Confirmation latency is period-limited (below) but stays inside the
simultaneity envelope (<35 ms, SPECIFICATION §2) from ~A1 upward; the
sub-100 Hz instruments land 32–88 ms for confirmation, which still reads
as "instant" for note identity on sustained bass notes (the onset — the
rhythm reward — is instant everywhere). Nothing blocks the game-feel
objective.

## Physics walls (documented, not fought)

1. **Confirmation latency floor = blanking + ~1 period + overlap.** A note
   cannot be pitch-confirmed faster than its own period allows: A0 (27.5 Hz)
   needs ~36 ms of tone minimum; measured 64–88 ms with the 8 ms blanking.
   Bands above ~110 Hz confirm in ≤24 ms. This is signal physics, not an
   implementation gap — the road to sub-floor bass confirmation would be
   pitch *prediction* from the attack transient, a research item, not M1.
2. **Wide correlation peak on sparse harmonic stacks.** With only 4 partials
   the NSDF peak at low fundamentals is broad (peak width ∝ 1/highest-partial),
   giving 30–50 cent scatter on the fast confirm estimate (semitone identity
   still correct). Real instruments with 8+ partials measure ≤10 cents. The
   fast estimate is confirmation-grade; cents-level intonation comes from the
   sustain trackers (YIN/BACF columns).
3. **YIN window sizing below ~95 Hz.** Blind YIN with a 2048 window cannot
   resolve periods longer than 1024 samples (<47 Hz). The sweep grows the
   window per note (≥2 periods); apps must do the same (window ≈ 4 periods,
   capped 16k) or rely on the score-informed matcher, whose window scales
   with the expected note automatically.
4. **YIN lag-range truncation above ~3.5 kHz.** The ±2-semitone band spans
   only ~3 integer lags at C8; a +2-lag margin on tau_max was required
   (fixed in dsp_detector.hpp) or the peak falls outside the scan.
5. **Spectral-envelope dependence.** Detectors on fundamental-weak spectra
   (low piano notes radiated through a mic, some bass instruments) lean on
   harmonics; the harmonic-robust paths are the NSDF matcher and BACF.
6. **Aliasing at the very top.** Partials must be synthesized (and, in
   playback, band-limited) below ~16 kHz; at C8 only 3–4 partials exist in
   band. Nothing in the detector assumes a fixed partial count.

## Engineering notes found by the sweep

- **Retrigger accounting**: a decaying note can retrigger the onset
  detector; per-note analysis must latch the FIRST onset or the clean
  window restarts and confirmations drop (the bug that made YIN appear to
  miss 26/88 keys before the fix).
- The profile's blanking must cover the instrument's attack (8 ms pick
  transient ↔ 8 ms blanking; real hammers ≈ 4 ms ↔ keyboard's 6 ms).
- BACF (Cycfi Q) confirms 82/88 keys in this sweep — the bounded matcher
  is the primary confirm path, BACF the steady-tracking option.
