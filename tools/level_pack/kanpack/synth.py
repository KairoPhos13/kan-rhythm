# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""The gentle synth that renders a level's companion and reference audio.

Route (b) of the song survey: kan! renders its own companion from a public
domain score or MIDI, so the audio is as free as the score. The first choice
is a real synthesiser; the survey's environment (no root, no FluidRIL sound
font to hand) makes a small additive synth the reproducible answer, and it
keeps the render's licence our own (CC0). See tools/level_pack/README.md.

The voice is deliberately plain: a fundamental with two quiet harmonics, a
short attack and release, loud as the note's velocity. The companion voice
decays gently like a plucked string; the melody voice holds like a flute.
Nothing here runs on an audio callback path; it renders offline to a buffer.
"""

from __future__ import annotations

import numpy as np

from . import smf

SAMPLE_RATE = 44100

ATTACK_S = 0.012
RELEASE_S = 0.09

# Velocity (1-127) to gain: audible but never harsh.
def _velocity_gain(velocity: int) -> float:
    return 0.16 + 0.30 * (max(1, min(127, velocity)) / 127)


def _harmonics(timbre: str) -> list[tuple[int, float]]:
    if timbre == "melody":
        return [(1, 1.0), (2, 0.22), (3, 0.07)]
    return [(1, 1.0), (2, 0.30), (3, 0.12), (4, 0.05)]


def _voice(timbre: str, frequency: float, start: float, duration: float,
           gain: float, tail: float) -> np.ndarray:
    """One note as samples starting at 0 s, long enough for its tail."""
    length = int((duration + tail) * SAMPLE_RATE)
    t = np.arange(length, dtype=float) / SAMPLE_RATE
    attack = max(1, int(ATTACK_S * SAMPLE_RATE))
    release = max(1, int(RELEASE_S * SAMPLE_RATE))

    envelope = np.ones(length)
    envelope[:attack] = np.linspace(0, 1, attack)
    if release < length:
        envelope[-release:] = np.linspace(1, 0, release)

    if timbre != "melody":
        # A plucked companion: the partials fade at different speeds.
        envelope *= np.exp(-t / max(0.35, duration))

    wave = np.zeros(length)
    for partial, weight in _harmonics(timbre):
        wave += weight * np.sin(2 * np.pi * frequency * partial * t)

    return wave * envelope * gain


def render(midi: smf.MidiFile, tracks: list[int], tempo_bpm: float,
           timbres: dict[int, str] | None = None,
           gains: dict[int, float] | None = None,
           tail: float = 0.9) -> np.ndarray:
    """Render the given tracks of a MIDI (times in beats) to mono samples.

    ``tempo_bpm`` retimes the whole file: the generator always plays at the
    calm tempo the level was designed for, whatever the source file said.
    ``timbres`` maps a track index to "melody" or "companion".
    """
    timbres = timbres or {}
    gains = gains or {}
    beat_s = 60.0 / tempo_bpm
    total = midi.beats * beat_s
    mix = np.zeros(int((total + tail) * SAMPLE_RATE) + 1)

    for index in tracks:
        if index >= len(midi.tracks):
            continue
        track = midi.tracks[index]
        timbre = timbres.get(index, "companion")
        gain_scale = gains.get(index, 1.0)
        for note in track.notes:
            frequency = 440.0 * 2 ** ((note.pitch - 69) / 12)
            voice = _voice(timbre, frequency, note.start * beat_s,
                           max(0.05, note.duration * beat_s),
                           _velocity_gain(note.velocity) * gain_scale, tail)
            start = int(note.start * beat_s * SAMPLE_RATE)
            end = min(len(mix), start + len(voice))
            if end > start:
                mix[start:end] += voice[:end - start]

    # Overlapping notes can add up past full scale; keep it gentle.
    return np.tanh(mix * 0.9)
