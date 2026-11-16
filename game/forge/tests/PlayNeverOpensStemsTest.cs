// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks;
using osu.Game.Rulesets.Edit.Checks.Components;
using Kan.Forge.Editor;
using Kan.Forge.Media;
using Kan.Forge.Play;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// Play never opens the level's stems (issue 42): the voice lookup, the
    /// level audio naming and the verify tab's file checks must all pass
    /// over what lives under "stems/", while the same files outside it keep
    /// being read exactly as before.
    /// </summary>
    [TestFixture]
    public class PlayNeverOpensStemsTest
    {
        [Test]
        public void TestAStemIsNeverTheLevelAudioOrItsVoice()
        {
            Assert.That(BeatmapAudio.IsLevelVoice("stems/level voice.ogg"), Is.False);
            Assert.That(BeatmapAudio.IsLevelVoice("stems/vocals.ogg"), Is.False);
            Assert.That(BeatmapAudio.IsLevelAudio("stems/level audio.ogg"), Is.False);
            Assert.That(BeatmapAudio.IsLevelAudio("stems/bass.ogg"), Is.False);

            Assert.That(BeatmapAudio.IsLevelVoice("My Song - level voice.ogg"), Is.True);
            Assert.That(BeatmapAudio.IsLevelVoice("My Song - level voice.wav"), Is.True);
            Assert.That(BeatmapAudio.IsLevelAudio("My Song - level audio.ogg"), Is.True);
            Assert.That(BeatmapAudio.IsLevelAudio("My Song - level audio.wav"), Is.True);
        }

        [Test]
        public void TestTheVoiceLookupOpensNoStem() => LevelTestBed.Run(bed =>
        {
            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>
            {
                // A stem named like a voice: play must not take it for one.
                ["stems/level voice.ogg"] = "the wrong voice"u8.ToArray(),
                ["stems/vocals.ogg"] = "a stem"u8.ToArray(),
            });

            var working = bed.WorkingOf(set);
            Assert.That(LevelPart.FindVoice(working), Is.Null, "the voice lookup opened a stem");

            // The real voice, next to the stems, is found as before.
            byte[] voice = "the real voice"u8.ToArray();
            bed.Beatmaps.AddFile(set, new MemoryStream(voice), "My Song - level voice.ogg");
            Assert.That(LevelPart.FindVoice(working), Is.EqualTo(voice));

            return System.Threading.Tasks.Task.CompletedTask;
        });

        [Test]
        public void TestTheVerifyChecksOpenNoStem() => LevelTestBed.Run(bed =>
        {
            var set = bed.CreateLevel("My Song", new Dictionary<string, byte[]>
            {
                // Stems that would each be flagged as samples or hitsounds.
                ["stems/tiny.ogg"] = shortWav(),
                ["stems/broken.ogg"] = brokenAudio(),
                ["stems/empty.ogg"] = Array.Empty<byte>(),

                // The same problems at the top level: the checks still run.
                ["canary-short.wav"] = shortWav(),
                ["canary-broken.ogg"] = brokenAudio(),
                ["canary-empty.bin"] = Array.Empty<byte>(),
            });

            var working = bed.WorkingOf(set);
            var context = new BeatmapVerifierContext(working.Beatmap, working);

            MediaAudioExtractor.EnsureBass();

            var issues = new ICheck[]
                {
                    new CheckTooShortAudioFiles(),
                    new CheckHitsoundsFormat(),
                    new CheckZeroByteFiles(),
                    new CheckDelayedHitsounds(),
                }
                .SelectMany(c => c.Run(context))
                .ToList();

            Assert.That(fileNames(issues), Has.None.StartsWith("stems/"), "a verify check opened a stem");

            // The checks are alive: the same problems outside stems/ count.
            Assert.That(fileNames(issues), Has.Some.EqualTo("canary-short.wav"), "the too short check no longer runs");
            Assert.That(fileNames(issues), Has.Some.EqualTo("canary-broken.ogg"), "the hitsound format check no longer runs");
            Assert.That(fileNames(issues), Has.Some.EqualTo("canary-empty.bin"), "the zero byte check no longer runs");

            return System.Threading.Tasks.Task.CompletedTask;
        });

        // ------------------------------------------------------------------

        /// <summary>The file names the issues talk about.</summary>
        private static IEnumerable<string> fileNames(IEnumerable<Issue> issues)
            => issues.SelectMany(i => i.Arguments).OfType<string>();

        /// <summary>10 ms of WAV: too short for the sample check.</summary>
        private static byte[] shortWav()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write("RIFF"u8);
            writer.Write(36 + 480 * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(48000);
            writer.Write(48000 * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(480 * 2);

            for (int i = 0; i < 480; i++)
                writer.Write((short)1000);

            return stream.ToArray();
        }

        /// <summary>Bytes no decoder accepts.</summary>
        private static byte[] brokenAudio()
        {
            var bytes = new byte[300];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(i * 31 + 7);
            return bytes;
        }
    }
}
