// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using Kan.Forge.Score;

namespace Kan.Forge.Play
{
    /// <summary>
    /// The level's part in the song select preview (owner 2026-10-15: the
    /// companion alone is too abstract): the <see cref="LevelPart"/> of the
    /// selected level, in step with the preview. Lives on the game; heard
    /// only while song select (or the loading screen after it) shows a kan!
    /// level.
    /// </summary>
    public partial class LevelPreview : Component
    {
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
        private LevelPart? part;

        /// <summary>The level whose audio the music controller plays now.</summary>
        private WorkingBeatmap? playingLevel;

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

            if (part == null) return;

            var levelAudio = music.CurrentTrack;
            bool ours = playingLevel?.BeatmapSetInfo?.ID == set && music.TrackLoaded;

            // The level audio fades in and out on track changes, or is silenced (the
            // mic timing, 2026-10-17): so is its part — its whole volume, adjustments included.
            part.Follow(levelAudio.CurrentTime, ours && levelAudio.IsRunning && onPreviewScreen(), ours ? levelAudio : null, (float)levelAudio.AggregateVolume.Value);
        }

        private bool onPreviewScreen() => game.ScreenStack?.CurrentScreen is SongSelect or PlayerLoader;

        private void prepare(WorkingBeatmap beatmap, Guid set)
        {
            preparedFor = set;
            preparing?.Cancel();
            part?.Dispose();
            part = null;

            if (set == Guid.Empty) return;

            var cancel = preparing = new CancellationTokenSource();
            var setInfo = beatmap.BeatmapSetInfo;
            string? root = forgeRoot();

            Task.Run(() => LevelPart.Find(setInfo, root, loadScore), cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested || preparedFor != set) return;

                if (task.IsFaulted)
                {
                    Logger.Log($"kan!: the level preview could not be prepared: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                part = LevelPart.Open(audio, task.Result, $"kan-preview-{set:N}");
                Logger.Log($"kan!: level preview adds {(part?.Kind ?? LevelPart.Source.None).ToString().ToLowerInvariant()}");
            }), CancellationToken.None);
        }

        protected override void Dispose(bool isDisposing)
        {
            preparing?.Cancel();
            part?.Dispose();
            part = null;

            if (music != null)
                music.TrackChanged -= onTrackChanged;

            base.Dispose(isDisposing);
        }
    }
}
