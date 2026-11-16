# libkan summit: from the bench to the game

What has to be built so the game judges a polyphonic level with the
method the truth bench proved (`pair`), and in which order. Companion of
`libkan-summit-plan.md` (findings, numbers) and
`libkan-summit-engineering.md` (rules). Every number here comes from the
plan's findings of 2026-11-16.

## 1. What is proven and what is not

Proven in the bench, offline posteriors, real piano (MAESTRO):

- right notes accepted 98.0 % on the held-out list, attack inside the
  perfect window 95.8 %;
- with the protection by doubt: zero misses charged on right play, on
  both lists and on the three local expert levels;
- wrong notes credited as right: 8 / 11 / 35 / 27 % (semitone, tone,
  octave, not played) on the held-out list.

Not proven yet: the same numbers live (the model runs on the whole
recording in the bench), a detuned instrument beyond 20 cents, winds and
bowed instruments on the polyphonic path (they keep the one-voice
path), more than 10 notes sounding.

## 2. The method, as the library must do it

Per chart note, in this order. Names are the bench's
(`game/forge/native/truth_path_pair.hpp` is the reference
implementation and its selftest the reference test).

1. **Evidence.** Basic Pitch onset and note posteriors, 88 pitches, one
   frame every 256 samples at 22050 Hz (about 11.6 ms).
2. **Peaks.** Per pitch, a local maximum of the onset posterior of at
   least 0.15.
3. **Running time offset.** Median offset of the last 15 unambiguous
   notes (one peak of at least 0.3 of the pitch in the meh window)
   written at least 300 ms earlier.
4. **Claim.** The unclaimed peak of the note's pitch in its meh window
   with the highest `value * (1 - min(1, d / meh))`, `d` measured from
   the score time plus the offset. Gives `s` and the attack time.
5. **Sustain.** `nmax`: the highest note posterior of the pitch from the
   attack to the written end, at most 1000 ms. `na`: its mean over the
   12 frames after the attack.
6. **Rival.** The strongest unclaimed peak at 1, 2 or 12 semitones in
   the perfect window (not a double of a claimed peak): `u`, and `un`
   its note posterior mean over 12 frames.
7. **Fact.**
   - `real` = `u >= 0.3` and `un >= 0.15` and `u >= s + 0.3`;
   - accepted when (`nmax >= 0.13` and not (`real` and
     `un >= nmax + 0.15`)) or (`s >= 0.25` and not `real`);
   - refused, `clear`: `real` and `u - s >= 0.6` and
     `un - nmax >= 0.4`. The fact names the rival's pitch;
   - refused, `grey`: every other refusal.
8. **Doubt.** For every chart note, known when the chart is set:
   `doubt = sigmoid(-6.35 + 3.01 * max(0, 48 - midi) / 12
   + 0.27 * max(0, midi - 84) / 12 - 1.34 * ln(min(pace_ms, 400) / 100)
   + 1.02 * min(sounding, 10) / 5 + 0.14 * min(chord_size, 6) / 3)`.
   `pace_ms` is the time to the nearest chart note more than 30 ms
   away, `sounding` the chart notes alive at the note's start,
   `chord_size` the chart notes starting within 30 ms. The coefficients
   are calibration data of the evidence source and ship with it.

Deadlines in stream time: the provisional fact (attack seen, key may
light) when the claim's peak is `peak + 2 frames` old; the final fact
when the later of the rival's sustain window and the note's own
12 frames has passed (about 170 ms after the attack), or when the meh
window closes with no claim. `nmax` may still rise until the written
end: a note refused grey at its deadline is upgraded to accepted if
`nmax` reaches its bar before the end (an event, never a downgrade).

## 3. What a final fact carries (C ABI, added at the end)

`chart_note_id`, `state` (accepted, refused_clear, refused_grey,
not_measured), `attack_stream_ms`, `time_deviation_ms` (attack against
the chart time through the clock map, the running offset given
separately as `player_offset_ms`), `pitch_deviation_cents` (against the
instrument's running tuning; `NaN` until package 9 gives the tracker a
reading), `rival_midi` (0 when none), `support` (`s`), `sustain`
(`nmax`), `doubt`. Provisional facts carry the id and the attack time
only. Existing events keep their layout and meaning.

## 4. The policy layer (game, `game/core/Play/`)

A pure function from facts to grades, with one hidden state.

- accepted: graded perfect, ok or meh from `time_deviation_ms` and
  `pitch_deviation_cents` with the judge's existing windows and
  ellipses (`LiveJudge.cs`). Settling of a blown note is not a
  deviation: pitch is read after the profile's settle time.
- refused_clear: a miss.
- refused_grey: if the hidden budget has a token, spend it and judge
  as not measured (no score, no miss, the combo stays); else a miss.
- not_measured: as today.
- Budget: capacity 5, starts full; every chart note adds
  `12 * doubt` when its fact arrives, whatever the fact. Never drawn,
  never stored in a result or a replay header. Mods that ask for
  strictness may set the capacity to 0.

## 5. Work packages, in order

| # | Package | Owner | Done when |
|---|---|---|---|
| 4f | Bench: `clear`/`grey`, doubt, the policy check with the budget, levels as a manifest | B | the bench prints the plan's tables of 2026-11-16 |
| 5 | libkan `ChartMeasurer`: chart store, clock map, lanes, steps 2 to 8 on posteriors given by a source interface; offline tests with synthetic posteriors; a `chart` bench path equal to `pair` note for note | B, reviewed by A | `chart` equals `pair` on both lists |
| 6 | Streamed Basic Pitch as a source: incremental frames with at most 100 ms of look-ahead; equal to offline within 0.5 points; cost measured pinned | B | bench `chart-live` within 0.5 points and inside two threads in real time |
| 7 | C ABI: set chart, clock map, lane events of section 3; forge native selftest | B | selftest green, old events byte identical on the one-voice dumps |
| 8 | Game: feed the chart, consume facts, policy of section 4, keys light on provisional facts; isolated instance with `KAN_LIVE_INPUT` on the three levels and autoplay; ocarina `bench-play.sh` | B, reviewed by A | zero misses on the three levels through the game |
| 9 | Tuning tracker in `ChartMeasurer` from the contour's peak bin; detuned controls at 20, 40, 60 cents equal to the original | A then B | detune rows within one point |
| 10 | Evidence for fast octaves at the ends of the keyboard (harmonic check for claimed-weak notes), to cut the leniency on virtuoso levels | A | wrong notes that end as a miss rise on the Etude and the Liszt with zero misses kept |
| 11 | Documents: issue 41, validation status, libkan README | B | |

Rules for all of them: the engineering guide. Bounded builds. The game
only in the isolated instance, never a microphone. MAESTRO stays under
`tools/dataset_bench/data/`.
