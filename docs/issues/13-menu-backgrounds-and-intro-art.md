# Hand-made menu backgrounds and intro art (brand folder)

Labels: `art`, `help wanted`
Size: M

## Context

kan! currently shows eight menu backgrounds and the intro visuals
generated from scratch by `game/tools/gen-provisional-brand.py` (fork
F-22): procedural geometry that keeps the release clean of osu!'s art,
but it is placeholder work. The root
[README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)
invites artists to make kan!'s identity real ("logo, menu, intro: see
game/brand/"), and
[assets/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/assets/TODO.md)
lists an identity kit (palette, typography, skin shell) as release
polish.

Everything needed to land art is in place: files in
[game/brand/](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md)
replace osu!'s resources one at a time (fork F-21), and a missing file
simply falls back to the current behaviour, so contributions can land
incrementally, one background at a time.

## What to do

1. Read [game/brand/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md)
   for exact names and sizes. The main deliverables:
   - `Textures/Menu/menu-background-1.jpg` … `-8.jpg`, 2732×1537: the
     main-menu backgrounds (a set that shares one mood is ideal;
     contributing fewer than eight is fine, they fill in over time);
   - `Textures/Intro/Triangles/logo-background.png` and
     `logo-highlight.png` (1295×1295): the "triangles" intro's logo
     fill and light;
   - `Textures/Intro/Welcome/welcome_text.png` (750×156): the
     "welcome" intro's text art.
2. Design brief: **calm**. kan! is "forgiving by default … never aims at
   anxiety or compulsion" (root README); menu art is the first thing a
   player sees and should feel like a quiet room with an instrument in
   it, not a slot machine. Warm golds/silvers of the kan! logo are the
   anchor; clarity matters more than detail (the UI draws over these).
3. Deliver as named in the brand folder, from scratch (never copy, trace
   or closely imitate osu!'s art: CC BY-NC 4.0 and trademark), and
   record author + licence (CC0/CC BY preferred) in the brand credits.

## Acceptance criteria

- [ ] At least one menu background delivered at 2732×1537 with the exact
      file name, visible in the running game
- [ ] (Bonus) triangles intro fill/highlight and/or welcome text art
      delivered at the specified sizes
- [ ] 100% original work; no osu! asset copied, traced or closely
      imitated
- [ ] Author + licence recorded in the brand credits
- [ ] The set reads as one mood (or the plan to complete the set is
      written down)

## Pointers

- [game/brand/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md):
  file table and fallback rule
- `game/tools/gen-provisional-brand.py`: current placeholders
  (reference for palette/mood)
- [assets/TODO.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/assets/TODO.md):
  identity kit v0 (palette, typography, skin shell)
- Fork F-21/F-22 in
  [game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)

## Notes

- JPEG backgrounds at 2732×1537 are a few hundred KB each; check the
  asset rules in
  [assets/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/assets/README.md)
  for anything unusually large (Git LFS discussion threshold).
- One background is a complete, mergeable contribution; do not feel
  obliged to deliver all eight.
