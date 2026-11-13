# November 2026 audit: follow-up on the judge and editor bugs

This collects what the audit fix lane of 2026-11-12 could not change inside
this repository, and what it deliberately left alone. The findings in
question are from section 3 ("Bugs") of `/mnt/dev_workspace/WDS/kan-audit-2026-11.md`.
The only public identity of this project is KairoPhos (kairophos@proton.me).

## B6 (part 2): carry the level's local files across a beatmap update

Fixed here (this repository): the work folder and the autosaves now go by a
stable level id stored in the kan! files (`KanScore.LevelId`,
`KanDesign.LevelId`, resolved by `Kan.Core.LevelDocument.KeyOf` and stamped
on first open of the level), so a re-import with a new set id still finds
them. A level with no id yet keeps the old key (its set id), so existing
work folders keep working.

Not fixed (it needs the osu! fork, which this lane may not edit): locally
added kan files still die with the old set. The fork change that is needed,
exactly:

- File: `game/client/osu.Game/Beatmaps/BeatmapImporter.cs`, method
  `ImportAsUpdate` (the `first.PerformWrite(updated => ...)` block, after
  `updated.DateAdded = originalDateAdded;`).
- Change: before the original set is deleted, copy every file of
  `original.Files` that `updated` does not already carry (same rule the
  beatmap file store uses: compare `Filename` ignoring case) into the
  updated set, through the same file store the importer already writes to.
  The kan! files (`kan.score.json`, `kan.design.json`, `kan.credits.json`)
  are the ones that matter here; carrying every extra file is the simpler
  and more general rule (locally added audio belongs to the level too).
- Why: `ImportAsUpdate` imports the update as a new set and then deletes the
  original (its files with it). Any kan! file the player created locally on
  a level that arrived without one is lost, and with it the stable id that
  keys the work folder. Carrying the files across keeps the local work
  alive; the key change above then finds the work folder.

Tested here for the key half (`game/forge/tests/LevelKeyTest.cs`). The carry
half can only be tested in the fork.

## B12: file paths on Windows

Not fixed, deliberately. The bug is real (string parameters marshal as ANSI
on Windows, so a path outside the system code page cannot be passed), but
the fix is all or nothing: UTF-8 marshalling on the C# side
(`UnmanagedType.LPUTF8Str` in `game/core/Native/KanForgeNative.cs`) is only
correct if every consumer of those paths speaks UTF-8. miniaudio does (it
converts UTF-8 to wide chars, `miniaudio.h` `ma_fopen`), but the bridge's
own WAV writer (`std::fopen` in `game/forge/native/kan_forge_api.cpp`) and
its process launches (`kan_forge_fetch_audio` through `cmd.exe`,
`kan_forge_separate_stems` through libkan) do not, and libkan's file
opening was not examined. Half of the change would move the breakage from
"paths outside the code page do not work" to "paths inside the code page
stop working", which is a regression. The change cannot be verified on this
Linux host either (no Windows runtime here).

The shape of a complete fix, for whoever takes it to Windows: marshal all
strings and string arrays as UTF-8; open files in the bridge through
`std::filesystem::path` built from UTF-8 (or `_wfopen` on Windows); pass
wide command lines to `CreateProcessW` (or quote UTF-8 for the shell);
audit libkan's file opening the same way; then verify on a Windows machine
with a non-Latin user folder.

## B15: first-wins static storage in `KanLevelFiles`

Not fixed. Its fix is the architecture step A2 of the audit (one explicit
fork hook in place of the ruleset icon's dependency loader, and the three
static flags with it), which changes the fork. The behaviour itself
(first-wins `storage`/`game`, one-shot component starts) is deliberate for
the loader that exists today: it makes the side door idempotent. With two
game objects in one process (visual tests) the second one is indeed
starved; that is a test-infrastructure consequence of A2, not a bug with a
small fix.

## B11: score-wide shifts and the design sections

The grid half of B11 is fixed here: the repair step's tempo grid lives on
the editor's beatmap (`game/forge/Editor/Compose/EditorGrid.cs`), not in
the compose screen, so undoing a repair from the track tab restores it even
after compose closed (`game/forge/tests/EditorGridTest.cs`).

The second half ("score-wide shifts move notes and leave design sections
behind") is not changed, because it is not a bug on its own: the design's
sections mark stretches of the level's audio, and a score-wide shift (the
transcriber delay, a grid refit) moves notes without moving the music, so
hand-placed sections should stay where the music is. Generated sections
already re-derive from the notes at each open
(`AutoDesign.Refresh`). Moving sections with notes is a product decision
(owner call), and one rule cannot serve both kinds of section.

## C2 (part 2): separation speed on the portable baseline

The shipped native library now builds for x86-64-v2 (audit C2), which
keeps it from crashing on older CPUs but also turns off ggml's AVX2, FMA
and F16C kernels. Stem separation on the CPU is therefore slower than a
locally tuned build; the GPU (Vulkan) path and live recognition are not
affected. The fix that keeps both is ggml's runtime dispatch
(`GGML_BACKEND_DL` with `GGML_CPU_ALL_VARIANTS`): one CPU backend per
instruction set, the best one chosen when the game starts. It changes how
the bridge loads ggml (separate backend libraries next to the bridge), so
it needs its own lane and a test on an older CPU.

Decision taken (2026-11-12): keep the B11 rule as it is. Design sections
mark the music in the audio, and a score-wide shift of the notes does not
move that music.
