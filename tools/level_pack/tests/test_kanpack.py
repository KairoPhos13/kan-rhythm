# Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
# See the LICENCE file in the kan-rhythm repository root for full licence text.
"""Offline tests for the level generator's own code (repo rule 5: DSP needs a
test with known input and expected output).

Run with: python3 -m unittest discover -s tools/level_pack/tests
"""

from __future__ import annotations

import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

try:
    import numpy as np  # noqa: E402
except ImportError:  # the CI's Python has only the standard library
    np = None

from kanpack import level as level_mod  # noqa: E402
from kanpack import loudness, smf  # noqa: E402

if np is not None:
    from kanpack import synth  # noqa: E402

# Packing needs only the standard library; rendering and the loudness
# measurement's tests need numpy, and are skipped without it.
needs_numpy = unittest.skipIf(np is None, "numpy is not installed (render-only tests)")

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


@needs_numpy
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


@needs_numpy
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

    def test_arrange_skips_a_prelude_and_counts_in_one_bar(self):
        midi = smf.MidiFile(tracks=[smf.Track(name="melody", notes=[
            smf.Note(start=0, end=2, pitch=60),     # prelude: cut
            smf.Note(start=3, end=5, pitch=62),     # straddles the cut: clipped
            smf.Note(start=8, end=9, pitch=64),
        ])])
        song = level_mod.Song(slug="t", title="t", meter=(3, 4), skip_beats=4)
        arranged, pulse = level_mod.arrange(midi, song)
        notes = arranged.tracks[0].notes
        self.assertEqual([(3, 4, 62), (7, 8, 64)], [(n.start, n.end, n.pitch) for n in notes])
        self.assertEqual(1, pulse)
        self.assertEqual([0, 1, 2], [n.start for n in arranged.tracks[pulse].notes])
        self.assertGreater(arranged.tracks[pulse].notes[0].velocity, arranged.tracks[pulse].notes[1].velocity)

    def test_arrange_without_count_in(self):
        midi = smf.MidiFile(tracks=[smf.Track(notes=[smf.Note(start=1, end=2, pitch=60)])])
        arranged, pulse = level_mod.arrange(midi, level_mod.Song(slug="t", title="t", count_in_beats=0))
        self.assertEqual(-1, pulse)
        self.assertEqual(1, len(arranged.tracks))
        self.assertEqual(1, arranged.tracks[0].notes[0].start)

    def test_background_is_a_png(self):
        data = level_mod.background_png("sakura", width=8, height=4)
        self.assertTrue(data.startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertTrue(data.endswith(b"IEND\xaeB`\x82"), "a complete PNG ends with IEND")

    def test_the_osu_carries_the_scores_marks(self):
        notes = [{"StartMs": 1000, "DurationMs": 500},
                 {"StartMs": 2000, "DurationMs": 2500}]
        lines = [l for l in level_mod.hit_objects_text(notes).splitlines() if l]
        self.assertEqual(["256,192,1000.000,1,0,0:0:0:0:",
                          "256,192,2000.000,1,0,0:0:0:0:",
                          "256,192,4500.000,1,0,0:0:0:0:"], lines,
                         "one mark per note's start and one at the level's end (the last note's end)")

        song = level_mod.Song(slug="t", title="t")
        text = level_mod.osu_text(song, notes, "audio.ogg", "background.png", 0)
        self.assertIn("[HitObjects]\n" + "".join(line + "\n" for line in lines), text)

    def test_a_level_without_notes_has_no_marks(self):
        self.assertEqual("", level_mod.hit_objects_text([]))
        song = level_mod.Song(slug="t", title="t")
        self.assertTrue(level_mod.osu_text(song, [], "audio.ogg", "background.png", 0).endswith("[HitObjects]\n"))

    def test_the_shipped_levels_carry_their_scores_marks(self):
        for name in sorted(os.listdir(CHARTS)):
            level_dir = os.path.join(CHARTS, name, "level")
            if not os.path.isdir(level_dir):
                continue
            with open(os.path.join(level_dir, "kan.score.json"), encoding="utf-8") as handle:
                notes = json.load(handle)["Notes"]
            expected = level_mod.hit_objects_text(notes).splitlines()

            osu_files = [f for f in sorted(os.listdir(level_dir)) if f.endswith(".osu")]
            self.assertEqual(1, len(osu_files), f"{name}: one .osu per level")
            with open(os.path.join(level_dir, osu_files[0]), encoding="utf-8") as handle:
                text = handle.read()
            section = text.split("[HitObjects]", 1)[1]
            written = [l for l in section.splitlines() if l.strip()]
            self.assertEqual(expected, written,
                             f"{name}: the .osu's marks are not its score's (run make_level.py osu)")

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
