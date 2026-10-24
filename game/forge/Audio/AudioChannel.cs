// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass.Fx;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.IO.Stores;
using osu.Game.Audio.Effects;
using Kan.Forge.Tracks;
using Kan.Core.Tracks;

namespace Kan.Forge.Audio
{
    /// <summary>
    /// One track played along the level — a set's track in the editor, the
    /// reference or the score's notes in play, the player's take in a replay:
    /// its own mixer (room for per-track filters and gain) and playback channel,
    /// in the game's music volume (its track store hangs from the global one).
    /// </summary>
    public sealed class AudioChannel : IDisposable
    {
        public readonly AudioMixer Mixer;
        public readonly ITrackStore Store;
        public readonly Track Track;

        /// <summary>Keeps this channel on the timeline without audible corrections.</summary>
        public readonly ChannelSync Sync = new ChannelSync();

        private readonly AudioFilter lowCut;
        private readonly AudioFilter highCut;

        // BASS_FX compressor at ratio 1 = no compression, just its output
        // gain: a clean amplifier for gains above 0 dB.
        private readonly CompressorParameters boost = new CompressorParameters
        {
            fThreshold = 0,
            fRatio = 1,
            fAttack = 1,
            fRelease = 100,
            fGain = 0,
            lChannel = ManagedBass.FXChannelFlags.All,
        };

        private bool boostAttached;
        private float boostDb;

        // Pitch without a speed change: the frequency moves the pitch (and
        // the speed), the tempo puts the speed back — their product, the
        // track's rate, stays 1, so the channel keeps the timeline.
        private readonly BindableDouble pitchFrequency = new BindableDouble(1);
        private readonly BindableDouble pitchTempo = new BindableDouble(1);

        private AudioChannel(AudioMixer mixer, ITrackStore store, Track track)
        {
            Mixer = mixer;
            Store = store;
            Track = track;

            lowCut = new AudioFilter(mixer, BQFType.HighPass);
            highCut = new AudioFilter(mixer, BQFType.LowPass);

            track.AddAdjustment(AdjustableProperty.Frequency, pitchFrequency);
            track.AddAdjustment(AdjustableProperty.Tempo, pitchTempo);
        }

        /// <summary>Amplify above 0 dB (the track volume covers 0..1).</summary>
        public void ApplyBoost(float gain)
        {
            float db = gain > 1 ? 20 * MathF.Log10(gain) : 0;

            if (db <= 0.01f)
            {
                if (boostAttached)
                {
                    Mixer.RemoveEffect(boost);
                    boostAttached = false;
                }

                return;
            }

            if (!boostAttached)
            {
                boost.fGain = db;
                Mixer.AddEffect(boost);
                boostAttached = true;
                boostDb = db;
            }
            else if (Math.Abs(db - boostDb) > 0.01f)
            {
                boost.fGain = db;
                Mixer.UpdateEffect(boost);
                boostDb = db;
            }
        }

        /// <summary>Follow the track's pending filters (same biquads the save renders with) and pitch shift.</summary>
        public void ApplyFilters(TrackEdits edits)
        {
            lowCut.Cutoff = edits.LowCutHz > 0 ? (int)edits.LowCutHz : 1;
            highCut.Cutoff = edits.HighCutHz > 0 ? (int)Math.Min(edits.HighCutHz, AudioFilter.MAX_LOWPASS_CUTOFF) : AudioFilter.MAX_LOWPASS_CUTOFF;

            double ratio = Math.Pow(2, edits.PitchSemitones / 12.0);
            pitchFrequency.Value = ratio;
            pitchTempo.Value = 1 / ratio;
        }

        public static AudioChannel? Open(AudioManager audio, ForgeTrack track)
        {
            if (!File.Exists(track.Path)) return null;

            return open(audio, track.Name, new AbsoluteFileStore(), track.Path);
        }

        /// <summary>A channel playing audio held in memory (a rendered WAV).</summary>
        public static AudioChannel? Open(AudioManager audio, string name, byte[] audioFile)
            => open(audio, name, new MemoryFileStore(name, audioFile), name);

        private static AudioChannel? open(AudioManager audio, string name, IResourceStore<byte[]> files, string path)
        {
            var mixer = audio.CreateAudioMixer($"kan-forge:{name}");
            var store = audio.GetTrackStore(files, mixer);
            var t = store.Get(path);

            if (t == null)
            {
                store.Dispose();
                mixer.Dispose();
                return null;
            }

            return new AudioChannel(mixer, store, t);
        }

        public void Dispose()
        {
            if (boostAttached)
                Mixer.RemoveEffect(boost);

            lowCut.Dispose();
            highCut.Dispose();
            Track.Stop();
            Track.Dispose();
            Store.Dispose();
            Mixer.Dispose();
        }
    }

    /// <summary>Resource store over absolute file paths (the set's tracks live outside the beatmap store).</summary>
    internal sealed class AbsoluteFileStore : IResourceStore<byte[]>
    {
        public byte[] Get(string name) => File.Exists(name) ? File.ReadAllBytes(name) : null!;

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(Get(name));

        public Stream GetStream(string name) => File.Exists(name) ? File.OpenRead(name) : null!;

        public IEnumerable<string> GetAvailableResources() => Array.Empty<string>();

        public void Dispose()
        {
        }
    }

    /// <summary>Resource store over one file held in memory.</summary>
    internal sealed class MemoryFileStore : IResourceStore<byte[]>
    {
        private readonly string name;
        private readonly byte[] data;

        public MemoryFileStore(string name, byte[] data)
        {
            this.name = name;
            this.data = data;
        }

        public byte[] Get(string n) => n == name ? data : null!;

        public Task<byte[]> GetAsync(string n, CancellationToken cancellationToken = default) => Task.FromResult(Get(n));

        public Stream GetStream(string n) => n == name ? new MemoryStream(data, false) : null!;

        public IEnumerable<string> GetAvailableResources() => new[] { name };

        public void Dispose()
        {
        }
    }
}
