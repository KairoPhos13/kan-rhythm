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
using osu.Game.Beatmaps;
using Kan.Core.Score;

namespace Kan.Forge.Play
{
    /// <summary>
    /// Autoplay is heard (owner 2026-10-15: "in auto mode neither the
    /// reference nor the synthesized notes are heard"): the level's
    /// <see cref="LevelPart"/> plays with the level audio, as the perfect
    /// player would sound. Pauses, seeks and rate mods follow the level audio.
    /// </summary>
    public partial class AutoplayPart : Component
    {
        private readonly Func<BeatmapSetInfo, KanScore?> loadScore;
        private readonly Func<string?> forgeRoot;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private LevelPart? part;

        public AutoplayPart(Func<BeatmapSetInfo, KanScore?> loadScore, Func<string?> forgeRoot)
        {
            this.loadScore = loadScore;
            this.forgeRoot = forgeRoot;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var set = working.Value.BeatmapSetInfo;
            if (set == null) return;

            string? root = forgeRoot();

            Task.Run(() => LevelPart.Find(set, root, loadScore), cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested) return;

                if (task.IsFaulted)
                {
                    Logger.Log($"kan!: the autoplay part could not be prepared: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                part = LevelPart.Open(audio, task.Result, $"kan-autoplay-{set.ID:N}");
                Logger.Log($"kan!: autoplay plays {(part?.Kind ?? LevelPart.Source.None).ToString().ToLowerInvariant()}");
            }), CancellationToken.None);
        }

        protected override void Update()
        {
            base.Update();

            if (part == null || !working.Value.TrackLoaded) return;

            var levelAudio = working.Value.Track;
            part.Follow(levelAudio.CurrentTime, levelAudio.IsRunning, levelAudio, 1);
        }

        protected override void Dispose(bool isDisposing)
        {
            cancel.Cancel();
            part?.Dispose();
            part = null;
            base.Dispose(isDisposing);
        }
    }
}
