# Player-facing quickstart in the root README

Labels: `docs`, `good first issue`
Size: S

## Context

The root [README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)
currently reads for builders and contributors: architecture, CMake
commands, milestones. The game's own
[game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md)
is done and good, but the release checklist in
[game/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/TODO.md)
still lists *"the root README for players"* alongside the demo video: a
player who lands on the repository front page should see themselves
addressed within a few seconds: what kan! is, what they need
(instrument + any microphone), how they get playing, and how to share
levels.

The "Join in" section already invites contributors; the gap is the
player's first paragraph and path.

## What to do

1. Add a short player-facing block near the top of the root README (or
   restructure the intro slightly, keep changes small and
   review-friendly):
   - one paragraph for players: play songs on your own instrument, any
     instrument, a plain microphone is enough; free, offline, no
     account;
   - **Quickstart for players:** download → (build from source today,
     with a link to `game/README.md` until installers exist) → choose
     instrument → mic test → play an easy level;
   - where levels come from today (sharing files; bundled free levels as
     they land) and where the community lives;
   - keep the existing contributor content intact and linked from
     "Join in".
2. Tone check against the project's voice: calm, plain, inviting,
   "practice made fun", never hype or pressure.
3. Cross-check every claim against reality (nothing that does not work
   today may be promised; offline/no-account is real today, keep it
   that way in the wording).

## Acceptance criteria

- [ ] Root README addresses players within the first screen of text
- [ ] A concrete quickstart path (what to install/run today) with links
      to `game/README.md`
- [ ] Instrument + plain-microphone promise stated plainly
- [ ] No claim that does not hold today; contributor sections unchanged
      and still linked
- [ ] Wording reviewed against "calm by design" (no hype, no pressure)

## Pointers

- [README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md):
  current structure (Roadmap, Ecosystem Naming, Join in)
- [game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md):
  the game's player-oriented description to draw from ("What it does")
- [CONTRIBUTING.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/CONTRIBUTING.md):
  English-only policy and tone expectations

## Notes

- Good first issue: docs-only, small diff, big visibility.
- If installers/releases are not public yet, say "build from source"
  honestly and link the demo video issue's output when it exists.
