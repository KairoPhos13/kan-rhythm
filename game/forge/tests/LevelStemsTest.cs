// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Database;
using Kan.Core.Tracks;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// A level keeps its stems (issue 42): the stems of its song as the
    /// creator worked with them, encoded once into the level under "stems/",
    /// with a manifest saying what they are. The same stems are attached
    /// once; stems that are not of one song (lengths or formats apart) are
    /// refused with a clear error.
    /// </summary>
    [TestFixture]
    public class LevelStemsTest
    {
        private const int rate = 44100;

        [Test]
        public void TestALevelKeepsItsStemsAsOggWithAManifest() => LevelTestBed.Run(async bed =>
        {
            string work = workFolder();
            var separated = demuxOutput(work, ("bass", 8820), ("vocals", 8820));

            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>());
            var working = bed.WorkingOf(set);

            string? problem = null;
            bool attached = await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null, m => problem = m);

            Assert.That(attached, Is.True, problem);
            Assert.That(set.Files.Where(f => LevelStems.IsStemFile(f.Filename)).Select(f => f.Filename),
                        Is.EquivalentTo(new[] { "stems/bass.ogg", "stems/vocals.ogg", LevelStems.MANIFEST }));

            // The stems are Ogg at the stem quality, not the raw WAV.
            byte[] bass = bed.ReadFile(working, "stems/bass.ogg")!;
            Assert.That(bass.Take(4).ToArray(), Is.EqualTo("OggS"u8.ToArray()));
            Assert.That(bass.Length, Is.LessThan(8820 * 2 * 4), "much smaller than the source WAV");

            var manifest = LevelStems.ReadManifest(working)!;
            Assert.That(manifest, Is.Not.Null, "the level has no stems.json");
            Assert.That(manifest.Version, Is.EqualTo(1));
            Assert.That(manifest.Source, Is.EqualTo("demux"));
            Assert.That(manifest.Model, Does.Contain("grade"));
            Assert.That(manifest.SampleRate, Is.EqualTo(rate));
            Assert.That(manifest.Channels, Is.EqualTo(2));
            Assert.That(manifest.Frames, Is.EqualTo(8820));
            Assert.That(manifest.SourceSignature, Is.Not.Null.And.Not.Empty);

            Assert.That(manifest.Stems.Select(s => s.Kind), Is.EqualTo(new[] { "bass", "vocals" }));
            Assert.That(manifest.Stems.Select(s => s.File), Is.EqualTo(new[] { "bass.ogg", "vocals.ogg" }));
            Assert.That(manifest.Stems[0].Sha256, Is.EqualTo(sha256Of(bass)), "the manifest says what the level holds");
        });

        [Test]
        public void TestTheSameStemsAreNotAttachedTwice() => LevelTestBed.Run(async bed =>
        {
            string work = workFolder();
            var separated = demuxOutput(work, ("bass", 8820), ("vocals", 8820));

            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>());
            var working = bed.WorkingOf(set);

            Assert.That(await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null), Is.True);

            // The level holds a marker where the stem was: the same sources
            // again change nothing (the work is skipped).
            byte[] marker = "not really a stem"u8.ToArray();
            bed.Beatmaps.AddFile(set, new MemoryStream(marker), "stems/bass.ogg");
            Assert.That(await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null), Is.True);
            Assert.That(bed.ReadFile(working, "stems/bass.ogg"), Is.EqualTo(marker), "the same stems were encoded again");

            // The source changed (its mtime): the stems are attached again.
            File.SetLastWriteTimeUtc(separated[0].Path, DateTime.UtcNow.AddMinutes(1));
            Assert.That(await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null), Is.True);
            Assert.That(bed.ReadFile(working, "stems/bass.ogg")!.Take(4).ToArray(), Is.EqualTo("OggS"u8.ToArray()));
        });

        [Test]
        public void TestAMismatchedStemSetIsRefused() => LevelTestBed.Run(async bed =>
        {
            string work = workFolder();
            var separated = demuxOutput(work, ("bass", 8820), ("vocals", 4410));

            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>());
            var working = bed.WorkingOf(set);

            string? problem = null;
            Assert.That(await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null, m => problem = m), Is.False);
            Assert.That(problem, Does.Contain("same length").Or.Contain("one separation"));
            Assert.That(set.Files.Any(f => LevelStems.IsStemFile(f.Filename)), Is.False, "a refused set leaves no stems");
        });

        /// <summary>
        /// The stems the creator worked with (owner levels, 2026-11-16): a
        /// stem edited after the demux (split by range, repaired in time)
        /// sits outside the demux folder, may differ by a few samples, and
        /// travels under its track's name, marked as edited.
        /// </summary>
        [Test]
        public void TestEditedStemsTravelUnderTheirTrackNames() => LevelTestBed.Run(async bed =>
        {
            string work = workFolder();
            var separated = demuxOutput(work, ("bass", 8820));

            string edited = Path.Combine(work, "edit-3ea4646e.wav");
            writeStereoWav(edited, 8823);
            separated.Add(new ForgeTrack("In range ocarina", edited) { IsSeparated = true });

            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>());
            var working = bed.WorkingOf(set);

            string? problem = null;
            Assert.That(await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null, m => problem = m), Is.True, problem);

            var files = bed.FileNames(set.ID);
            Assert.That(files, Does.Contain("stems/bass.ogg"));
            Assert.That(files, Does.Contain("stems/in-range-ocarina.ogg"), "named by the track, not by the work file");

            var manifest = LevelStems.ReadManifest(bed.WorkingOf(bed.Realm.Run(r => r.Find<osu.Game.Beatmaps.BeatmapSetInfo>(set.ID)!.Detach())))!;
            Assert.That(manifest.Frames, Is.EqualTo(8823));
            Assert.That(manifest.Stems.Single(x => x.Kind == "bass").Edited, Is.False);
            Assert.That(manifest.Stems.Single(x => x.Kind == "in-range-ocarina").Edited, Is.True);
            Assert.That(manifest.Stems.Single(x => x.Kind == "in-range-ocarina").Frames, Is.EqualTo(8823));

            // Up to date afterwards: the same sources are not attached twice.
            Assert.That(LevelStems.IsUpToDate(bed.WorkingOf(bed.Realm.Run(r => r.Find<osu.Game.Beatmaps.BeatmapSetInfo>(set.ID)!.Detach())), separated), Is.True);
        });

        [Test]
        public void TestAMissingStemFileIsRefused() => LevelTestBed.Run(async bed =>
        {
            string work = workFolder();
            var separated = demuxOutput(work, ("bass", 8820), ("vocals", 8820));
            File.Delete(separated[1].Path);

            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>());
            var working = bed.WorkingOf(set);

            string? problem = null;
            Assert.That(await LevelStems.AttachAsync(separated, work, working, bed.Beatmaps, null, m => problem = m), Is.False);
            Assert.That(problem, Does.Contain("vocals.wav"));
        });

        [Test]
        public void TestStemFilesAreTheOnesUnderStems()
        {
            Assert.That(LevelStems.IsStemFile("stems/bass.ogg"), Is.True);
            Assert.That(LevelStems.IsStemFile("stems/stems.json"), Is.True);
            Assert.That(LevelStems.IsStemFile(@"stems\vocals.ogg"), Is.True);
            Assert.That(LevelStems.IsStemFile("bass.ogg"), Is.False);
            Assert.That(LevelStems.IsStemFile("stemsongs.ogg"), Is.False);
            Assert.That(LevelStems.IsStemFile("My Song - level audio.ogg"), Is.False);
        }

        // ------------------------------------------------------------------

        private static string workFolder()
        {
            string work = Path.Combine(Path.GetTempPath(), "kan-forge-tests", "stems-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(work);
            return work;
        }

        /// <summary>
        /// The demux output: its own folder of stem WAVs ("stems-…/roformer",
        /// as the demux writes them), one track per file. Each call is one
        /// separation.
        /// </summary>
        private static List<ForgeTrack> demuxOutput(string work, params (string Kind, int Frames)[] stems)
        {
            string folder = Path.Combine(work, "stems-" + Guid.NewGuid().ToString("N")[..8], "roformer");
            Directory.CreateDirectory(folder);

            var tracks = new List<ForgeTrack>();

            foreach (var (kind, frames) in stems)
            {
                string path = Path.Combine(folder, kind + ".wav");
                writeStereoWav(path, frames);
                tracks.Add(new ForgeTrack(kind, path) { IsSeparated = true });
            }

            return tracks;
        }

        private static void writeStereoWav(string path, int frames)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);

            writer.Write("RIFF"u8);
            writer.Write(36 + frames * 2 * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(rate);
            writer.Write(rate * 2 * 2);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(frames * 2 * 2);

            for (int i = 0; i < frames; i++)
            {
                writer.Write((short)(1000 * Math.Sin(2 * Math.PI * 220 * i / rate)));
                writer.Write((short)(1000 * Math.Sin(2 * Math.PI * 330 * i / rate)));
            }
        }

        private static string sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
