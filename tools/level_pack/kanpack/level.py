# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""Turn a MIDI and a song.json into the files an importable kan! level holds.

A kan! level is an osu! archive (`.osz`) whose extra files carry what the
osu! format cannot: the score (`kan.score.json`, pitch and duration per
note), the design (`kan.design.json`, how the level looks), the credits
(`kan.credits.json`) and the level's own CREDITS.md. This module writes all
of them from two inputs:

* the **score source**: a MIDI with a melody track and companion tracks
  (a public domain score or MIDI, or our own transcription of one);
* **song.json**: the level's metadata, licence and credits, which track is
  the melody, how far to transpose it, and the tempo for calm play.

The note data is written in the same shape `Kan.Core.Score.KanScore` reads
(game/core/Score/KanScore.cs), so the ruleset decodes what this writes.
"""

from __future__ import annotations

import datetime as dt
import json
import os
import struct
import zlib
from dataclasses import dataclass, field

from . import smf
from .loudness import CEILING_DBFS, REFERENCE_LUFS

# The 12-hole alto C ocarina's range (concert pitch), A4..F6.
OCARINA_LOW = 69
OCARINA_HIGH = 89


@dataclass
class Song:
    """song.json: the level's metadata and build parameters."""
    slug: str
    title: str
    title_unicode: str = ""
    artist: str = "Traditional"
    artist_unicode: str = ""
    origin: str = ""
    creator: str = "KairoPhos"
    version: str = "Calm"
    source: str = ""
    tags: str = ""
    midi: str = ""
    melody_track: int = 0
    companion_tracks: list = field(default_factory=list)
    transpose_semitones: int = 0
    tempo_bpm: float = 72.0
    meter: tuple[int, int] = (4, 4)
    instrument: str = "ocarina"
    palette: str = "nature"
    difficulty: int = 2
    preview_beat: float = 0.0
    # Beats of the source cut from its start (an instrumental prelude).
    skip_beats: float = 0.0
    # Beats of gentle count-in pulses before the music; None: one bar.
    count_in_beats: float | None = None
    cover_url: str = ""
    performers: list = field(default_factory=list)
    note: str = ""
    licence: str = ""
    sources: list = field(default_factory=list)

    @staticmethod
    def load(path: str) -> "Song":
        with open(path, encoding="utf-8") as handle:
            raw = json.load(handle)
        raw["meter"] = tuple(raw.get("meter", [4, 4]))
        known = {f for f in Song.__dataclass_fields__}
        return Song(**{k: v for k, v in raw.items() if k in known})


COUNT_IN_PITCH = 96  # C7: a soft, high tick, far above any melody


def arrange(midi: smf.MidiFile, song: "Song") -> tuple[smf.MidiFile, int]:
    """The source as the level plays it: the first `skip_beats` cut, then
    `count_in_beats` (one bar unless set) of soft pulses before the music,
    so a player hears the tempo before the first note. Returns the new file
    and the index of the count-in track (-1 if none). The grid stays whole:
    skip and count-in are whole beats, so bars stay on the level's grid.
    """
    count_in = song.meter[0] if song.count_in_beats is None else song.count_in_beats
    shift = count_in - song.skip_beats
    tracks = []
    for track in midi.tracks:
        notes = []
        for note in track.notes:
            if note.end <= song.skip_beats:
                continue
            start = max(note.start, song.skip_beats)
            notes.append(smf.Note(start + shift, note.end + shift, note.pitch, note.velocity))
        tracks.append(smf.Track(track.name, track.program, track.channel, notes))

    pulse = -1
    if count_in > 0:
        ticks = [smf.Note(b, b + 0.12, COUNT_IN_PITCH, 70 if b % song.meter[0] == 0 else 45)
                 for b in range(int(count_in))]
        tracks.append(smf.Track("count-in", 0, 9, ticks))
        pulse = len(tracks) - 1

    return smf.MidiFile(midi.ticks_per_beat, midi.tempo_bpm, midi.numerator, midi.denominator, tracks), pulse


def score_notes(midi: smf.MidiFile, melody_track: int, transpose: int,
                tempo_bpm: float) -> list[dict]:
    """The melody track's notes as kan! score notes (times in ms).

    The whole file is retimed at `tempo_bpm` (the tempo for calm play), so
    the score and the rendered audio agree however the source was written.
    """
    beat_ms = 60000.0 / tempo_bpm
    notes = []
    for note in midi.tracks[melody_track].notes:
        pitch = note.pitch + transpose
        if not 0 <= pitch <= 127:
            raise ValueError(f"note {note.pitch} + {transpose} is outside MIDI")
        start = note.start * beat_ms
        duration = max(10.0, note.duration * beat_ms)
        notes.append({
            "StartMs": round(start, 3),
            "DurationMs": round(duration, 3),
            "Midi": pitch,
            "Cents": 0.0,
            "Intensity": round(0.35 + 0.55 * (max(1, min(127, note.velocity)) / 127), 3),
            "Articulation": "Normal",
            "Vibrato": False,
            "PitchUnknown": False,
        })
    notes.sort(key=lambda n: (n["StartMs"], n["Midi"]))
    return notes


def kan_score_json(song: Song, notes: list[dict], saved_at: dt.datetime) -> str:
    beat_ms = 60000.0 / song.tempo_bpm
    return json.dumps({
        "Version": 1,
        "Instrument": song.instrument,
        "Notes": notes,
        "TranscriptionAtUtc": saved_at.isoformat(timespec="seconds") + "Z",
        "SavedAtUtc": saved_at.isoformat(timespec="seconds") + "Z",
        "Grid": [{"TimeMs": 0.0, "BeatMs": beat_ms}],
    }, indent=2)


# ----------------------------------------------------------------------
# the design: a calm one, shaped like AutoDesign but kept (not generated)
# ----------------------------------------------------------------------

BREAK_MS = 3500.0
PHRASE_GAP_MS = 1200.0
MIN_PHRASE_MS = 9000.0
RING_MS = 400.0


def design_sections(notes: list[dict], level_ms: float) -> list[dict]:
    """Intro, phrases and outro with rests between, like AutoDesign.Generate.

    The sections are written out (Generated false) so the calm look a level
    ships with is the look it keeps; a generated design is redrawn every time
    the level opens (editor-pipeline §6g).
    """
    if not notes:
        return [{"StartMs": 0.0, "EndMs": level_ms, "Kind": "Break",
                 "Climax": False, "BreakStyle": "Visualizer",
                 "Palette": None, "Label": None}]

    ordered = sorted(notes, key=lambda n: n["StartMs"])
    runs: list[list[dict]] = [[ordered[0]]]
    for note in ordered[1:]:
        if note["StartMs"] - runs[-1][-1]["StartMs"] - runs[-1][-1]["DurationMs"] >= BREAK_MS:
            runs.append([])
        runs[-1].append(note)

    sections: list[dict] = []
    first_start = ordered[0]["StartMs"]
    if first_start >= BREAK_MS:
        sections.append(_section(0, first_start, "Intro", "Countdown"))

    for index, run in enumerate(runs):
        from_ms = sections[-1]["EndMs"] if sections else 0.0
        run_end = max(n["StartMs"] + n["DurationMs"] for n in run)
        for phrase in _phrases(run, from_ms, run_end + RING_MS):
            sections.append(phrase)
        if index + 1 < len(runs):
            sections.append(_section(run_end + RING_MS, runs[index + 1][0]["StartMs"], "Break", "Visualizer"))

    tail_start = sections[-1]["EndMs"]
    if level_ms - tail_start >= BREAK_MS:
        sections.append(_section(tail_start, level_ms, "Outro", "Visualizer"))
    else:
        sections[-1]["EndMs"] = level_ms

    return sections


def _section(start: float, end: float, kind: str, break_style: str) -> dict:
    return {"StartMs": round(start, 3), "EndMs": round(end, 3), "Kind": kind,
            "Climax": False, "BreakStyle": break_style, "Palette": None, "Label": None}


def _phrases(notes: list[dict], start: float, end: float) -> list[dict]:
    """Cut a run of notes at its longer silences, every phrase at least 9 s."""
    cuts = [start]
    reach = notes[0]["StartMs"] + notes[0]["DurationMs"]
    for note in notes[1:]:
        if note["StartMs"] - reach >= PHRASE_GAP_MS and note["StartMs"] - cuts[-1] >= MIN_PHRASE_MS \
                and end - note["StartMs"] >= MIN_PHRASE_MS:
            cuts.append(note["StartMs"])
        reach = max(reach, note["StartMs"] + note["DurationMs"])
    cuts.append(end)
    return [_section(cuts[i], cuts[i + 1], "Phrase", "Visualizer") for i in range(len(cuts) - 1)
            if cuts[i + 1] > cuts[i]]


def kan_design_json(song: Song, notes: list[dict], level_ms: float, saved_at: dt.datetime) -> str:
    return json.dumps({
        "Version": 2,
        "Palette": song.palette,
        "Sparkle": 0.5,
        "Glow": 0.7,
        "Picture": 0.6,
        "ShowSectionNames": True,
        "Sections": design_sections(notes, level_ms),
        "Generated": False,
        "SavedAtUtc": saved_at.isoformat(timespec="seconds") + "Z",
    }, indent=2)


def kan_credits_json(song: Song) -> str:
    return json.dumps({
        "Version": 1,
        "CoverUrl": song.cover_url,
        "Performers": song.performers,
        "Note": song.note,
    }, indent=2)


# ----------------------------------------------------------------------
# the .osu side and the level's own credits
# ----------------------------------------------------------------------


def hit_objects_text(notes: list[dict]) -> str:
    """The score as hit-object marks in the osu! format.

    The notes cannot travel in the .osu (no pitch, no note duration there),
    but the game and the server read a level's length and object count from
    its hit objects: so the .osu carries a plain circle at every note's start
    and one at the level's end (the last note's end), which is exactly the
    span the level is played over. The ruleset replaces the marks with the
    real notes when the level is played. This mirrors `LevelMarks.TimesOf` in
    game/core/Score/LevelMarks.cs; the model is documented in
    docs/architecture/level-metadata.md.
    """
    if not notes:
        return ""
    marks = sorted(n["StartMs"] for n in notes)
    marks.append(max(n["StartMs"] + n["DurationMs"] for n in notes))
    return "".join(f"256,192,{ms:.3f},1,0,0:0:0:0:\n" for ms in marks)


def osu_text(song: Song, notes: list[dict], audio_name: str, background_name: str, preview_ms: int) -> str:
    beat_length = 60000.0 / song.tempo_bpm
    numerator, denominator = song.meter
    return (
        "osu file format v14\n"
        "\n"
        "[General]\n"
        f"AudioFilename: {audio_name}\n"
        "AudioLeadIn: 0\n"
        f"PreviewTime: {preview_ms}\n"
        "Countdown: 0\n"
        "SampleSet: Soft\n"
        "StackLeniency: 0.7\n"
        "Mode: 0\n"
        "LetterboxInBreaks: 0\n"
        "WidescreenStoryboard: 0\n"
        "\n"
        "[Editor]\n"
        "DistanceSpacing: 1.0\n"
        "BeatDivisor: 4\n"
        "GridSize: 32\n"
        "TimelineZoom: 1\n"
        "\n"
        "[Metadata]\n"
        f"Title:{song.title}\n"
        f"TitleUnicode:{song.title_unicode or song.title}\n"
        f"Artist:{song.artist}\n"
        f"ArtistUnicode:{song.artist_unicode or song.artist}\n"
        f"Creator:{song.creator}\n"
        f"Version:{song.version}\n"
        f"Source:{song.source}\n"
        f"Tags:{song.tags}\n"
        "\n"
        "[Difficulty]\n"
        "HPDrainRate: 5\n"
        "CircleSize: 4\n"
        "OverallDifficulty: 5\n"
        "ApproachRate: 5\n"
        "SliderMultiplier: 1.4\n"
        "SliderTickRate: 1\n"
        "\n"
        "[Events]\n"
        "//Background and Video events\n"
        f'0,0,"{background_name}",0,0\n'
        "\n"
        "[TimingPoints]\n"
        f"0,{beat_length:.3f},{numerator},2,0,80,1,0\n"
        "\n"
        "[HitObjects]\n"
        f"{hit_objects_text(notes)}"
    )


def credits_md(song: Song, filenames: dict[str, str]) -> str:
    lines = [
        f"# {song.title}",
        "",
        f"{song.origin}" if song.origin else "",
        "",
        "This level is part of the kan! starter pack (KairoPhos). Melody only; no lyrics.",
        "",
        "## Licence and sources",
        "",
    ]
    if song.licence:
        lines += [f"**Level licence:** {song.licence}", ""]
    for source in song.sources:
        url = source.get('url', '')
        where = f": {url}" if url else ""
        lines.append(f"- **{source.get('what', 'source')}**{where}"
                     f" ({source.get('licence', '')}){(' ' + source['note']) if source.get('note') else ''}")
    lines += ["", "## Credits", ""]
    if song.note:
        lines += [song.note, ""]
    for performer in song.performers:
        links = ", ".join(performer.get("Links", []))
        lines.append(f"- {performer.get('Name', '')} ({performer.get('Role', '')})"
                     f"{(': ' + links) if links else ''}")
    lines += [
        "",
        "## Files",
        "",
        f"- `{filenames['audio']}`: the level's companion (the audio played along),"
        f" rendered by kan! from the score, levelled to {REFERENCE_LUFS:.0f} LUFS"
        f" with peaks under {CEILING_DBFS:.0f} dBFS.",
        f"- `{filenames['reference']}`: the reference (melody and companion together),"
        f" rendered and levelled the same way.",
        f"- `kan.score.json`, `kan.design.json`, `kan.credits.json`: the level's score,"
        " design and credits (the kan! files inside a level).",
        f"- `{filenames['background']}`: the level's background (original, CC0).",
        "",
    ]
    return "\n".join(line for line in lines if line != "" or True)


# ----------------------------------------------------------------------
# a calm background: a vertical gradient, written as a PNG (stdlib only)
# ----------------------------------------------------------------------

PALETTES = {
    "sakura": ((34, 24, 38), (86, 48, 74)),
    "ocean": ((12, 26, 42), (28, 68, 92)),
    "nature": ((18, 30, 22), (46, 78, 56)),
    "holy": ((30, 28, 22), (92, 82, 58)),
    "aurora": ((16, 24, 38), (44, 66, 88)),
    "ember": ((34, 22, 20), (92, 58, 44)),
}


def background_png(palette: str, width: int = 640, height: int = 360) -> bytes:
    top, bottom = PALETTES.get(palette, PALETTES["nature"])
    rows = bytearray()
    for y in range(height):
        mix = y / max(1, height - 1)
        colour = tuple(int(round(top[i] + (bottom[i] - top[i]) * mix)) for i in range(3))
        rows.append(0)  # no filter
        rows += bytes(colour) * width
    raw = zlib.compress(bytes(rows), 9)

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + kind + payload
                + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", raw)
            + chunk(b"IEND", b""))
