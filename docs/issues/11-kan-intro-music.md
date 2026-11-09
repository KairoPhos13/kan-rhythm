# kan!'s own intro music

Labels: `art`, `audio`, `help wanted`
Size: M

## Context

The intro currently plays one of the player's own levels or starts
silent: fork F-22 muted osu!'s theme music because it is not ours (osu!'s
resources are CC BY-NC 4.0 and its name and logo are trademarks — see
[game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)).
The switch that plays a themed intro (`use_themed_intro` in the fork's
`IntroScreen.cs`) is deliberately off until kan! has its own track.

kan! deserves a first sound of its own — and the root
[README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)
explicitly invites artists to make it ("Art and sound for kan!'s own
identity too (logo, menu, intro)"). This is an artist-first task, not a
programming one: the file placement is already solved.

## What to do

1. Compose and record (or sequence) a short original intro theme. Design
   brief:
   - **calm by design**: the intro is a welcome, not a hype moment — no
     compulsive earworm pressure, no adrenaline cues (see "Ground rules"
     in [game/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/README.md));
   - 5–20 seconds, loops or fades cleanly; fits a rhythm game played on
     real instruments (an acoustic-friendly palette suits the brand:
     the logo is a white ring fading to gold);
   - original work only, or a recording you can release under a free
     licence (CC0, CC BY or public domain; record which).
2. Package it as a beatmap set holding the track, as
   [game/brand/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md)
   specifies: `Tracks/triangles.osz` (and/or `circles.osz`,
   `welcome.osz`) in `game/brand/`.
3. Ask a maintainer to flip `use_themed_intro` back on in
   `IntroScreen.cs` (fork change, logged in
   `game/docs/fork-divergence.md`) and to record the track's credits.
4. Optional companions: a soft exit sound for the "see ya" moment
   (`Samples/Intro/…` in the brand folder) — current osu! voices are
   never played (F-22).

## Acceptance criteria

- [ ] An original (or freely licensed) intro track exists, with licence
      and author recorded
- [ ] It is packaged as `game/brand/Tracks/*.osz` per
      `game/brand/README.md` and plays in-game
- [ ] `use_themed_intro` behaviour verified: the intro plays kan!'s
      music (or the player's own level), never osu!'s theme or voice
- [ ] Credits recorded in the appropriate notices/credits file
- [ ] The fork delta is logged in `game/docs/fork-divergence.md` (by a
      maintainer)

## Pointers

- [game/brand/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md) —
  `Tracks/` naming, `Samples/Intro/`, the `use_themed_intro` switch
- [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md) —
  F-22 (what was muted and why)
- [assets/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/assets/README.md) —
  asset licensing ground rules (provenance + licence, no GPL/AGPL)

## Notes

- Never sample, cover closely, or derive from osu!'s theme music —
  trademark and licence hygiene, and the project's own identity.
- If you want to contribute several short candidates, that is welcome;
  the maintainer picks the final one.
