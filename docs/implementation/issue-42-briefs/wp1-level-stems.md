# Issue 42, work package 1: a level keeps its stems (game side)

You are the builder. Work ONLY in the git worktree
`/mnt/dev_workspace/WDS/kan-rhythm-agents/level-stems` (branch
`agent/level-stems`). Never edit `/mnt/dev_workspace/WDS/kan-rhythm` (the
main folder); you may READ from it. Everything you write is English
(AGENTS.md). The architect (Claude) reviews and merges your branch.

## Goal

Owner decision, 2026-11-16: a level keeps the separated tracks (stems) of
its song, as **Ogg Vorbis** (owner, same day: FLAC was weighed and
dropped; Ogg needs no new library, keeps levels small for the server and
the downloads, and is transparent at the quality used), so others can later make the bass level, the guitar
level, and so on, without a new demux (each demux loses quality). Play
never opens them.

This package does the game side: stems written into the level, existing levels upgraded, and uploads that keep working.
The server side (serving a light play package without stems) is work
package 2, later.

## Read first

- `docs/issues/42-level-stems-and-compressed-audio.md` (the issue, with
  step 1's progress: the level audio and voice are already Ogg).
- Commit `3c4e6d3` (`git show 3c4e6d3`): step 1. **Reuse its pattern**:
  - `game/forge/native/cmake/kan_vorbis.cmake`: Xiph sources fetched and
    built as one static library, without their own CMake;
  - `game/forge/native/kan_forge_ogg.cpp/.hpp`: an encoder in its own
    translation unit, exported through `kan_forge_api.cpp` with `guarded`;
  - selftest section 8 in `kan_forge_selftest.cpp`;
  - `BeatmapAudio.ToPlayFileAsync` and `game/forge/tests/LevelPlayFileTest.cs`;
  - licence texts in `game/forge/native/licenses/`, listed in
    `game/packaging/licenses.manifest`, credited in
    `libkan/THIRD_PARTY_NOTICES.md`, `docs/release-credits.md` and
    `game/tools/gen-third-party-notices.py`.
- `game/forge/Editor/BeatmapAudio.cs` (how a file goes into a level:
  `beatmaps.AddFile`, `DeleteFile`, `SaveEditor`; the signature).
- `game/core/Tracks/ForgeSetFile.cs` and `ForgeTrack.cs`: the set
  (`set.json` in the level's work folder `~/.local/share/kan/kan-forge/<id>/`),
  `IsSeparated` marks a demuxed stem.
- `game/forge/Editor/KanTracksScreen.Actions.cs`: where the demux output
  (`roformer/*.wav`, 32 bit float, full song length) becomes tracks.
- `game/client/osu.Game/Screens/Edit/Submission/SubmissionBeatmapExporter.cs`
  and `BeatmapSubmissionScreen.cs` (the fork): what is uploaded.

## Background you must respect

- The server sits behind a Cloudflare tunnel: **a request body over
  100 MB is refused**, and the server's own limit is 100 MiB
  (`server/app/src/config.ts`).
- Measured by the architect on "No hay lugar más alto" (6:11, six stems,
  `kan_forge_encode_ogg`): WAV float 750 MB; Ogg 25.2 MB at quality 0.6,
  27.9 MB at 0.7, 31.4 MB at 0.8, about 16 s for the six. With the Ogg
  level audio and voice (about 9 MB) a level with stems is about 40 MB,
  so it fits one upload for songs up to about 14 minutes.

## Deliverables

### 1. Stems encoder: reuse the Ogg encoder

- **No new library.** Stems are encoded with `kan_forge_encode_ogg` (step
  1) at a higher quality than the played files, because they are source
  material: `LevelStems.STEM_QUALITY = 0.8f`.
- Selftest (extend section 8 of `kan_forge_selftest.cpp`): a stereo
  signal with different content per channel, encoded at 0.8, decodes with
  the exact frame count, both channels kept apart (each channel's SNR
  against its own source above 30 dB, and clearly worse against the other
  channel), and an SNR at 0.8 not below the one at 0.6.

### 2. Stems in the level

- A level's stems live in the level's files under `stems/`:
  `stems/<kind>.ogg` (kind = the demux stem name: vocals, bass, drums,
  guitar, piano, other; keep the names the demux gives) and
  `stems/stems.json`:
  ```json
  { "version": 1, "source": "demux", "model": "<model id and grade>",
    "sample_rate": 44100, "channels": 2, "frames": 16384000,
    "stems": [ { "file": "bass.ogg", "kind": "bass", "sha256": "..." } ] }
  ```
  Stems are the raw separation: full song length, no gain, edits, trims
  or window. All of a level's stems come from ONE separation and have the
  same frame count (check it; refuse a mismatched set with a clear
  error).
- New code in `game/forge/Editor/LevelStems.cs` (static, like
  `BeatmapAudio`):
  - `Task<bool> AttachAsync(...)`: when the set has separated tracks whose
    files exist, encode each to Ogg (`STEM_QUALITY`) in the work folder (off the update
    thread), and add them plus `stems.json` to the level, replacing
    earlier stems. Skip the work when the level already holds the same
    stems: compare a signature of the source files (path, length, mtime)
    stored in `stems.json` as `source_signature`.
  - `IsStemFile(string fileName)`: true for anything under `stems/`.
- Call `AttachAsync` where the level audio is rendered
  (`BeatmapAudio.RenderAsync`, after the level audio and voice are set),
  so a demuxed level carries its stems from its next save. A failure to
  attach stems must not fail the render: report it the way the editor
  reports other non-fatal problems (find how; `ForgeNotify`).

### 3. Play never opens stems

- Find every place the game reads a level's files by enumeration (the
  voice lookup `LevelPart.FindVoice`, `ComposeMonitor`, the fork's import
  and export paths, anything that lists `BeatmapSetInfo.Files`), and make
  sure none opens or decodes `stems/`. Add a test.

### 4. Uploads carry the stems, within the limit

- Stems travel with the level: the submission package and the plain
  export (`.osz`) both include `stems/`. The server keeps this full
  package; work package 2 makes it serve a light play package (without
  `stems/`) to players.
- Guard the limit: before uploading, if the package would exceed 95 MB
  (under both Cloudflare's 100 MB and the server's 100 MiB), the
  submission leaves out `stems/` and tells the creator, in plain words,
  that the stems stayed on their computer because the level is too long.
  Never fail the upload because of stems. Find where the submission
  package is built in the fork
  (`SubmissionBeatmapExporter.cs`, `BeatmapSubmissionScreen.cs`); keep the
  fork change minimal, behind a clearly named hook the kan code supplies
  if it can (see how `LevelOsuMarks.EditorObject` is set from
  `KanLevelFiles.Init`). Log the change in `game/docs/fork-divergence.md`
  with the next free F number. The message text is UX: write it plainly;
  the architect may reword it.
- Tests: a submission package of a level with stems has them under
  `stems/`; one over the limit leaves them out and says so.

### 5. Upgrading existing levels

- `game/forge/Editor/LevelUpgrade.cs`: `Task<UpgradeReport>
  UpgradeAllAsync(BeatmapManager, RealmAccess, ...)` that, for every kan
  level of the local player:
  1. turns a WAV level audio and a WAV voice file (names in
     `BeatmapAudio`: `... - level audio.wav`, `... - level voice.wav`) into
     Ogg with `BeatmapAudio.ToPlayFileAsync`, swaps the files, points
     `Metadata.AudioFile` at the new name, and saves the beatmap through
     `BeatmapManager` (no editor open). Store the new level audio
     signature in the set (`ForgeTrackSession` or `set.json`, find where
     `LevelAudioSignature` lives) so the editor does not render it again;
  2. attaches stems from the level's work folder when it has them
     (`LevelStems.AttachAsync`).
  It never changes the sound: same samples in, Ogg out; a level whose
  audio is not a kan render (an imported mp3 or ogg) is left alone. It
  reports per level: converted, stems attached, skipped (why), failed
  (why). It is idempotent: a second run changes nothing.
- No UI. The architect adds the button. Expose only the method and its
  report.
- Test it on levels in a temporary realm (see how existing forge tests
  build beatmaps, or the fork's test helpers), never on the owner's data.

### 6. Docs

- Issue 42: a "Step 2 progress" part with what you built and measured.
- `game/docs/level-creation-guide.md`: one short paragraph on stems kept
  in the level.

## Measure and report

- On the owner's data, READ ONLY: the work folder of level
  `efaf06a20e3445c4af047bbb905159dc` has six demuxed stems in
  `stems-44489aa9/roformer/`. Copy them to your scratch folder and measure
  at 0.8: Ogg size of the six, encode time, and each stem's round-trip
  SNR. Do not touch the
  owner's realm (`~/.local/share/kan/client.realm`) or files store.

## Rules

- **Never launch the game on the owner's screen.** You do not need to run
  the game at all for this package; tests are enough.
- **Bounded builds:** `nice cmake --build <dir> -j 6`, dotnet with
  `-maxcpucount:4`, `JOBS=6` for make. Never a bare `-j`, never a full
  clean rebuild. The machine also runs other heavy work.
- Build and test: `cd game && JOBS=6 nice make check` must pass (strict
  builds of core, forge and ruleset, native ctest, C# suites). If `make
  check` uses a dotnet path that is missing, pass
  `DOTNET=$HOME/.dotnet/dotnet`.
- Native: C++20, `-Wall -Wextra`, no new warnings in first-party code
  (configure once with `-DKAN_WERROR=ON` to check).
- Edit source files with your edit tool. **No `sed`, no Python slice
  edits on source files.**
- **Do not touch:** `libkan/` (the main folder owns it), `server/`,
  `tools/level_pack`, `charts/`, the songs, the difficulty mods, health,
  fail and streaks.
- Do not remove any capability. MP3, WAV and Ogg level audio keep playing.
- **Privacy:** commit only with
  `git -c user.name=KairoPhos -c user.email=kairophos@proton.me commit ...`,
  in the worktree and in its `game/client` submodule. Never write any
  other name or email anywhere. End every commit message with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Style: short, plain English; no dashes used to join clauses in prose.
- **Do not push, tag or deploy.**
- Small commits, one per deliverable, on `agent/level-stems` (and the
  fork's `main` inside the worktree's submodule for the fork changes).
- **Do not end while any command you started still runs** (builds, tests
  in the background). Wait for them and read their results.

## Final summary (short but deep)

- What you built, file by file, and the commits.
- The measurements: Ogg size of the six stems, encode time, round-trip
  SNR, the package size of that level with its stems, `make check` result
  with test counts.
- Anything you found that is wrong or risky, and anything left undone.
