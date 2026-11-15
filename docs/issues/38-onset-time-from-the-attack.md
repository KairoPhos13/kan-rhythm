# Note times from the attack, not from when the note is recognised

Labels: `libkan`, `game`, `timing`
Size: M

## Context

Owner QA of v0.1.1 (2026-11-14), calibration with a real ocarina: in some
cases recognition lags noticeably, and the game shows the attack as late
even though it was played on time; the delay was the recogniser's, not
the player's. Calibration then measures that delay as part of the
microphone's, and judging calls the player late.

The November 2026 audit (`kan-audit-2026-11.md`, section 2) explains the
likely causes:

- **N4:** the onset rule compares against a moving average with a time
  constant of about 10 ms, so an attack that rises more slowly than about
  60 ms (a soft-tongued wind note, a bowed note) is detected late, at the
  point where the rule finally fires, not where the sound began.
- **N11:** event time is the sample count mapped to the level through a
  wall-clock stopwatch with no resync; drift and scheduling add to the
  lag. On the Steam Deck the first play places everything early and a
  restart fixes it (issue 39), which points at the start of the mapping.

Fixes stay generalist (every instrument, never one instrument's special
case) and live judging stays forgiving by default.

## What to do

1. Reproduce with recordings: a set of attacks with known onset times,
   both synthetic (fast and slow rises, from 5 to 150 ms) and real (the
   owner's ocarina takes). Measure detected time minus true onset per
   attack. This is the baseline.
2. Date each note back to where its attack began: once a note is
   confirmed, search back in the recent audio for the start of the rise
   (energy above the noise floor measured in calibration), and report
   that time. The recognition can still take as long as it needs; only
   the reported time moves.
3. Map sample time to the level time without drift (N11): resync the
   stopwatch mapping against the audio clock regularly.
4. Calibration's microphone-delay step then measures the microphone and
   the audio path only, and shows the attack where it was played.

## Acceptance

- [ ] An offline test (repo rule 5): for synthetic attacks with rises from
      5 to 150 ms, the reported onset lies within 10 ms of the true onset.
- [ ] On the owner's ocarina takes, the mean onset error and its spread
      drop against the baseline (numbers in the PR).
- [ ] Calibration on the owner's setup no longer shows on-time attacks as
      late.
