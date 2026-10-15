// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Logging;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using Kan.Forge.Editor;
using Kan.Forge.Media;
using Kan.Forge.Score;
using Kan.Forge.Tracks;

namespace Kan.Forge.Play
{
    /// <summary>
    /// The level's part in the song select preview (owner 2026-10-15: the
    /// companion alone is too abstract). The level audio is the companion
    /// only, so the part the player will play is added on its own channel,
    /// in step with the preview: the reference performance when it is on
    /// this computer (the level's author has it; it sounds best), else the
    /// level's notes as plain tones (<see cref="ScoreSynth"/>).
    /// Lives on the game; heard only while song select (or the loading
    /// screen after it) shows a kan! level.
    /// </summary>
    public partial class LevelPreview : Component
    {
        public enum Source
        {
            None,
            Reference,
            Notes,
        }

        /// <summary>The notes are a guide, not the real part: a little under the companion.</summary>
        private const float notes_volume = 0.8f;

        private readonly Func<BeatmapSetInfo, KanScore?> loadScore;
        private readonly Func<string?> forgeRoot;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private MusicController music { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private OsuGame game { get; set; } = null!;

        private Guid preparedFor;
        private CancellationTokenSource? preparing;

        private SetMonitor.Channel? channel;
        private ForgeTrack? reference;

        /// <summary>Where the channel's audio starts on the level audio's timeline.</summary>
        private double channelOffsetMs;

        private DrawableTrack? adjustedBy;

        /// <summary>The level whose audio the music controller plays now.</summary>
        private WorkingBeatmap? playingLevel;

        /// <summary>What the preview adds for the selected level.</summary>
        public Source Playing { get; private set; }

        public LevelPreview(Func<BeatmapSetInfo, KanScore?> loadScore, Func<string?> forgeRoot)
        {
            this.loadScore = loadScore;
            this.forgeRoot = forgeRoot;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            playingLevel = working.Value;
            music.TrackChanged += onTrackChanged;
        }

        private void onTrackChanged(WorkingBeatmap level, TrackChangeDirection _) => playingLevel = level;

        protected override void Update()
        {
            base.Update();

            var beatmap = working.Value;
            bool kanLevel = beatmap.BeatmapInfo.Ruleset.ShortName == @"kan" && beatmap.BeatmapSetInfo != null;
            Guid set = kanLevel ? beatmap.BeatmapSetInfo!.ID : Guid.Empty;

            if (set != preparedFor)
                prepare(beatmap, set);

            if (channel == null) return;

            var t = channel.Track;
            if (!t.IsLoaded) return;

            DrawableTrack levelAudio = music.CurrentTrack;
            bool ours = playingLevel?.BeatmapSetInfo?.ID == set && music.TrackLoaded;
            followRate(ours ? levelAudio : null);

            // The level audio fades in and out on track changes: so does its part.
            float fade = (float)levelAudio.Volume.Value;

            bool running = ours && levelAudio.IsRunning && onPreviewScreen();
            double filePos = levelAudio.CurrentTime - channelOffsetMs;

            if (reference != null)
            {
                double heardAt = t.IsRunning ? t.CurrentTime : filePos;
                bool heard = reference.InClip(heardAt) && reference.Edits.IsAudibleAt(heardAt);
                t.Volume.Value = heard ? Math.Clamp(reference.Gain, 0, 1) * fade : 0;
                channel.ApplyBoost(reference.Gain);
                channel.ApplyFilters(reference.Edits);
            }
            else
                t.Volume.Value = notes_volume * fade;

            channel.Sync.ExpectedOffsetMs = 0;
            channel.Sync.Update(t, filePos, running, Time.Current);
            channel.Sync.TakeResyncs();
        }

        private bool onPreviewScreen() => game.ScreenStack?.CurrentScreen is SongSelect or PlayerLoader;

        /// <summary>Rate mods change the preview's speed (and Nightcore/Daycore its pitch): follow them.</summary>
        private void followRate(DrawableTrack? levelAudio)
        {
            if (ReferenceEquals(levelAudio, adjustedBy)) return;

            unfollowRate();

            if (levelAudio == null || channel == null) return;

            channel.Track.AddAdjustment(AdjustableProperty.Tempo, levelAudio.AggregateTempo);
            channel.Track.AddAdjustment(AdjustableProperty.Frequency, levelAudio.AggregateFrequency);
            adjustedBy = levelAudio;
        }

        private void unfollowRate()
        {
            if (adjustedBy != null && channel != null)
            {
                channel.Track.RemoveAdjustment(AdjustableProperty.Tempo, adjustedBy.AggregateTempo);
                channel.Track.RemoveAdjustment(AdjustableProperty.Frequency, adjustedBy.AggregateFrequency);
            }

            adjustedBy = null;
        }

        private void prepare(WorkingBeatmap beatmap, Guid set)
        {
            preparedFor = set;
            preparing?.Cancel();
            close();

            if (set == Guid.Empty) return;

            var cancel = preparing = new CancellationTokenSource();
            var setInfo = beatmap.BeatmapSetInfo;
            string? root = forgeRoot();

            Task.Run(() => find(setInfo, root), cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested || preparedFor != set) return;

                if (task.IsFaulted)
                {
                    Logger.Log($"kan!: the level preview could not be prepared: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                var found = task.Result;

                if (found.Reference != null)
                {
                    channel = SetMonitor.Channel.Open(audio, found.Reference);
                    reference = channel != null ? found.Reference : null;
                    channelOffsetMs = found.OffsetMs;
                }

                if (channel == null && found.NotesWav != null)
                {
                    channel = SetMonitor.Channel.Open(audio, $"kan-preview-{set:N}.wav", found.NotesWav);
                    channelOffsetMs = 0;
                }

                Playing = channel == null ? Source.None : reference != null ? Source.Reference : Source.Notes;
                Logger.Log($"kan!: level preview adds {Playing.ToString().ToLowerInvariant()}");
            }), CancellationToken.None);
        }

        private record Found(ForgeTrack? Reference, double OffsetMs, byte[]? NotesWav);

        /// <summary>The reference on this computer, else the notes rendered as a WAV in memory (off the update thread).</summary>
        private Found find(BeatmapSetInfo set, string? root)
        {
            if (root != null)
            {
                var doc = ForgeSetFile.Load(Path.Combine(root, set.ID.ToString("N"), ForgeSetFile.FILE_NAME));
                var reference = doc == null ? null : ForgeSetFile.ToTracks(doc).Select(t => t.Track).FirstOrDefault(t => t.IsReference);

                if (reference != null && File.Exists(reference.Path))
                    return new Found(reference, reference.OffsetMs - doc!.AudioStartMs, null);
            }

            var score = loadScore(set);
            if (score == null || score.Notes.Count == 0) return new Found(null, 0, null);

            float[] samples = ScoreSynth.Render(score.Notes);
            using var wav = new MemoryStream();
            MediaAudioExtractor.WriteWav(wav, samples, samples.Length, 1, ScoreSynth.SAMPLE_RATE);
            return new Found(null, 0, wav.ToArray());
        }

        private void close()
        {
            unfollowRate();
            channel?.Dispose();
            channel = null;
            reference = null;
            Playing = Source.None;
        }

        protected override void Dispose(bool isDisposing)
        {
            preparing?.Cancel();
            close();

            if (music != null)
                music.TrackChanged -= onTrackChanged;

            base.Dispose(isDisposing);
        }
    }
}
