# Verify the macOS build and play-test kan! on macOS

Labels: `platform:macos`, `game`, `help wanted`
Size: M

## Context

kan! targets Linux, Windows and macOS
([README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)),
but macOS is the least-verified platform: "macOS if possible" is the
release item in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
and [game/README.md: Status](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md)
calls the macOS build unverified. The stack is a .NET 10 fork of
osu!lazer plus a native C++ bridge (`game/forge/native/`, miniaudio
capture through libkan). macOS is also the platform where microphone
permission prompts and audio device behaviour are most likely to surprise
players, worth finding out before they do.

## What to do

1. Build on macOS (Apple Silicon and/or Intel, say which) using the
   commands in
   [game/docs/environment.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/environment.md);
   record tool versions, warnings (target: 0 warnings / 0 errors) and
   run results.
2. Play-test with a real instrument and the built-in or an external mic:
   - first launch: what does the microphone permission prompt look like?
     What happens if it is denied? Is the failure clear and kind?
   - play one easy level end to end;
   - open the editor's track tab once (audio import + waveform preview);
   - check the about screen shows kan! and `THIRD_PARTY_NOTICES`.
3. Verify brand behaviour: kan! logo/backgrounds load (F-21), the intro
   says "welcome to kan!" with no osu! voice (F-22), and no request to
   any ppy.sh host appears in the logs (F-20).
4. Record everything in `game/docs/environment.md`; file bugs as
   separate issues.

## Acceptance criteria

- [ ] Build green on macOS (chip recorded): 0 warnings / 0 errors
- [ ] Client runs: one easy level played end to end with a microphone
- [ ] Microphone permission flow observed and its behaviour documented
      (prompt, denial path)
- [ ] Brand/about-screen checks pass; logs show no ppy.sh traffic
- [ ] `game/docs/environment.md` updated with results; issues filed

## Pointers

- [game/docs/environment.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/environment.md):
  platform notes to extend
- [game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md):
  `make` targets and the isolated-test-instance rule
- [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md):
  F-20/F-21/F-22 expected behaviours
- `libkan` capture path (miniaudio): device enumeration and permission
  behaviour live at that boundary

## Notes

- Apple signing/notarisation for distribution is a pending maintainer
  decision; this issue verifies local builds and play.
- If audio latency on macOS is notably worse than on Linux, measuring
  and reporting it (see
  `docs/dsp/latency-measurements.md`) is already a great contribution.
