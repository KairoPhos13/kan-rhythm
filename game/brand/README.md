# game/brand: kan!'s own art and sounds

Files here replace osu!'s resources in the game, one file at a time (fork
F-21, `docs/fork-divergence.md`). The build copies this folder next to the
executable as `brand/`. A file that is not here falls back to osu!'s.

osu!'s resources are licensed CC BY-NC 4.0 (`ppy/osu-resources`), and the
osu! name and logo are trademarks: everything in this folder must be made
from scratch (or be freely licensed). Never copy or trace osu!'s files.

Name each file exactly as osu!'s resource it replaces:

| File | Size | What it is |
|---|---|---|
| `Textures/Menu/logo.png` | 1024×1024, transparent | The big logo in the main menu (the beating circle) |
| `Textures/Icons/Logo.png` | 90×90 | The small logo icon |
| `Textures/Intro/Triangles/logo-background.png` | 1295×1295 | The "triangles" intro: the logo's fill |
| `Textures/Intro/Triangles/logo-highlight.png` | 1295×1295 | The "triangles" intro: the logo's light |
| `Textures/Intro/Welcome/welcome_text.png` | 750×156 | The "welcome" intro's text |
| `Textures/Menu/menu-background-1.jpg` … `-8.jpg` | 2732×1537 | Main menu backgrounds |
| `Tracks/circles.osz`, `Tracks/triangles.osz`, `Tracks/welcome.osz` | - | Intro music (a beatmap set holding the track) |
| `Samples/Intro/welcome.mp3`, `Samples/Intro/seeya.mp3` (and `Samples/Intro/Welcome/…`) | - | Voices at start and exit |

The app icons are not resources: replace `client/osu.Desktop/lazer.ico`,
`client/osu.Desktop/beatmap.ico` and `client/assets/lazer.png` (1024×1024),
`client/assets/lazer-nuget.png` (128×128) directly.

Intro music: not played for now (fork F-22 turns osu!'s theme off; the intro
plays one of the player's own levels or starts silent). When kan! has its own
track, put it in `Tracks/` and turn `use_themed_intro` back on in
`IntroScreen.cs`.

## Provisional art (2026-11-09)

The logo, the app icons, the triangles intro and the eight menu backgrounds
here are generated from scratch by `../tools/gen-provisional-brand.py`
(geometry, no font, no osu! art; owner's design: a white ring fading to gold
at the centre, "kan!" in white with silver borders). Run it again after
changing its constants; replace any file with hand-made art whenever it
exists.

Provenance and licence (recorded 2026-11-11, issue 26): every file
currently in this folder is that provisional generator output. Author:
KairoPhos (design and `../tools/gen-provisional-brand.py` as the source).
Licence: MIT, the same as the repository ([`LICENSE`](../../LICENSE)).
Any hand-made replacement art needs its own author and licence line here
when it lands.
