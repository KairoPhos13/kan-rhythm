# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""Offline tests for the level generator's own code (repo rule 5: DSP needs a
test with known input and expected output).

Run with: python3 -m unittest discover -s tools/level_pack/tests
"""

from __future__ import annotations

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402

from kanpack import level as level_mod  # noqa: E402
from kanpack import loudness, smf, synth  # noqa: E402

TEST_DIR = os.path.dirname(os.path.abspath(__file__))
PACK_DIR = os.path.dirname(TEST_DIR)
REPO_ROOT = os.path.dirname(os.path.dirname(PACK_DIR))
CHARTS = os.path.join(REPO_ROOT, "charts", "official")


class SmfTest(unittest.TestCase):
    def test_write_read_round_trip(self):
        path = os.path.join(TEST_DIR, "_round_trip.mid")
        midi = smf.MidiFile(ticks_per_beat=480, tempo_bpm=90, numerator=3, denominator=4)
        melody = smf.Track(name="melody", program=73)
        melody.notes = [smf.Note(start=0, end=1, pitch=69, velocity=80),
                        smf.Note(start=1, end=2.5, pitch=72, velocity=80)]
        midi.tracks = [melody]
        smf.write(path, midi)
        try:
            back = smf.read(path)
            self.assertEqual(1, len(back.tracks))
            self.assertAlmostEqual(90, back.tempo_bpm, places=2)
            self.assertEqual((3, 4), (back.numerator, back.denominator))
            self.assertEqual([69, 72], [n.pitch for n in back.tracks[0].notes])
            self.assertAlmostEqual(1.0, back.tracks[0].notes[0].duration, places=3)
            self.assertAlmostEqual(2.5, back.tracks[0].notes[1].end, places=3)
        finally:
            os.remove(path)

    def test_reads_a_committed_source(self):
        midi = smf.read(os.path.join(CHARTS, "sakura-sakura", "sources", "sakura-sakura.mid"))
        self.assertEqual(2, len(midi.tracks), "melody and companion")
        self.assertAlmostEqual(88, midi.tempo_bpm, places=1)
        first = midi.tracks[0].notes[0]
        self.assertEqual(69, first.pitch, "the score starts on A4")
        self.assertAlmostEqual(1.0, first.duration, places=3)


class LoudnessTest(unittest.TestCase):
    """Known input and expected output for the BS.1770 measurement."""

    def test_full_scale_sine(self):
        # 1 kHz at full scale: -3.01 dB mean square plus the K-weighting's
        # +0.68 dB at 1 kHz and the -0.691 offset = about -3.05 LUFS.
        t = np.arange(44100 * 3) / 44100
        sine = np.sin(2 * np.pi * 1000 * t)
        self.assertAlmostEqual(-3.05, loudness.integrated_loudness(sine, 44100), places=1)

    def test_halving_the_signal_is_6_db(self):
        t = np.arange(44100 * 2) / 44100
        sine = np.sin(2 * np.pi * 1000 * t)
        difference = loudness.integrated_loudness(sine) - loudness.integrated_loudness(0.5 * sine)
        self.assertAlmostEqual(6.02, difference, places=1)

    def test_levels_to_the_reference(self):
        t = np.arange(44100 * 2) / 44100
        quiet = 0.02 * np.sin(2 * np.pi * 440 * t)
        levelled = loudness.level(quiet)
        self.assertAlmostEqual(loudness.REFERENCE_LUFS,
                               loudness.integrated_loudness(levelled), places=1)
        self.assertLessEqual(loudness.sample_peak(levelled), 10 ** (loudness.CEILING_DBFS / 20) + 1e-6)

    def test_peak_holds_when_the_target_would_clip(self):
        t = np.arange(44100 * 2) / 44100
        loud = 0.9 * np.sin(2 * np.pi * 440 * t)
        levelled = loudness.level(loud)
        self.assertLessEqual(loudness.sample_peak(levelled), 10 ** (loudness.CEILING_DBFS / 20) + 1e-6)

    def test_silence(self):
        self.assertEqual(-float("inf"), loudness.integrated_loudness(np.zeros(44100)))
        self.assertEqual(1.0, loudness.loudness_gain(-float("inf"), 0.0))


class SynthTest(unittest.TestCase):
    def test_renders_the_note_it_is_given(self):
        midi = smf.MidiFile(tempo_bpm=60)  # one beat = one second
        track = smf.Track(name="melody")
        track.notes = [smf.Note(start=0, end=1, pitch=69, velocity=90)]
        midi.tracks = [track]
        samples = synth.render(midi, [0], 60.0, timbres={0: "melody"})
        self.assertAlmostEqual(1.9, len(samples) / synth.SAMPLE_RATE, places=1,
                               msg="one note plus the synth's tail")

        window = samples[:synth.SAMPLE_RATE]
        spectrum = np.abs(np.fft.rfft(window))
        peak_hz = np.argmax(spectrum) * synth.SAMPLE_RATE / len(window)
        self.assertAlmostEqual(440.0, peak_hz, delta=1.0, msg="A4 = 440 Hz is the loudest partial")


class LevelTest(unittest.TestCase):
    def test_score_notes_follow_the_midi_and_the_transposition(self):
        midi = smf.MidiFile(tempo_bpm=60)
        track = smf.Track(name="melody")
        track.notes = [smf.Note(start=0, end=1, pitch=69, velocity=127),
                       smf.Note(start=1, end=2, pitch=71, velocity=64)]
        midi.tracks = [track]

        notes = level_mod.score_notes(midi, 0, 12, 60.0)
        self.assertEqual([81, 83], [n["Midi"] for n in notes])
        self.assertEqual([0.0, 1000.0], [n["StartMs"] for n in notes])
        self.assertEqual([1000.0, 1000.0], [n["DurationMs"] for n in notes])
        self.assertGreater(notes[0]["Intensity"], notes[1]["Intensity"], "velocity becomes intensity")

    def test_the_design_covers_the_whole_level(self):
        notes = [{"StartMs": 0, "DurationMs": 500}, {"StartMs": 500, "DurationMs": 500},
                 {"StartMs": 12000, "DurationMs": 500}]
        sections = level_mod.design_sections(notes, 13000)
        self.assertEqual(0.0, sections[0]["StartMs"])
        self.assertEqual(13000.0, sections[-1]["EndMs"])
        for before, after in zip(sections, sections[1:]):
            self.assertEqual(before["EndMs"], after["StartMs"], "sections cover the timeline without gaps")
        self.assertEqual({"Phrase", "Break"}, {s["Kind"] for s in sections})
        self.assertTrue(all(s["Climax"] is False for s in sections), "the starter levels stay calm")

    def test_the_design_keeps_long_rests_as_intro_and_outro(self):
        notes = [{"StartMs": 5000, "DurationMs": 500}, {"StartMs": 14000, "DurationMs": 500}]
        sections = level_mod.design_sections(notes, 20000)
        self.assertEqual("Intro", sections[0]["Kind"])
        self.assertEqual("Outro", sections[-1]["Kind"])
        self.assertEqual(20000.0, sections[-1]["EndMs"])

    def test_background_is_a_png(self):
        data = level_mod.background_png("sakura", width=8, height=4)
        self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertTrue(data.endswith(b"IEND\xaeB`\x82"), "a complete PNG ends with IEND")

    def test_every_song_folder_is_complete(self):
        for name in sorted(os.listdir(CHARTS)):
            folder = os.path.join(CHARTS, name)
            if not os.path.isfile(os.path.join(folder, "song.json")):
                continue
            song = level_mod.Song.load(os.path.join(folder, "song.json"))
            self.assertTrue(os.path.isfile(os.path.join(folder, song.midi)), f"{name}: the MIDI is missing")
            self.assertTrue(os.path.isfile(os.path.join(folder, "CREDITS.md")), f"{name}: CREDITS.md is missing")
            self.assertTrue(song.sources, f"{name}: no sources recorded")
            self.assertTrue(song.licence, f"{name}: no licence recorded")


if __name__ == "__main__":
    unittest.main()
