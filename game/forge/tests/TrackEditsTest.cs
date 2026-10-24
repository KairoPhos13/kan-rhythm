// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Kan.Core.Native;
using Kan.Core.Transcription;
using Kan.Core.Tracks;
using Kan.Forge.Media;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// Hear-before-save edits, undo/redo and the saved set (owner
    /// 2026-10-14). Preview and render must follow the same rules.
    /// </summary>
    [TestFixture]
    public class TrackEditsTest
    {
        [Test]
        public void TestPreviewAudibility()
        {
            var e = new TrackEdits();
            Assert.That(e.IsAudibleAt(5000), Is.True, "no edits: everything is heard");

            e.Ranges.Add(new PendingRange(KanForgeEditMode.Keep, 1000, 9000));
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Mute, 4000, 5000));

            Assert.That(e.IsAudibleAt(500), Is.False, "outside the kept range");
            Assert.That(e.IsAudibleAt(2000), Is.True);
            Assert.That(e.IsAudibleAt(4500), Is.False, "inside the silenced range");
            Assert.That(e.IsAudibleAt(9000), Is.False, "ranges are [from, to)");
        }

        [Test]
        public void TestSeveralKeepsAddUp()
        {
            // "keep this" twice: both parts stay, everything else is muted
            // (owner 2026-10-14), and the timing is preserved.
            var e = new TrackEdits();
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Keep, 1000, 2000));
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Keep, 5000, 6000));

            Assert.That(e.IsAudibleAt(1500), Is.True);
            Assert.That(e.IsAudibleAt(5500), Is.True, "the second kept part is heard too");
            Assert.That(e.IsAudibleAt(3000), Is.False, "between kept parts is muted");
            Assert.That(e.IsAudibleAt(500), Is.False);

            var plan = e.RenderPlan();
            Assert.That(plan.All(p => p.Mode == KanForgeEditMode.Mute), Is.True, "keeps render as muting the gaps (timing preserved)");
            Assert.That(plan.Select(p => (p.FromMs, p.ToMs)).Take(2), Is.EqualTo(new[] { (0.0, 1000.0), (2000.0, 5000.0) }));
            Assert.That(plan[^1].FromMs, Is.EqualTo(6000), "…and everything after the last kept part");
            Assert.That(e.ResultOffsetMs(100), Is.EqualTo(100), "keeps never move the track");
        }

        [Test]
        public void TestNonsenseCombinationsAreFlagged()
        {
            var e = new TrackEdits();
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Trim, 10000, 20000));
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Keep, 1000, 2000));    // outside the trim
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Keep, 12000, 15000));
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Mute, 16000, 17000));  // outside every kept part

            var issues = e.Validate();
            Assert.That(issues.Any(i => i.IsError), Is.False, "something is still heard: no error");
            Assert.That(issues.Count(i => i.Message.Contains("outside the trim")), Is.EqualTo(1));
            Assert.That(issues.Count(i => i.Message.Contains("already silent")), Is.EqualTo(1));

            var silenced = new TrackEdits();
            silenced.Ranges.Add(new PendingRange(KanForgeEditMode.Keep, 1000, 2000));
            silenced.Ranges.Add(new PendingRange(KanForgeEditMode.Mute, 0, 3000));
            Assert.That(silenced.Validate().Any(i => i.IsError), Is.True, "everything silenced = nothing to hear");
            Assert.That(silenced.IsRenderable, Is.False);

            var apart = new TrackEdits();
            apart.Ranges.Add(new PendingRange(KanForgeEditMode.Trim, 0, 1000));
            apart.Ranges.Add(new PendingRange(KanForgeEditMode.Trim, 2000, 3000));
            Assert.That(apart.Validate().Single().IsError, Is.True, "trims that don't overlap");
        }

        [Test]
        public void TestLegacySetsReadAsCompanion()
        {
            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests", Guid.NewGuid().ToString("N")[..8], "set.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                { "Version": 1, "SongStartMs": 400, "SongEndMs": 9000,
                  "Tracks": [ { "Name": "a", "Path": "/tmp/a.wav", "IsAccompaniment": true },
                              { "Name": "m", "Path": "/tmp/m.wav", "IsMusic": true },
                              { "Name": "r", "Path": "/tmp/r.wav", "IsReference": true } ] }
                """);

            var doc = ForgeSetFile.Load(path)!;
            var tracks = ForgeSetFile.ToTracks(doc).Select(t => t.Track).ToList();

            Assert.That(tracks[0].IsCompanion, Is.True, "accompaniment → companion");
            Assert.That(tracks[1].IsCompanion, Is.True, "music → companion (it was the level audio)");
            Assert.That(tracks[2].IsCompanion, Is.False);
            Assert.That(doc.AudioStartMs, Is.EqualTo(400), "the old song window is the level audio window");
            Assert.That(doc.AudioEndMs, Is.EqualTo(9000));

            ForgeSetFile.Save(doc, path);
            string saved = File.ReadAllText(path);
            Assert.That(saved, Does.Not.Contain("IsAccompaniment").And.Not.Contain("IsMusic").And.Not.Contain("SongStartMs"),
                "old names are read, never written");
        }

        [Test]
        public void TestTrimsRenderLastAndMoveTheResult()
        {
            var e = new TrackEdits();
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Trim, 1000, 8000));
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Mute, 2000, 3000));
            e.Ranges.Add(new PendingRange(KanForgeEditMode.Trim, 1500, 9000));

            var plan = e.RenderPlan();
            Assert.That(plan.Select(p => p.Mode), Is.EqualTo(new[] { KanForgeEditMode.Mute, KanForgeEditMode.Trim }),
                "timing-preserving edits first, then a single trim");
            Assert.That(plan[1].FromMs, Is.EqualTo(1500), "trims intersect");
            Assert.That(plan[1].ToMs, Is.EqualTo(8000));

            Assert.That(e.ResultOffsetMs(2000), Is.EqualTo(3500), "the trimmed result starts where its audio was");
            Assert.That(e.IsAudibleAt(1200), Is.False, "preview treats trims as keeps");
            Assert.That(e.IsRenderable, Is.True);

            e.Ranges.Add(new PendingRange(KanForgeEditMode.Trim, 8500, 9000));
            Assert.That(e.IsRenderable, Is.False, "disjoint trims leave nothing to save");
        }

        [Test]
        public void TestUndoRedoRestoresStateAndCreatedTracks()
        {
            var set = new ForgeTrackSet();
            var a = set.Import("a", "/tmp/a.wav")!;
            var history = new ForgeSetHistory(set);

            history.Record();
            a.Muted = true;
            a.OffsetMs = 1200;
            a.Edits.LowCutHz = 200;

            history.Record();
            var created = set.Import("a (edit)", "/tmp/a-edit.wav")!;
            history.MarkCreated(created);

            var imported = set.Import("later import", "/tmp/c.wav")!; // from Resources, not the tab

            Assert.That(history.Undo(), Is.True);
            Assert.That(set.Tracks, Does.Not.Contain(created), "undo removes tracks the tab created");
            Assert.That(set.Tracks, Does.Contain(imported), "…but never imports made elsewhere");

            Assert.That(history.Undo(), Is.True);
            Assert.That(a.Muted, Is.False);
            Assert.That(a.OffsetMs, Is.EqualTo(0));
            Assert.That(a.Edits.LowCutHz, Is.EqualTo(0));
            Assert.That(history.CanUndo, Is.False);

            Assert.That(history.Redo(), Is.True);
            Assert.That(a.Muted, Is.True);
            Assert.That(a.OffsetMs, Is.EqualTo(1200));

            Assert.That(history.Redo(), Is.True);
            Assert.That(set.Tracks, Does.Contain(created), "redo brings created tracks back");
        }

        [Test]
        public void TestUndoCoversTrimsAndTheSongWindow()
        {
            var set = new ForgeTrackSet();
            var a = set.Import("a", "/tmp/a.wav")!;
            var history = new ForgeSetHistory(set);

            history.Record();
            a.ClipStartMs = 300;
            a.ClipEndMs = 900;
            set.AudioStartMs = 1000;
            set.AudioEndMs = 61000;

            Assert.That(a.InClip(200), Is.False);
            Assert.That(a.InClip(300), Is.True);
            Assert.That(a.InClip(900), Is.False, "the window is [start, end)");
            Assert.That(a.ClipEndOr(700), Is.EqualTo(700), "a window past the file ends with the file");

            history.Undo();
            Assert.That(a.IsClipped, Is.False);
            Assert.That(set.HasAudioWindow, Is.False);

            history.Redo();
            Assert.That(a.ClipEndMs, Is.EqualTo(900));
            Assert.That(set.AudioEndMs, Is.EqualTo(61000));
        }

        [Test]
        public void TestRepeatedChangesCoalesce()
        {
            var set = new ForgeTrackSet();
            var a = set.Import("a", "/tmp/a.wav")!;
            var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            var history = new ForgeSetHistory(set) { Now = () => now };

            for (int i = 1; i <= 5; i++)
            {
                history.Record("gain");
                a.Gain = 1 + i * 0.1f;
                now = now.AddMilliseconds(100);
            }

            history.Undo();
            Assert.That(a.Gain, Is.EqualTo(1), "a slider drag is one undo step");
            Assert.That(history.CanUndo, Is.False);
        }

        [Test]
        public void TestSetFileRoundTrip()
        {
            var set = new ForgeTrackSet();
            var a = set.Import("take", "/tmp/take.wav")!;
            a.Gain = 0.7f;
            a.OffsetMs = 2500;
            a.IsReference = true;
            a.Edits.Ranges.Add(new PendingRange(KanForgeEditMode.Mute, 100, 200));
            a.Edits.HighCutHz = 8000;
            a.ClipStartMs = 150;
            a.ClipEndMs = 4000;
            set.AudioStartMs = 500;
            set.AudioEndMs = 90000;
            var v = set.Import("clip (video audio)", "/tmp/clip.wav")!;
            v.IsCompanion = true;

            var draft = new TranscriptionDraft(new[] { new KanDraftNote { OnsetMs = 10, DurMs = 90, Midi = 60 } });
            string path = Path.Combine(Path.GetTempPath(), "kan-forge-tests", Guid.NewGuid().ToString("N")[..8], "set.json");

            ForgeSetFile.Save(ForgeSetFile.From(set, t => t == v, a.Path, draft), path);
            var doc = ForgeSetFile.Load(path);

            Assert.That(doc, Is.Not.Null);
            var tracks = ForgeSetFile.ToTracks(doc!);
            Assert.That(tracks.Select(t => t.Track.Name), Is.EqualTo(new[] { "take", "clip (video audio)" }));

            var take = tracks[0].Track;
            Assert.That(take.Gain, Is.EqualTo(0.7f));
            Assert.That(take.OffsetMs, Is.EqualTo(2500));
            Assert.That(take.IsReference, Is.True);
            Assert.That(take.Edits.Ranges.Single().ToMs, Is.EqualTo(200));
            Assert.That(take.Edits.HighCutHz, Is.EqualTo(8000));
            Assert.That(take.ClipStartMs, Is.EqualTo(150));
            Assert.That(take.ClipEndMs, Is.EqualTo(4000));
            Assert.That(doc!.AudioStartMs, Is.EqualTo(500));
            Assert.That(doc.AudioEndMs, Is.EqualTo(90000));
            Assert.That(tracks[1].FromVideo, Is.True);
            Assert.That(tracks[1].Track.IsCompanion, Is.True);

            Assert.That(doc!.TranscriptionSource, Is.EqualTo(a.Path));
            Assert.That(doc.TranscriptionNotes.Single().Midi, Is.EqualTo(60));

            Assert.That(ForgeSetFile.Load(path + ".missing"), Is.Null, "no file = no saved set");
        }

        [Test]
        public void TestFilterRenderAttenuatesOutsideThePassband()
        {
            const int rate = 48000;
            string dir = Path.Combine(Path.GetTempPath(), "kan-forge-tests", Guid.NewGuid().ToString("N")[..8]);
            string low = writeSine(Path.Combine(dir, "100hz.wav"), 100, rate);
            string high = writeSine(Path.Combine(dir, "5khz.wav"), 5000, rate);

            // High cut at 1 kHz: a 5 kHz tone drops, a 100 Hz tone passes.
            Assert.That(MediaAudioExtractor.TryFilterToWav(high, Path.Combine(dir, "5k-hc.wav"), 0, 1000, out string err), Is.True, err);
            Assert.That(MediaAudioExtractor.TryFilterToWav(low, Path.Combine(dir, "100-hc.wav"), 0, 1000, out err), Is.True, err);
            Assert.That(rms(Path.Combine(dir, "5k-hc.wav")), Is.LessThan(0.05f), "high cut removes content above the cutoff");
            Assert.That(rms(Path.Combine(dir, "100-hc.wav")), Is.GreaterThan(0.3f), "…and keeps content below it");

            // Low cut at 1 kHz: the reverse.
            Assert.That(MediaAudioExtractor.TryFilterToWav(low, Path.Combine(dir, "100-lc.wav"), 1000, 0, out err), Is.True, err);
            Assert.That(MediaAudioExtractor.TryFilterToWav(high, Path.Combine(dir, "5k-lc.wav"), 1000, 0, out err), Is.True, err);
            Assert.That(rms(Path.Combine(dir, "100-lc.wav")), Is.LessThan(0.05f), "low cut removes content below the cutoff");
            Assert.That(rms(Path.Combine(dir, "5k-lc.wav")), Is.GreaterThan(0.3f), "…and keeps content above it");
        }

        /// <summary>1 s mono PCM16 sine at amplitude 0.5 (RMS ≈ 0.354).</summary>
        private static string writeSine(string path, float hz, int rate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var w = new BinaryWriter(File.Create(path));
            int n = rate;
            w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVEfmt "u8.ToArray());
            w.Write(16); w.Write((ushort)1); w.Write((ushort)1); w.Write(rate); w.Write(rate * 2);
            w.Write((ushort)2); w.Write((ushort)16); w.Write("data"u8.ToArray()); w.Write(n * 2);
            for (int i = 0; i < n; i++)
                w.Write((short)(0.5f * MathF.Sin(2 * MathF.PI * hz * i / rate) * 32767));
            return path;
        }

        /// <summary>RMS of a PCM16 WAV, skipping the first 50 ms (filter settling).</summary>
        private static float rms(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int channels = BitConverter.ToInt16(bytes, 22);
            int rate = BitConverter.ToInt32(bytes, 24);
            int start = 44 + rate / 20 * channels * 2;
            double sum = 0;
            int count = 0;

            for (int i = start; i + 1 < bytes.Length; i += 2)
            {
                float s = BitConverter.ToInt16(bytes, i) / 32767f;
                sum += s * s;
                count++;
            }

            return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
        }
    }
}
