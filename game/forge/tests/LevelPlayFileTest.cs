// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ManagedBass;
using NUnit.Framework;
using Kan.Core.Tracks;
using Kan.Forge.Editor;
using Kan.Forge.Media;
using Kan.Forge.Pipeline;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The level's played files travel as Ogg Vorbis (issue 42): a render is
    /// encoded small, the game's audio library plays it back at its full
    /// length, and levels written as WAV are still recognised and written
    /// again as Ogg.
    /// </summary>
    [TestFixture]
    public class LevelPlayFileTest
    {
        private const int rate = 48000;

        [Test]
        public async Task TestARenderBecomesASmallOggTheGamePlays()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kan-forge-tests", "play-file-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            string wav = Path.Combine(dir, "level-audio.wav");
            int frames = rate * 3;
            writeStereoWav(wav, frames);

            string ogg = await BeatmapAudio.ToPlayFileAsync(wav);

            Assert.That(Path.GetExtension(ogg), Is.EqualTo(".ogg"));
            byte[] bytes = File.ReadAllBytes(ogg);
            Assert.That(bytes.Take(4).ToArray(), Is.EqualTo("OggS"u8.ToArray()));
            Assert.That(bytes.Length * 5, Is.LessThan(new FileInfo(wav).Length), "about a ninth of the WAV");

            // What the game plays it with: BASS, from the bytes (as the
            // level's voice is opened) and from the file.
            MediaAudioExtractor.EnsureBass();
            int handle = Bass.CreateStream(bytes, 0, bytes.Length, BassFlags.Decode | BassFlags.Float);
            Assert.That(handle, Is.Not.Zero, $"BASS cannot open the Ogg file: {Bass.LastError}");

            try
            {
                var info = Bass.ChannelGetInfo(handle);
                long decodedFrames = Bass.ChannelGetLength(handle) / (4 * info.Channels);
                Assert.That(info.Channels, Is.EqualTo(2), "the channels are kept");
                Assert.That(info.Frequency, Is.EqualTo(rate), "the sample rate is kept");
                Assert.That(decodedFrames, Is.EqualTo(frames), "every frame, no padding");
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        [Test]
        public void TestAMissingRenderIsAControlledError()
        {
            Assert.ThrowsAsync<ForgeException>(() => BeatmapAudio.ToPlayFileAsync("/tmp/definitely-not-here.wav"));
        }

        [Test]
        public void TestVoiceFilesOfBothFormatsAreFound()
        {
            Assert.That(BeatmapAudio.LEVEL_VOICE_NAME, Is.EqualTo("level voice.ogg"));
            Assert.That(BeatmapAudio.IsLevelVoice("No hay lugar mas alto - level voice.ogg"), Is.True);
            Assert.That(BeatmapAudio.IsLevelVoice("No hay lugar mas alto - level voice.wav"), Is.True, "levels from before issue 42");
            Assert.That(BeatmapAudio.IsLevelVoice("No hay lugar mas alto - level voice.mp3"), Is.False);
            Assert.That(BeatmapAudio.IsLevelVoice("No hay lugar mas alto - level audio.ogg"), Is.False);
        }

        [Test]
        public void TestALevelWrittenAsWavIsWrittenAgain()
        {
            var set = new ForgeTrackSet();
            set.Import("mix", "/tmp/mix.wav")!.IsCompanion = true;

            Assert.That(BeatmapAudio.Signature(set), Does.Contain("#F.ogg#"),
                "the format is part of what the level audio is made of: a WAV level renders again");
        }

        private static void writeStereoWav(string path, int frames)
        {
            using var f = File.Create(path);
            using var w = new BinaryWriter(f);
            int bytes = frames * 4;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + bytes);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((ushort)1);
            w.Write((ushort)2);
            w.Write(rate);
            w.Write(rate * 4);
            w.Write((ushort)4);
            w.Write((ushort)16);
            w.Write("data"u8.ToArray());
            w.Write(bytes);

            for (int i = 0; i < frames; i++)
            {
                // A chord on the left, a melody line on the right.
                double t = (double)i / rate;
                w.Write((short)(0.3 * short.MaxValue * (Math.Sin(2 * Math.PI * 220 * t) + Math.Sin(2 * Math.PI * 277.18 * t)) / 2));
                w.Write((short)(0.3 * short.MaxValue * Math.Sin(2 * Math.PI * (440 + 110 * Math.Floor(t * 2)) * t)));
            }
        }
    }
}
