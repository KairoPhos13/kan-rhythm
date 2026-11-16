// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;

namespace Kan.Forge.Play
{
    /// <summary>
    /// The level's voice (owner 2026-11-16): a companion that sings, kept in
    /// the level as its own file so a player may turn it off (the No voice
    /// mod: then this part is simply not added). It plays with the level
    /// audio, in step with it through pauses, seeks and rate mods. A level
    /// without a voice file plays nothing more.
    /// </summary>
    public partial class VoicePart : Component
    {
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private LevelPart? part;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var beatmap = working.Value;
            var set = beatmap.BeatmapSetInfo;
            if (set == null) return;

            Task.Run(() => LevelPart.FindVoice(beatmap), cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested) return;

                if (task.IsFaulted)
                {
                    Logger.Log($"kan!: the level's voice could not be read: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                if (task.Result == null) return;

                part = LevelPart.OpenVoice(audio, task.Result, $"kan-voice-{set.ID:N}");
                Logger.Log($"kan!: the level's voice {(part != null ? "plays" : "could not be opened")}");
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
            base.Dispose(isDisposing);
        }
    }
}
