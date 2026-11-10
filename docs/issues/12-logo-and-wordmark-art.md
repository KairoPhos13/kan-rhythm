# Hand-made kan! logo and wordmark (replace the provisional art)

Labels: `art`, `help wanted`
Size: M

## Context

kan!'s current identity is provisional: the logo, app icons and menu art
are generated from scratch by `game/tools/gen-provisional-brand.py`
(geometry only, no font) following the owner's design: a white ring
whose disc fades to gold at the centre, "kan!" in white with silver
borders (fork F-22,
[game/docs/fork-divergence.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/docs/fork-divergence.md)).
Generated art carries the release today, but kan! is "by the community,
for the community", and the root
[README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)
invites artists to make the real thing.

The hook is ready: files in
[game/brand/](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md)
replace osu!'s resources one file at a time (fork F-21). What is missing
is hand-made art at the exact sizes and names the game loads.

## What to do

1. Read [game/brand/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md)
   first; it lists every file name and size. The two highest-value
   pieces:
   - `Textures/Menu/logo.png` (1024×1024, transparent): the big logo in
     the main menu (the beating disc);
   - `Textures/Icons/Logo.png` (90×90): the small logo icon.
2. Design within the established identity (white ring fading to gold at
   the centre, "kan!" in white with silver borders) or propose a
   refined variant of it; final acceptance is the maintainer's, and
   evolutions of the design are welcome if they keep the family feel.
   The name story for reference: 奏でる *kanaderu*, "to play an
   instrument"; read in English: "grab your instrument, you kan!"
   ([README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md)).
3. Deliverables: the PNGs at the exact names/sizes above (plus the intro
   and app-icon files if you go further; see the brand README table),
   made **from scratch**. Never copy, trace or closely imitate osu!'s
   art: osu!'s resources are CC BY-NC 4.0 and its logo is a trademark.
4. Record author and licence (CC0 or CC BY preferred) in the credits
   file the brand folder uses, and note the source file (e.g. layered
   SVG/PSD) so future edits are possible.

## Acceptance criteria

- [ ] `Textures/Menu/logo.png` (1024×1024, transparent) delivered and
      visible as the main-menu logo in a running build
- [ ] `Textures/Icons/Logo.png` (90×90) delivered and legible at small
      sizes
- [ ] 100% original work; no osu! asset copied, traced or closely
      imitated
- [ ] Author + licence recorded; layered source included if possible
- [ ] The provisional generator remains untouched as the fallback (other
      files keep working)

## Pointers

- [game/brand/README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/game/brand/README.md):
  the file table, sizes, and fallback rule ("a missing file falls back
  to osu!'s")
- `game/tools/gen-provisional-brand.py`: current provisional geometry
  (reference only)
- Fork F-21 `KanBrandResources.cs`: how the `brand/` folder overrides
  osu!'s resources at runtime
- [docs/adr/0011-rebrand-kan-identity.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/docs/adr/0011-rebrand-kan-identity.md):
  identity policy (trademark hygiene)

## Notes

- Trademark hygiene is ship-blocking: originality is not a style
  preference, it is a release requirement.
- Small files (90×90) need hand-tuned pixel craft, not downsampling;
  both pieces earn the same credit.
