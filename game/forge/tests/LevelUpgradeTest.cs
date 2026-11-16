// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Kan.Core.Tracks;
using Kan.Forge.Editor;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The upgrade of the local levels (issue 42 step 2): the WAV level
    /// audio and voice of kan!forge become Ogg (the same samples in, Ogg
    /// out), a demuxed level carries its stems, a level with an imported
    /// song is left alone, and a second run changes nothing.
    /// </summary>
    [TestFixture]
    public class LevelUpgradeTest
    {
        [Test]
        public void TestWavFilesBecomeOggAndTheStemsAreAttached() => LevelTestBed.Run(async bed =>
        {
            // A level of kan!forge: WAV renders, and a demux in the work
            // folder (stems-…/roformer, as the demux writes them).
            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>
            {
                ["My Song - level audio.wav"] = wav(),
                ["My Song - level voice.wav"] = wav(),
            }, audioFile: "My Song - level audio.wav");

            var working = bed.WorkingOf(set);
            string workDir = Path.Combine(bed.ForgeRoot, LevelSession.KeyOf(working));
            string roformer = Path.Combine(workDir, "stems-1", "roformer");
            Directory.CreateDirectory(roformer);

            string bass = Path.Combine(roformer, "bass.wav");
            File.WriteAllBytes(bass, wav());

            var doc = new ForgeSetFile.Document();
            doc.Tracks.Add(new ForgeSetFile.TrackEntry { Name = "bass", Path = bass, IsSeparated = true });
            ForgeSetFile.Save(doc, Path.Combine(workDir, ForgeSetFile.FILE_NAME));

            var report = await LevelUpgrade.UpgradeAllAsync(bed.Beatmaps, bed.Realm, bed.ForgeRoot);
            string words = string.Join("; ", report.Levels.SelectMany(l => l.Lines()));

            Assert.That(report.Failed, Is.Zero, words);
            Assert.That(report.Converted, Is.EqualTo(1), words);
            Assert.That(report.StemsAttached, Is.EqualTo(1), words);

            Assert.That(bed.FileNames(set.ID), Does.Contain("My Song - level audio.ogg"));
            Assert.That(bed.FileNames(set.ID), Does.Contain("My Song - level voice.ogg"));
            Assert.That(bed.FileNames(set.ID), Does.Contain("stems/bass.ogg"));
            Assert.That(bed.FileNames(set.ID), Does.Contain(LevelStems.MANIFEST));
            Assert.That(bed.FileNames(set.ID), Has.None.EndsWith(".wav"), "the WAV renders are replaced");
            Assert.That(bed.AudioFileOf(set.ID), Is.EqualTo("My Song - level audio.ogg"));

            // The same audio, only rewrapped: the file is a real Ogg now.
            Assert.That(bed.ReadFile(set.ID, "My Song - level audio.ogg")!.Take(4).ToArray(), Is.EqualTo("OggS"u8.ToArray()));

            // The set keeps the level audio's signature (of the current
            // format): the editor does not render the level audio again.
            var stored = ForgeSetFile.Load(Path.Combine(workDir, ForgeSetFile.FILE_NAME));
            Assert.That(stored!.LevelAudioSignature, Is.Not.Null.And.Contain("#F.ogg#"));

            // Idempotent: a second run changes nothing and says so.
            var before = bed.FileNames(set.ID).OrderBy(n => n, StringComparer.Ordinal).ToList();

            var again = await LevelUpgrade.UpgradeAllAsync(bed.Beatmaps, bed.Realm, bed.ForgeRoot);

            Assert.That(again.Converted, Is.Zero);
            Assert.That(again.StemsAttached, Is.Zero);
            Assert.That(again.Skipped, Is.EqualTo(1));
            Assert.That(bed.FileNames(set.ID).OrderBy(n => n, StringComparer.Ordinal), Is.EqualTo(before));
        });

        [Test]
        public void TestALevelWithAnImportedSongIsLeftAlone() => LevelTestBed.Run(async bed =>
        {
            var set = bed.CreateLevel("Imported", new Dictionary<string, byte[]>
            {
                ["song.mp3"] = "an imported song"u8.ToArray(),
            }, audioFile: "song.mp3");

            var before = bed.FileNames(set.ID).OrderBy(n => n, StringComparer.Ordinal).ToList();

            var report = await LevelUpgrade.UpgradeAllAsync(bed.Beatmaps, bed.Realm, bed.ForgeRoot);

            Assert.That(report.Failed, Is.Zero, string.Join("; ", report.Levels.SelectMany(l => l.Failed)));
            Assert.That(report.Converted, Is.Zero);
            Assert.That(report.Levels.Single().Skipped, Has.One.Contains("not a kan render"));
            Assert.That(bed.FileNames(set.ID).OrderBy(n => n, StringComparer.Ordinal), Is.EqualTo(before));
        });

        // ------------------------------------------------------------------

        /// <summary>200 frames of PCM16 stereo: the demux output's shape.</summary>
        private static byte[] wav()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write("RIFF"u8);
            writer.Write(36 + 200 * 2 * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(44100);
            writer.Write(44100 * 2 * 2);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(200 * 2 * 2);

            for (int i = 0; i < 200; i++)
            {
                writer.Write((short)(1000 * Math.Sin(2 * Math.PI * 220 * i / 44100)));
                writer.Write((short)(1000 * Math.Sin(2 * Math.PI * 330 * i / 44100)));
            }

            return stream.ToArray();
        }
    }
}
