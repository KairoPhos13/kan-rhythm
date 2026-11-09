# game/brand — kan!'s own art and sounds

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
| `Tracks/circles.osz`, `Tracks/triangles.osz`, `Tracks/welcome.osz` | — | Intro music (a beatmap set holding the track) |
| `Samples/Intro/welcome.mp3`, `Samples/Intro/seeya.mp3` (and `Samples/Intro/Welcome/…`) | — | Voices at start and exit |

The app icons are not resources: replace `client/osu.Desktop/lazer.ico`,
`client/osu.Desktop/beatmap.ico` and `client/assets/lazer.png` (1024×1024),
`client/assets/lazer-nuget.png` (128×128) directly.

Intro music already imported into a player's game stays until that copy of
the intro is deleted from their data folder.
