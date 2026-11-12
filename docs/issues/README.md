# docs/issues/: draft GitHub issues for kan!'s public release

Drafted issues for the first public release of kan!
(github.com/KairoPhos13/kan-rhythm). Each file is the body of one issue,
ready to publish with `gh issue create --body-file`. They implement the
"Join in" invitation in the root
[README.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/README.md):
pending work open for anyone: play-testers on other instruments and
microphones, artists, translators, level creators, web developers.

Ground rules baked into every issue: **calm by design** (forgiving
defaults, strictness only by choice, never pressure or compulsion),
**generalist** (fixes serve every instrument, never one instrument's
special case), kan!'s vocabulary (**companion**, never "piano", for the
accompaniment), and English only
([CONTRIBUTING.md](https://github.com/KairoPhos13/kan-rhythm/blob/main/CONTRIBUTING.md)).

## Now: the ten that matter most (2026-11-10, states refreshed 2026-11-12)

Where kan! stands: the server is live (`kan-api.kairophos.link`), the website
is ready (`kan.kairophos.link`), accounts work from the website and the game.
Releases v0.1.0 to v0.1.2 are published on GitHub (`docs/releases/` has the
notes), the game ships with five starter levels (v0.1.2), and creators can
submit levels from kan!forge (v0.1.1).

| Order | Issue | Who | State |
|---|---|---|---|
| 1 | [31 Stay quiet online without realtime services](31-quiet-online-without-realtime.md) | agent | done 2026-11-10 |
| 2 | [32 Server tests in CI, shaped like the game](32-server-ci-with-postgres.md) | agent | done 2026-11-10 |
| 3 | [33 Admin tools: password reset, accounts](33-admin-tools.md) | agent | done 2026-11-10 |
| 4 | [35 Nightly database backups to R2](35-nightly-backups.md) | Claude | done 2026-11-10 |
| 5 | [15 First free levels](15-first-free-bundled-level.md) with [14 companions](14-companions-for-free-songs.md) | owner, then import | levels done 2026-11-11 (agent), shipped in v0.1.2; play-tests 01-06 left; 14 open |
| 6 | [09 Windows](09-windows-build-and-playtest.md) and [10 macOS](10-macos-build-and-playtest.md) play-tests, with the first release `v0.1.0` | owner, testers | releases v0.1.0 to v0.1.2 published (2026-11-10 to 2026-11-11); play-tests 09, 10 open |
| 7 | [22 Leftover osu! text](22-osu-string-sweep-remaining.md) (mod descriptions such as "SS or quit") | agent | done 2026-11-10 |
| 8 | [24 Hide osu!-only features](24-hide-osu-only-features.md) | agent | done 2026-11-10 |
| 9 | [34 Level uploads for creators](34-level-uploads.md) | owner decides the design | done 2026-11-11 (agent), shipped in v0.1.1: [lazer-protocol.md](../../server/docs/lazer-protocol.md) §4.8, fork F-30 in [fork-divergence.md](../../game/docs/fork-divergence.md) |
| 10 | [27 Level creator handbook](27-level-creator-handbook.md) | agent, owner reviews | draft done 2026-11-11, shipped in v0.1.1 as a first draft; owner reviews |

## Issue 15 progress (2026-11-11)

The starter pack is built and shipped: five levels in `charts/official/`
(2 Japanese, 1 Chinese, 1 Latin American, 1 hymn; easy to moderate on the
12-hole alto C ocarina), made from public-domain scores and MIDI files by
`tools/level_pack` (route (b): kan! renders its own companion). The release
zip carries them in `levels/`, and the ruleset imports them the first time
the game runs, once per pack version, with no account. Each level is loaded
and checked by `game/core/tests/StarterPackTest.cs` (notes, range, timing,
audio) and every source and licence is recorded in the level's `CREDITS.md`.
What is left of the acceptance list: playing the levels end to end on two
instrument families (the play-test issues 01-06), and the artist-made
companions of issue 14.

## Issue 34 progress (2026-11-11)

Levels now reach kan!'s server from their creators, through osu!'s own
in-editor submission pointed at kan!'s server (fork F-30 in
[game/docs/fork-divergence.md](../../game/docs/fork-divergence.md)): a
signed-in creator opens the level in kan!forge, chooses *File > Submit
level...*, and the level is published under their name as *Work in
progress* or *Pending*. Following osu!'s status semantics, ranked levels
appear in the main listing and get a leaderboard, and the owner moves a
level between statuses from the admin CLI
(`server/app/src/admin/level-admin.ts`). The routes and their rules are
recorded in [server/docs/lazer-protocol.md](../../server/docs/lazer-protocol.md)
§4.8; the submission shares the importer's checks (kan! difficulties
only, size limits, `server/app/src/beatmaps/submission-rules.ts`) and
limits uploads per creator and hour. Shipped in v0.1.1.

## Index

| # | Title | Labels | Size |
|---|---|---|---|
| 01 | [Play-test kan! on recorder and flute (wind family)](01-test-recorder-and-flute.md) | `instrument-testing` `help wanted` | M |
| 02 | [Play-test kan! with the singing voice (voice family)](02-test-voice.md) | `instrument-testing` `help wanted` | M |
| 03 | [Play-test kan! on violin (bowed-string family)](03-test-violin.md) | `instrument-testing` `help wanted` | M |
| 04 | [Play-test kan! on guitar and other plucked strings (plucked family)](04-test-guitar-and-plucked.md) | `instrument-testing` `help wanted` | M |
| 05 | [Play-test kan! on harmonica (free-reed family)](05-test-harmonica.md) | `instrument-testing` `help wanted` | M |
| 06 | [Play-test kan! on laptop and headset microphones](06-test-laptop-and-headset-microphones.md) | `instrument-testing` `audio` `help wanted` | M |
| 07 | [Windows noise suppression and audio enhancements vs kan!'s input](07-windows-noise-suppression.md) | `audio` `platform:windows` `instrument-testing` | S |
| 08 | [Ambient-noise false-trigger gate: measure and close the < 1% gate](08-ambient-noise-false-trigger-gate.md) | `libkan` `audio` `instrument-testing` | M |
| 09 | [Verify the Windows build and play-test kan! on Windows](09-windows-build-and-playtest.md) | `platform:windows` `game` `help wanted` | M |
| 10 | [Verify the macOS build and play-test kan! on macOS](10-macos-build-and-playtest.md) | `platform:macos` `game` `help wanted` | M |
| 11 | [kan!'s own intro music](11-kan-intro-music.md) | `art` `audio` `help wanted` | M |
| 12 | [Hand-made kan! logo and wordmark (replace the provisional art)](12-logo-and-wordmark-art.md) | `art` `help wanted` | M |
| 13 | [Hand-made menu backgrounds and intro art (brand folder)](13-menu-backgrounds-and-intro-art.md) | `art` `help wanted` | M |
| 14 | [Companions for the free songs (artists: melodies + accompaniments)](14-companions-for-free-songs.md) | `art` `help wanted` | L |
| 15 | [Ship a first free bundled level (charts/official/)](15-first-free-bundled-level.md) | `editor` `art` `help wanted` | L |
| 16 | [First run: choose the instrument, test the microphone, play an easy level](16-first-run-flow.md) | `game` `help wanted` | L |
| 17 | [3-step calibration screen (latency, noise floor, instrument detune)](17-calibration-screen.md) | `game` `audio` | M |
| 18 | [Colour-blind-safe palettes for notes and judgment feedback](18-colour-blind-safe-palettes.md) | `game` `help wanted` | M |
| 19 | [Font size and UI scaling that stay readable on any screen](19-font-size-and-ui-scaling.md) | `game` `good first issue` | S |
| 20 | [A short demo video for the release](20-demo-video.md) | `docs` `help wanted` | M |
| 21 | [Player-facing quickstart in the root README](21-player-quickstart-readme.md) | `docs` `good first issue` | S |
| 22 | [String sweep, remaining scope: leftover "osu!" text in English strings](22-osu-string-sweep-remaining.md) | `game` `good first issue` | S |
| 23 | [Localise the rebranded strings: other languages still say "osu!"](23-localise-rebranded-strings.md) | `localisation` `help wanted` `good first issue` | M |
| 24 | [Hide osu!-only features still reachable in the client (stable import, tutorial)](24-hide-osu-only-features.md) | `game` | S |
| 25 | [BASS replacement study: scope the clean-room "kanplay" option (D)](25-bass-replacement-study.md) | `licensing` `docs` | M |
| 26 | [Third-party credits and licence audit before the public release](26-third-party-credits-audit.md): audited 2026-11-11, result and GAP list in [release-credits.md](../release-credits.md) | `licensing` `docs` | S |
| 27 | [Level creator handbook: how to make a kan! level](27-level-creator-handbook.md) | `docs` `editor` `help wanted` | L |
| 28 | [Publish the `.kan` / `.kchart` format specification](28-chart-format-specification.md) | `docs` | M |
| 29 | [Server MVP follow-up: public website pages (landing + level catalogue)](29-website-pages.md) | `server` `help wanted` | L |
| 30 | [Server MVP follow-up: public profile pages (kp stats, per-instrument ranks)](30-profile-pages.md) | `server` | M |
| 31 | [Stay quiet online when kan!'s server has no realtime services](31-quiet-online-without-realtime.md) | `game` `server` | S |
| 32 | [Run the server's full test suite in CI, with requests shaped like the game's](32-server-ci-with-postgres.md) | `server` | S |
| 33 | [Admin tools: reset a player's password, list and remove accounts](33-admin-tools.md) | `server` | S |
| 34 | [Let level creators upload their levels to kan!'s catalogue](34-level-uploads.md) | `server` `editor` `help wanted` | L |
| 35 | [Nightly database backups to R2, with a restore drill](35-nightly-backups.md) | `server` | S |

Size key: **S** (hours to a day) · **M** (a few days) · **L** (a week or
more of part-time work).

## How to publish

Run from the repository root, with `gh` authenticated against
github.com/KairoPhos13/kan-rhythm. Read a body once before publishing; the
files are plain GitHub-flavored Markdown and are used as-is.

### 1. Create the labels first (once)

```bash
gh label create "good first issue"    --color 7057ff --description "Small, well-scoped, welcoming to newcomers"
gh label create "help wanted"         --color 008672 --description "Extra attention or hands wanted"
gh label create "game"                --color 1d76db --description "kan! game client (game/)"
gh label create "editor"              --color 5319e7 --description "kan!forge level editor (game/forge)"
gh label create "libkan"              --color 0e8a16 --description "libkan DSP engine"
gh label create "server"              --color fbca04 --description "Backend and web (server/)"
gh label create "art"                 --color d4c5f9 --description "Art, music and brand assets"
gh label create "audio"               --color c2e0c6 --description "Audio behaviour, capture, playback"
gh label create "docs"                --color 0075ca --description "Documentation"
gh label create "localisation"        --color f9d0c4 --description "Translations and locale strings"
gh label create "instrument-testing"  --color bfd4f2 --description "Play-testing on real instruments/microphones"
gh label create "platform:windows"    --color e4e669 --description "Windows-specific"
gh label create "platform:macos"      --color c5def5 --description "macOS-specific"
gh label create "licensing"           --color e99695 --description "Licence compliance and third-party notices"
```

### 2. Publish each issue

The title is the file's first line (`# …`); the labels are on the
`Labels:` line. This loop publishes them in order:

```bash
for f in docs/issues/[0-9][0-9]-*.md; do
  title=$(sed -n '1s/^# //p' "$f")
  labels=$(sed -n 's/^Labels: //p' "$f" | tr -d '`' | tr ',' '\n' \
           | sed 's/^ *//;s/ *$//' | paste -sd, -)
  gh issue create --title "$title" --body-file "$f" --label "$labels"
done
```

Notes:

- `--label` accepts a comma-separated list; label names with spaces are
  fine when quoted as above.
- Publish the whole set at once, or in waves (play-testing 01-10 first,
  content 11-15 alongside the release announcement), maintainer's
  choice. Issue numbers are assigned by GitHub and will not match the
  file numbers; that is expected.
- Owner-only or private work (repository history, project accounts,
  hosting credentials, funding decisions) is deliberately **not** in
  this set. Where such a decision is referenced, the body says "pending
  maintainer decision" and stops there.
