// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using Kan.Forge.Media;
using Kan.Forge.Play;
using Kan.Forge.Score;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// A perfect hit is heard (owner 2026-10-15: "short, never out of tune —
    /// it says you're doing great"): a small bell at the note just played, an
    /// octave up (<see cref="ChimeSynth"/>). One sample per pitch of the
    /// score, made when the level loads; played as game samples, so the
    /// effects volume governs it.
    /// </summary>
    public partial class KanPerfectChime : Component
    {
        private const double volume = 0.35;

        private readonly KanPlayState state;
        private readonly Dictionary<int, byte[]> wavs = new Dictionary<int, byte[]>();
        private ISampleStore? samples;

        public KanPerfectChime(KanPlayState state)
        {
            this.state = state;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            foreach (int midi in state.Notes.Where(n => !n.PitchUnknown).Select(n => ChimeSynth.PitchFor(n.Midi)).Distinct())
            {
                float[] chime = ChimeSynth.Render(midi);
                using var wav = new MemoryStream();
                MediaAudioExtractor.WriteWav(wav, chime, chime.Length, 1, ChimeSynth.SAMPLE_RATE);
                wavs[midi] = wav.ToArray();
            }

            samples = audio.GetSampleStore(new ChimeStore(wavs), audio.SampleMixer);
            samples.Volume.Value = volume;
            state.Judged += onJudged;
        }

        private void onJudged(int index, LiveVerdict verdict)
        {
            if (verdict.Result != LiveResult.Perfect) return;

            var note = state.Notes[index];
            if (note.PitchUnknown) return;

            var sample = samples?.Get(name(ChimeSynth.PitchFor(note.Midi)));
            sample?.Play();

            if (!logged)
            {
                logged = true;
                osu.Framework.Logging.Logger.Log($"kan!: perfect chime {(sample != null ? "plays" : "missing")} ({KanStaff.NoteName(ChimeSynth.PitchFor(note.Midi))})");
            }
        }

        private bool logged;

        private static string name(int midi) => $"kan-chime-{midi}.wav";

        protected override void Dispose(bool isDisposing)
        {
            state.Judged -= onJudged;
            samples?.Dispose();
            base.Dispose(isDisposing);
        }

        /// <summary>The chimes, held in memory.</summary>
        private sealed class ChimeStore : IResourceStore<byte[]>
        {
            private readonly Dictionary<int, byte[]> wavs;

            public ChimeStore(Dictionary<int, byte[]> wavs) => this.wavs = wavs;

            private byte[]? find(string name)
                => name.StartsWith(@"kan-chime-", StringComparison.Ordinal) && int.TryParse(name[10..^4], out int midi) && wavs.TryGetValue(midi, out var wav) ? wav : null;

            public byte[] Get(string name) => find(name)!;

            public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

            public Stream GetStream(string name) => find(name) is { } wav ? new MemoryStream(wav, false) : null!;

            public IEnumerable<string> GetAvailableResources() => wavs.Keys.Select(name);

            public void Dispose()
            {
            }
        }
    }
}
