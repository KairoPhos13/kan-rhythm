# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""A small Standard MIDI File (SMF) reader and writer.

The level generator needs two things from a MIDI file: which notes each track
plays, and where the tempo sits. Everything else (controllers, lyrics, system
exclusive) is passed over. The reader handles formats 0 and 1, running status
and SMPTE-free time divisions (ticks per beat), which covers every file the
pack's sources use. The writer emits format 1 with one track per part.

Keeping this here means the generator has no third-party dependency: the same
Python that builds the levels on a maintainer's machine builds them in CI.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field


@dataclass
class Note:
    """One note: where it starts and ends (in beats from the file's start)."""

    start: float
    end: float
    pitch: int
    velocity: int = 90

    @property
    def duration(self) -> float:
        return self.end - self.start


@dataclass
class Track:
    name: str = ""
    program: int = 0
    channel: int = 0
    notes: list[Note] = field(default_factory=list)


@dataclass
class MidiFile:
    ticks_per_beat: int = 480
    tempo_bpm: float = 120.0
    numerator: int = 4
    denominator: int = 4
    tracks: list[Track] = field(default_factory=list)

    @property
    def beats(self) -> float:
        """The length of the file in beats."""
        return max((n.end for t in self.tracks for n in t.notes), default=0.0)


# ----------------------------------------------------------------------
# reading
# ----------------------------------------------------------------------


class _Reader:
    def __init__(self, data: bytes):
        self.data = data
        self.pos = 0

    def byte(self) -> int:
        b = self.data[self.pos]
        self.pos += 1
        return b

    def take(self, n: int) -> bytes:
        chunk = self.data[self.pos:self.pos + n]
        self.pos += n
        return chunk

    def uint(self, n: int) -> int:
        return int.from_bytes(self.take(n), "big")

    def varlen(self) -> int:
        value = 0
        while True:
            b = self.byte()
            value = (value << 7) | (b & 0x7F)
            if not b & 0x80:
                return value


def read(path: str) -> MidiFile:
    """Read an SMF into note lists per track, times in beats."""
    with open(path, "rb") as handle:
        data = handle.read()

    if data[:4] != b"MThd":
        raise ValueError(f"{path}: not a Standard MIDI File")

    header_len = int.from_bytes(data[4:8], "big")
    fmt, ntrks, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise ValueError(f"{path}: SMPTE time division is not supported")

    out = MidiFile(ticks_per_beat=division)
    reader = _Reader(data[8 + header_len:])

    tempo_us = 500000  # 120 BPM until the file says otherwise
    for _ in range(ntrks):
        if reader.pos + 8 > len(reader.data):
            break
        chunk_id = reader.take(4)
        if chunk_id != b"MTrk":
            raise ValueError(f"{path}: expected an MTrk chunk")
        length = reader.uint(4)
        end = reader.pos + length

        track = Track()
        running_status = 0
        tick = 0
        open_notes: dict[tuple[int, int], tuple[int, int]] = {}

        def close_note(key, off_tick):
            start_tick, velocity = open_notes.pop(key)
            track.notes.append(Note(
                start=start_tick / division,
                end=max(off_tick / division, start_tick / division + 1e-6),
                pitch=key[1],
                velocity=velocity,
            ))

        while reader.pos < end:
            tick += reader.varlen()
            status = reader.data[reader.pos]
            if status & 0x80:
                reader.pos += 1
                running_status = status
            else:
                status = running_status

            kind = status & 0xF0
            channel = status & 0x0F

            if status == 0xFF:  # meta
                meta_type = reader.byte()
                meta_len = reader.varlen()
                payload = reader.take(meta_len)
                if meta_type == 0x51 and meta_len == 3:
                    tempo_us = int.from_bytes(payload, "big")
                    out.tempo_bpm = round(60_000_000 / tempo_us, 3)
                elif meta_type == 0x58 and meta_len >= 2:
                    out.numerator = payload[0]
                    out.denominator = 2 ** payload[1]
                elif meta_type == 0x03:
                    track.name = payload.decode("latin-1")
            elif status in (0xF0, 0xF7):  # sysex
                reader.take(reader.varlen())
            elif kind in (0x80, 0x90, 0xA0, 0xB0, 0xE0):
                d1, d2 = reader.byte(), reader.byte()
                if kind == 0x90 and d2 > 0:
                    open_notes.setdefault((channel, d1), (tick, d2))
                elif kind in (0x80, 0x90):
                    key = (channel, d1)
                    if key in open_notes:
                        close_note(key, tick)
            elif kind in (0xC0, 0xD0):
                d1 = reader.byte()
                if kind == 0xC0:
                    track.program = d1
                    track.channel = channel

        for key, (start_tick, velocity) in list(open_notes.items()):
            close_note(key, tick)

        track.notes.sort(key=lambda n: n.start)
        if track.notes:
            out.tracks.append(track)
        reader.pos = end

    return out


# ----------------------------------------------------------------------
# writing
# ----------------------------------------------------------------------


def _varlen(value: int) -> bytes:
    value = max(0, int(value))
    out = bytearray([value & 0x7F])
    value >>= 7
    while value:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    return bytes(reversed(out))


def write(path: str, midi: MidiFile) -> None:
    """Write tracks as an SMF format 1 file (track 0 carries tempo and meter)."""
    header = b"MThd" + struct.pack(">IHHH", 6, 1, len(midi.tracks) + 1, midi.ticks_per_beat)

    tempo_us = int(round(60_000_000 / midi.tempo_bpm))
    conductor = bytearray()
    conductor += _varlen(0) + b"\xff\x51\x03" + tempo_us.to_bytes(3, "big")
    conductor += _varlen(0) + b"\xff\x58\x04" + bytes([midi.numerator, {1: 0, 2: 1, 4: 2, 8: 3, 16: 4}.get(midi.denominator, 2), 24, 8])
    conductor += _varlen(0) + b"\xff\x2f\x00"

    chunks = [b"MTrk" + struct.pack(">I", len(conductor)) + bytes(conductor)]

    for track in midi.tracks:
        events: list[tuple[int, int, bytes]] = []
        if track.name:
            payload = track.name.encode("latin-1")
            events.append((0, 0, b"\xff\x03" + _varlen(len(payload)) + payload))
        events.append((0, 1, bytes([0xC0 | (track.channel & 0x0F), track.program & 0x7F])))

        tpq = midi.ticks_per_beat
        for note in track.notes:
            start = int(round(note.start * tpq))
            end = max(start + 1, int(round(note.end * tpq)))
            events.append((start, 3, bytes([0x90 | (track.channel & 0x0F), note.pitch & 0x7F, max(1, min(127, note.velocity))])))
            events.append((end, 2, bytes([0x80 | (track.channel & 0x0F), note.pitch & 0x7F, 0])))

        events.sort(key=lambda e: (e[0], e[1]))
        body = bytearray()
        last = 0
        for tick, _, payload in events:
            body += _varlen(tick - last) + payload
            last = tick
        body += _varlen(0) + b"\xff\x2f\x00"
        chunks.append(b"MTrk" + struct.pack(">I", len(body)) + bytes(body))

    with open(path, "wb") as handle:
        handle.write(header)
        for chunk in chunks:
            handle.write(chunk)
