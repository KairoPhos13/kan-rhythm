// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Game.Beatmaps;
using Kan.Forge.Editor;
using Kan.Forge.Media;
using Kan.Core.Score;
using Kan.Forge.Tracks;
using Kan.Forge.Audio;
using Kan.Core.Tracks;
using Kan.Core.Media;

namespace Kan.Forge.Play
{
    /// <summary>
    /// The part the player plays, as sound, kept in step with the level audio
    /// (the companion): the reference take when it is on this computer (the
    /// level's author has it; it sounds best), else the level's notes as
    /// plain tones (<see cref="ScoreSynth"/>). Heard in the song select
    /// preview and in autoplay.
    /// </summary>
    public sealed class LevelPart : IDisposable
    {
        public enum Source
        {
            None,
            Reference,
            Notes,
        }

        /// <summary>What a part is made of; found off the update thread (<see cref="Find"/>).</summary>
        public sealed record Material(ForgeTrack? Reference, double OffsetMs, byte[]? NotesWav);

        /// <summary>The notes are a guide, not the real part: a little under the companion.</summary>
        private const float notes_volume = 0.8f;

        private readonly AudioChannel channel;
        private readonly ForgeTrack? reference;

        /// <summary>Where the channel's audio starts on the level audio's timeline.</summary>
        private readonly double offsetMs;

        /// <summary>A steady clock for the channel's settling and rate limiting.</summary>
        private readonly Stopwatch clock = Stopwatch.StartNew();

        private IAdjustableAudioComponent? rateSource;

        public Source Kind => reference != null ? Source.Reference : Source.Notes;

        private LevelPart(AudioChannel channel, ForgeTrack? reference, double offsetMs)
        {
            this.channel = channel;
            this.reference = reference;
            this.offsetMs = offsetMs;

            // Both channels report positions the same way: 0 apart is in step.
            channel.Sync.ExpectedOffsetMs = 0;
        }

        /// <summary>The reference on this computer, else the notes rendered as a WAV in memory. Slow: call it off the update thread.</summary>
        public static Material Find(BeatmapSetInfo set, string? forgeRoot, Func<BeatmapSetInfo, KanScore?> loadScore)
        {
            if (forgeRoot != null)
            {
                var doc = ForgeSetFile.Load(Path.Combine(forgeRoot, set.ID.ToString("N"), ForgeSetFile.FILE_NAME));
                var reference = doc == null ? null : ForgeSetFile.ToTracks(doc).Select(t => t.Track).FirstOrDefault(t => t.IsReference);

                if (reference != null && File.Exists(reference.Path))
                    return new Material(reference, reference.OffsetMs - doc!.AudioStartMs, null);
            }

            var score = loadScore(set);
            if (score == null || score.Notes.Count == 0) return new Material(null, 0, null);

            float[] samples = ScoreSynth.Render(score.Notes);
            using var wav = new MemoryStream();
            WavFile.Write(wav, samples, samples.Length, 1, ScoreSynth.SAMPLE_RATE);
            return new Material(null, 0, wav.ToArray());
        }

        /// <summary>A channel for the material, or null when there is nothing to play.</summary>
        public static LevelPart? Open(AudioManager audio, Material material, string name)
        {
            if (material.Reference != null && AudioChannel.Open(audio, material.Reference) is { } referenceChannel)
                return new LevelPart(referenceChannel, material.Reference, material.OffsetMs);

            if (material.NotesWav != null && AudioChannel.Open(audio, $"{name}.wav", material.NotesWav) is { } notesChannel)
                return new LevelPart(notesChannel, null, 0);

            return null;
        }

        /// <summary>
        /// Keep in step with the level audio: <paramref name="levelAudioMs"/>
        /// is its position, <paramref name="rate"/> what sets its speed (rate
        /// mods), <paramref name="volume"/> a fade (0..1). Call every frame.
        /// </summary>
        public void Follow(double levelAudioMs, bool play, IAdjustableAudioComponent? rate, float volume)
        {
            var t = channel.Track;
            if (!t.IsLoaded) return;

            followRate(rate);

            double filePos = levelAudioMs - offsetMs;

            if (reference != null)
            {
                double heardAt = t.IsRunning ? t.CurrentTime : filePos;
                bool heard = reference.InClip(heardAt) && reference.Edits.IsAudibleAt(heardAt);
                t.Volume.Value = heard ? Math.Clamp(reference.Gain, 0, 1) * volume : 0;
                channel.ApplyBoost(reference.Gain);
                channel.ApplyFilters(reference.Edits);
            }
            else
                t.Volume.Value = notes_volume * volume;

            channel.Sync.Update(t, filePos, play, clock.Elapsed.TotalMilliseconds);
            channel.Sync.TakeResyncs();
        }

        /// <summary>Rate mods change the level's speed: follow them.</summary>
        private void followRate(IAdjustableAudioComponent? source)
        {
            if (ReferenceEquals(source, rateSource)) return;

            unfollowRate();

            if (source == null) return;

            channel.Track.AddAdjustment(AdjustableProperty.Tempo, source.AggregateTempo);
            channel.Track.AddAdjustment(AdjustableProperty.Frequency, source.AggregateFrequency);
            rateSource = source;
        }

        private void unfollowRate()
        {
            if (rateSource != null)
            {
                channel.Track.RemoveAdjustment(AdjustableProperty.Tempo, rateSource.AggregateTempo);
                channel.Track.RemoveAdjustment(AdjustableProperty.Frequency, rateSource.AggregateFrequency);
            }

            rateSource = null;
        }

        public void Dispose()
        {
            unfollowRate();
            channel.Dispose();
        }
    }
}
