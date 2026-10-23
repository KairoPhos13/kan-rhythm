// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Kan.Forge.Design;
using Kan.Forge.Media;
using Kan.Forge.Native;
using Kan.Forge.Score;
using Kan.Forge.Tracks;

namespace Kan.Forge.Tests
{
    /// <summary>Repairing the timeline's timing (owner 2026-10-22: a cover played without a click drifts).</summary>
    [TestFixture]
    public class TimelineEditTest
    {
        [Test]
        public void TimesMoveWithTheEdit()
        {
            var insert = TimelineEdit.InsertSilence(1000, 200);
            Assert.That(insert.Map(999), Is.EqualTo(999));
            Assert.That(insert.Map(1000), Is.EqualTo(1200), "what sits at the point is pushed after the silence");
            Assert.That(insert.Map(3000), Is.EqualTo(3200));

            var cut = TimelineEdit.Cut(2000, 1000); // either order
            Assert.That(cut.Map(500), Is.EqualTo(500));
            Assert.That(cut.Map(1500), Is.EqualTo(1000), "inside the cut: its point");
            Assert.That(cut.Map(2500), Is.EqualTo(1500));
            Assert.That(cut.Removes(1999), Is.True);
            Assert.That(cut.Removes(2000), Is.False);

            var stretch = TimelineEdit.Stretch(1000, 2000, 1500);
            Assert.That(stretch.Map(1500), Is.EqualTo(1750));
            Assert.That(stretch.Map(2000), Is.EqualTo(2500));
            Assert.That(stretch.Shifted(100).FromMs, Is.EqualTo(1100), "level time → set time");

            Assert.That(TimelineEdit.Stretch(1000, 2000, 1000).IsEmpty, Is.True);
            Assert.That(TimelineEdit.InsertSilence(1000, 0).IsEmpty, Is.True);
        }

        [Test]
        public void TheNextTempoIsPutBackOnTheBeat()
        {
            // 500 ms beats from 1000: the next change at 3120 is 120 ms late (cut), at 3380 120 ms early (put in).
            Assert.That(TimelineEdit.SyncNext(1000, 500, 3120), Is.EqualTo(TimelineEdit.Cut(3000, 3120)));
            Assert.That(TimelineEdit.SyncNext(1000, 500, 3380), Is.EqualTo(TimelineEdit.InsertSilence(3380, 120)));
            Assert.That(TimelineEdit.SyncNext(1000, 500, 3000.4), Is.Null, "already on a beat");

            // Afterwards the change is on a beat of the tempo before.
            var edit = TimelineEdit.SyncNext(1000, 500, 3380)!;
            Assert.That((edit.Map(3380) - 1000) % 500, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void TheScoreAndTheDesignFollow()
        {
            var score = new KanScore
            {
                Notes =
                {
                    new ScoreNote { StartMs = 500, DurationMs = 400, Midi = 72 },
                    new ScoreNote { StartMs = 1200, DurationMs = 400, Midi = 74 }, // cut away
                    new ScoreNote { StartMs = 1800, DurationMs = 400, Midi = 76 },
                },
            };

            Assert.That(TimelineEdit.Cut(1000, 1500).ApplyTo(score), Is.EqualTo(1));
            Assert.That(score.Notes.Select(n => n.StartMs), Is.EqualTo(new[] { 500.0, 1300 }));

            TimelineEdit.Stretch(0, 1000, 2000).ApplyTo(score);
            Assert.That(score.Notes[0].StartMs, Is.EqualTo(1000));
            Assert.That(score.Notes[0].DurationMs, Is.EqualTo(800), "a stretched note lasts longer");
            Assert.That(score.Notes[1].StartMs, Is.EqualTo(2300), "after the span: by the difference");

            var design = new KanDesign
            {
                Sections =
                {
                    new DesignSection { StartMs = 0, EndMs = 1000 },
                    new DesignSection { StartMs = 1000, EndMs = 1500 },
                    new DesignSection { StartMs = 1500, EndMs = 4000 },
                },
            };

            TimelineEdit.Cut(1000, 1500).ApplyTo(design);
            Assert.That(design.Sections.Select(s => (s.StartMs, s.EndMs)), Is.EqualTo(new[] { (0.0, 1000.0), (1000.0, 3500.0) }), "still covering, the cut one gone");
        }

        [Test]
        public void EachTrackTakesTheEditOnItsOwnFile()
        {
            // A 10 s file starting at 2 s on the set's timeline, trimmed and with a silenced part.
            var track = new ForgeTrack("companion", "a.wav") { ClipEndMs = 9000, OffsetMs = 2000 };
            track.Edits.Ranges.Add(new PendingRange(KanForgeEditMode.Mute, 6000, 7000));

            var inside = TimelineEdit.InsertSilence(5000, 500).PlanFor(track, 10000);
            Assert.That(inside.Render, Is.EqualTo(TimelineEdit.InsertSilence(3000, 500)), "in the file's own time");
            Assert.That(inside.OffsetMs, Is.EqualTo(2000));
            Assert.That(inside.ClipEndMs, Is.EqualTo(9500));
            Assert.That(inside.Edits.Ranges[0].FromMs, Is.EqualTo(6500), "pending edits move with the audio");

            var before = TimelineEdit.InsertSilence(1000, 500).PlanFor(track, 10000);
            Assert.That(before.Render, Is.Null, "before the file: it only moves");
            Assert.That(before.OffsetMs, Is.EqualTo(2500));
            Assert.That(before.ClipEndMs, Is.EqualTo(9000));

            Assert.That(TimelineEdit.Cut(13000, 14000).PlanFor(track, 10000).Render, Is.Null, "after the file: untouched");

            var overStart = TimelineEdit.Cut(1000, 3000).PlanFor(track, 10000);
            Assert.That(overStart.Render, Is.EqualTo(TimelineEdit.Cut(0, 1000)), "only the file's part of the cut");
            Assert.That(overStart.OffsetMs, Is.EqualTo(1000), "what followed the cut starts at its point");

            var stretch = TimelineEdit.Stretch(1000, 5000, 6000).PlanFor(track, 10000);
            Assert.That(stretch.Render, Is.EqualTo(TimelineEdit.Stretch(0, 3000, 4500)), "the file's part, at the same rate");
            Assert.That(stretch.OffsetMs, Is.EqualTo(2500));
        }

        [Test]
        public void TheAudioIsRetimed()
        {
            const int rate = 44100;
            string dir = Path.Combine(Path.GetTempPath(), "kan-forge-tests", "retime-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            string src = Path.Combine(dir, "tone.wav");

            // 1 s of 440 Hz.
            var tone = new float[rate];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / rate);
            MediaAudioExtractor.WriteWav(src, tone, tone.Length, 1, rate);

            float[] retime(double from, double to, double length)
            {
                string outWav = Path.Combine(dir, $"out-{Guid.NewGuid():N}.wav");
                Assert.That(MediaAudioExtractor.TryRetimeToWav(src, outWav, from, to, length, out string error), Is.True, error);
                return readPcm16(outWav);
            }

            double ms(float[] s) => s.Length * 1000.0 / rate;
            float rms(float[] s, double fromMs, double toMs)
            {
                int a = (int)(fromMs * rate / 1000), b = (int)(toMs * rate / 1000);
                return MathF.Sqrt(s[a..b].Select(x => x * x).Average());
            }

            var inserted = retime(400, 400, 500);
            Assert.That(ms(inserted), Is.EqualTo(1500).Within(1));
            Assert.That(rms(inserted, 450, 850), Is.LessThan(0.001f), "the silence");
            Assert.That(rms(inserted, 1000, 1400), Is.GreaterThan(0.3f), "the rest, later");

            var cut = retime(200, 600, 0);
            Assert.That(ms(cut), Is.EqualTo(600).Within(1));
            Assert.That(rms(cut, 250, 550), Is.GreaterThan(0.3f));

            var stretched = retime(0, 1000, 1500);
            Assert.That(ms(stretched), Is.EqualTo(1500).Within(1));

            // Slower, same pitch: count the rising zero crossings in the middle.
            int a0 = (int)(0.4 * rate), a1 = (int)(1.1 * rate), rises = 0;
            for (int i = a0 + 1; i < a1; i++)
                if (stretched[i - 1] < 0 && stretched[i] >= 0) rises++;
            Assert.That(rises / 0.7, Is.EqualTo(440).Within(440 * 0.03), "the pitch is kept");
        }

        private sealed class Carried : ForgeSetHistory.IStepState
        {
            public string Value = "before";

            public object Capture() => Value;

            public void Restore(object state) => Value = (string)state;
        }

        [Test]
        public void UndoBringsTheFileAndWhatTheStepCarriedBack()
        {
            var set = new ForgeTrackSet();
            var track = new ForgeTrack("reference", "old.wav") { Peaks = new float[] { 1 } };
            set.Add(track);

            var history = new ForgeSetHistory(set);
            var carried = new Carried();
            history.Record(null, carried);
            Assert.That(history.UndoLink, Is.SameAs(carried));

            track.Path = "repaired.wav";
            track.Peaks = new float[] { 2 };
            carried.Value = "after";

            history.Undo();
            Assert.That(track.Path, Is.EqualTo("old.wav"));
            Assert.That(track.Peaks, Is.EqualTo(new float[] { 1 }), "its waveform with it");
            Assert.That(carried.Value, Is.EqualTo("before"));
            Assert.That(history.RedoLink, Is.SameAs(carried));

            history.Redo();
            Assert.That(track.Path, Is.EqualTo("repaired.wav"));
            Assert.That(carried.Value, Is.EqualTo("after"));

            // The score's history keeps its linked entry in step.
            var scores = new ScoreHistory();
            scores.Record(new KanScore(), link: carried);
            Assert.That(scores.UndoLink, Is.SameAs(carried));
            scores.Undo(new KanScore());
            Assert.That(scores.RedoLink, Is.SameAs(carried));
        }

        private static float[] readPcm16(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var samples = new float[(bytes.Length - 44) / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(bytes, 44 + i * 2) / 32767f;
            return samples;
        }
    }
}
