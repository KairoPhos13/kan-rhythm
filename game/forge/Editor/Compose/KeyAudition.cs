// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using Kan.Core.Media;
using Kan.Core.Score;
using Kan.Forge.Audio;

namespace Kan.Forge.Editor.Compose
{
    /// <summary>
    /// Sounds a pitch from the piano roll's keyboard, to try a note by ear
    /// before writing it (owner 2026-10-25: placing a note and playing it
    /// back was clumsy). The score's own tone (<see cref="ScoreSynth"/>), one
    /// sample per pitch made the first time it is played; game samples, so
    /// the effects volume governs it.
    /// </summary>
    internal partial class KeyAudition : Component
    {
        private const double note_ms = 600;
        private const double volume = 0.8;

        private readonly MemoryFiles tones = new MemoryFiles();
        private ISampleStore? samples;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            samples = audio.GetSampleStore(tones, audio.SampleMixer);
            samples.Volume.Value = volume;
        }

        /// <summary>Sound a pitch; whether its sample was there to play.</summary>
        public bool Play(int midi)
        {
            string name = $"kan-key-{midi}.wav";

            if (!tones.Contains(name))
            {
                float[] tone = ScoreSynth.Render(new[] { new ScoreNote { Midi = midi, StartMs = 0, DurationMs = note_ms, Intensity = 0.8f } });
                using var wav = new MemoryStream();
                WavFile.Write(wav, tone, tone.Length, 1, ScoreSynth.SAMPLE_RATE);
                tones.Add(name, wav.ToArray());
            }

            var sample = samples?.Get(name);
            sample?.Play();
            return sample != null;
        }

        protected override void Dispose(bool isDisposing)
        {
            samples?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}
