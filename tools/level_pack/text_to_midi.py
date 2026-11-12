#!/usr/bin/env python3
# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""Turn an auditable text transcription into the MIDI the level generator reads.

Every melody the pack typesets from a printed score lives in
`charts/official/<song>/sources/<slug>.txt`, one line per note, so the
transcription can be reviewed line by line against the score it came from and
rebuilt into a MIDI at any time:

    python3 tools/level_pack/text_to_midi.py charts/official/kojo-no-tsuki/sources/kojo-no-tsuki.txt

The text format is deliberately plain:

    tempo 72
    meter 4 4
    melody                 # the notes the player plays (track 0)
      0 A4 1               # <start in beats> <pitch> <duration in beats>
      1 A4 1
      2 D5 2
    companion              # what plays along (track 1): chords or single notes
      0 [D2,A2,D3] 4       # a chord at beat 0 for 4 beats
      4 [D2,A2,D3] 2

Blank lines and anything after `#` are ignored. The output is a format 1 SMF
with one track per part, which `make_level.py render` reads like any other
MIDI (it takes `melody_track` 0 and `companion_tracks` [1]).
"""

from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from kanpack import smf  # noqa: E402

PITCHES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def pitch_of(text: str) -> int:
    text = text.strip()
    letter = text[0].upper()
    if letter not in PITCHES:
        raise ValueError(f"not a note name: {text}")
    i, accidental = 1, 0
    while i < len(text) and text[i] in "#b♯♭":
        accidental += 1 if text[i] in "#♯" else -1
        i += 1
    octave = int(text[i:])
    midi = (octave + 1) * 12 + PITCHES[letter] + accidental
    if not 0 <= midi <= 127:
        raise ValueError(f"outside MIDI range: {text}")
    return midi


def parse(path: str) -> smf.MidiFile:
    midi = smf.MidiFile(ticks_per_beat=480)
    tracks: dict[str, smf.Track] = {}
    current: smf.Track | None = None

    with open(path, encoding="utf-8") as handle:
        for number, raw in enumerate(handle, start=1):
            line = raw.strip()
            if line.startswith("#"):
                continue
            # A comment is a '#' with space before it: '#' is part of note names.
            line = line.split(" #", 1)[0].strip()
            if not line:
                continue
            if line.startswith("tempo "):
                midi.tempo_bpm = float(line.split()[1])
                continue
            if line.startswith("meter "):
                midi.numerator, midi.denominator = int(line.split()[1]), int(line.split()[2])
                continue
            if "[" not in line and line.split()[0] in ("melody", "companion", "reference"):
                current = tracks.setdefault(line.split()[0], smf.Track(name=line.split()[0]))
                continue
            if current is None:
                raise ValueError(f"{path}:{number}: notes before a part name")

            fields = line.split()
            start, duration = float(fields[0]), float(fields[-1])
            pitches = fields[1]
            if pitches.startswith("["):
                chord = [pitch_of(p) for p in pitches.strip("[]").split(",")]
            else:
                chord = [pitch_of(pitches)]
            for pitch in chord:
                current.notes.append(smf.Note(start=start, end=start + duration,
                                              pitch=pitch, velocity=84 if current.name == "melody" else 62))

    for name in ("melody", "companion", "reference"):
        if name in tracks:
            midi.tracks.append(tracks[name])
    return midi


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    source = sys.argv[1]
    midi = parse(source)
    out = os.path.splitext(source)[0] + ".mid"
    smf.write(out, midi)
    print(f"{out}: {len(midi.tracks)} tracks, {midi.beats:.1f} beats at {midi.tempo_bpm:g} BPM")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
