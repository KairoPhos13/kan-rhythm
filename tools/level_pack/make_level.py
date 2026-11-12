#!/usr/bin/env python3
# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""Build kan! starter-pack levels from a MIDI and a per-song song.json.

Two commands, one for each half of the work:

    # render the companion and reference audio into <song>/level/ (needs numpy)
    python3 tools/level_pack/make_level.py render charts/official/sakura-sakura

    # pack <song>/level/ into charts/official/dist/<slug>.osz (stdlib only)
    python3 tools/level_pack/make_level.py pack charts/official/sakura-sakura

    # every song under charts/official/, render then pack
    python3 tools/level_pack/make_level.py build --all

`render` is the slow half: it reads the score source (a MIDI with a melody
track and companion tracks), writes the kan! files (score, design, credits),
renders the companion and the reference with the pack's synth and levels both
to the game's loudness reference. Its output (level/) is committed, so `pack`
- and the CI build - needs nothing but Python's standard library and rebuilds
bit-identical .osz files from what is in the repository.

Usage and design notes: tools/level_pack/README.md.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import shutil
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from kanpack import level as level_mod  # noqa: E402
from kanpack import loudness, smf  # noqa: E402

# kanpack.synth needs numpy and is imported where it renders: `pack` (what CI
# runs) must work with nothing but the standard library.
SAMPLE_RATE = 44100

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CHARTS_OFFICIAL = os.path.join(REPO_ROOT, "charts", "official")
PACK_JSON = os.path.join(CHARTS_OFFICIAL, "pack.json")

AUDIO_NAME = "audio.ogg"
REFERENCE_NAME = "reference.ogg"
BACKGROUND_NAME = "background.png"


def song_dirs(target: str | None, all_songs: bool) -> list[str]:
    if all_songs:
        dirs = [os.path.join(CHARTS_OFFICIAL, name) for name in sorted(os.listdir(CHARTS_OFFICIAL))
                if os.path.isfile(os.path.join(CHARTS_OFFICIAL, name, "song.json"))]
        if not dirs:
            raise SystemExit(f"no song.json found under {CHARTS_OFFICIAL}")
        return dirs
    if not target:
        raise SystemExit("give a song folder or --all")
    target = os.path.abspath(target)
    if not os.path.isfile(os.path.join(target, "song.json")):
        raise SystemExit(f"{target}: no song.json")
    return [target]


# ----------------------------------------------------------------------
# render
# ----------------------------------------------------------------------


def write_audio(path: str, samples, sample_rate: int = SAMPLE_RATE) -> None:
    """Write mono samples as Ogg Vorbis: ffmpeg when it is there, else soundfile.

    ffmpeg is preferred because libsndfile's Vorbis encoder aborts on longer
    files in some builds (seen on a 60 s render); either way the file is the
    same gentle Vorbis encode of the levelled samples.
    """
    import numpy as np
    import subprocess
    import tempfile
    import wave

    pcm = np.clip(samples, -1.0, 1.0)
    frames = (pcm * 32767.0).astype("<i2").tobytes()

    with tempfile.TemporaryDirectory() as tmp:
        wav_path = os.path.join(tmp, "render.wav")
        with wave.open(wav_path, "wb") as handle:
            handle.setnchannels(1)
            handle.setsampwidth(2)
            handle.setframerate(sample_rate)
            handle.writeframes(frames)

        try:
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", wav_path,
                            "-c:a", "libvorbis", "-q:a", "5", path], check=True)
            return
        except (OSError, subprocess.CalledProcessError):
            pass

        import soundfile as sf
        sf.write(path, samples, sample_rate, format="OGG", subtype="VORBIS")


def render_song(song_dir: str) -> str:
    try:
        import numpy as np  # noqa: F401
    except ImportError as e:
        raise SystemExit(f"rendering needs numpy: {e}")

    import numpy as np

    from kanpack import synth

    song = level_mod.Song.load(os.path.join(song_dir, "song.json"))
    midi_path = os.path.join(song_dir, song.midi)
    midi = smf.read(midi_path)
    if song.melody_track >= len(midi.tracks):
        raise SystemExit(f"{midi_path}: no track {song.melody_track}")
    midi, count_in_track = level_mod.arrange(midi, song)

    notes = level_mod.score_notes(midi, song.melody_track, song.transpose_semitones, song.tempo_bpm)
    if not notes:
        raise SystemExit(f"{midi_path}: the melody track has no notes")

    pitches = [n["Midi"] for n in notes]
    if min(pitches) < level_mod.OCARINA_LOW or max(pitches) > level_mod.OCARINA_HIGH:
        raise SystemExit(
            f"{song.slug}: melody range {min(pitches)}..{max(pitches)} is outside the"
            f" 12-hole alto C ocarina (A4..F6): adjust transpose_semitones")

    companion_tracks = song.companion_tracks or [i for i in range(len(midi.tracks)) if i != song.melody_track]
    companion_tracks = [i for i in companion_tracks if i != song.melody_track and i != count_in_track]
    if count_in_track >= 0:
        companion_tracks.append(count_in_track)
    melody_timbre = {song.melody_track: "melody"}
    companion = synth.render(midi, companion_tracks, song.tempo_bpm,
                             timbres={i: "companion" for i in companion_tracks})
    reference = synth.render(midi, companion_tracks + [song.melody_track], song.tempo_bpm,
                             timbres=melody_timbre | {i: "companion" for i in companion_tracks},
                             gains={song.melody_track: 1.15})

    level_dir = os.path.join(song_dir, "level")
    os.makedirs(level_dir, exist_ok=True)

    for name, samples in ((AUDIO_NAME, companion), (REFERENCE_NAME, reference)):
        write_audio(os.path.join(level_dir, name), loudness.level(samples, synth.SAMPLE_RATE))

    level_ms = len(companion) / synth.SAMPLE_RATE * 1000
    saved_at = dt.datetime.now(dt.timezone.utc).replace(tzinfo=None)

    with open(os.path.join(level_dir, "kan.score.json"), "w", encoding="utf-8") as handle:
        handle.write(level_mod.kan_score_json(song, notes, saved_at))
    with open(os.path.join(level_dir, "kan.design.json"), "w", encoding="utf-8") as handle:
        handle.write(level_mod.kan_design_json(song, notes, level_ms, saved_at))
    with open(os.path.join(level_dir, "kan.credits.json"), "w", encoding="utf-8") as handle:
        handle.write(level_mod.kan_credits_json(song))

    with open(os.path.join(level_dir, BACKGROUND_NAME), "wb") as handle:
        handle.write(level_mod.background_png(song.palette))

    preview_ms = int(min(notes[0]["StartMs"], level_ms))
    osu_name = f"{song.artist} - {song.title} ({song.creator}) [{song.version}].osu"
    with open(os.path.join(level_dir, osu_name), "w", encoding="utf-8", newline="\n") as handle:
        handle.write(level_mod.osu_text(song, AUDIO_NAME, BACKGROUND_NAME, preview_ms))

    filenames = {"audio": AUDIO_NAME, "reference": REFERENCE_NAME, "background": BACKGROUND_NAME}
    credits = level_mod.credits_md(song, filenames)
    for path in (os.path.join(level_dir, "CREDITS.md"), os.path.join(song_dir, "CREDITS.md")):
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(credits)

    peak = loudness.sample_peak(loudness.level(companion, synth.SAMPLE_RATE))
    print(f"{song.slug}: {len(notes)} notes, {level_ms / 1000:.1f} s,"
          f" range {min(pitches)}..{max(pitches)} (A4..F6 is 69..89), peak {peak:.2f}")
    return song.slug


# ----------------------------------------------------------------------
# pack
# ----------------------------------------------------------------------


def pack_song(song_dir: str, out_dir: str) -> str:
    song = level_mod.Song.load(os.path.join(song_dir, "song.json"))
    level_dir = os.path.join(song_dir, "level")
    if not os.path.isdir(level_dir):
        raise SystemExit(f"{song.slug}: no level/ folder: run 'render' first")

    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, f"{song.slug}.osz")
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(os.listdir(level_dir)):
            archive.write(os.path.join(level_dir, name), name)
    print(f"{song.slug}: {out_path} ({os.path.getsize(out_path)} bytes)")
    return song.slug


def pack_manifest(slugs: list[str], out_dir: str) -> None:
    manifest = {"name": "kan! starter pack", "version": pack_version(), "levels": [f"{s}.osz" for s in slugs]}
    with open(os.path.join(out_dir, "pack.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    print(f"pack.json: version {manifest['version']}, {len(slugs)} levels")


def pack_version() -> int:
    if os.path.isfile(PACK_JSON):
        with open(PACK_JSON, encoding="utf-8") as handle:
            return int(json.load(handle).get("version", 1))
    return 1


# ----------------------------------------------------------------------
# CLI
# ----------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["render", "pack", "build"], help="render audio, pack .osz, or both")
    parser.add_argument("song", nargs="?", help="a charts/official/<song> folder")
    parser.add_argument("--all", action="store_true", help="every song under charts/official/")
    parser.add_argument("--out", default=os.path.join(CHARTS_OFFICIAL, "dist"), help="where .osz files go")
    args = parser.parse_args()

    dirs = song_dirs(args.song, args.all)
    slugs = []
    for song_dir in dirs:
        if args.command in ("render", "build"):
            render_song(song_dir)
        if args.command in ("pack", "build"):
            slugs.append(pack_song(song_dir, args.out))

    if slugs:
        pack_manifest(slugs, args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
