// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using Kan.Forge.Editor;
using Kan.Forge.Audio;
using Kan.Core.Play;

namespace Kan.Forge.Play
{
    /// <summary>
    /// The player heard in their replay: the play's
    /// <see cref="PlayTake"/> plays with the level audio, each moment where
    /// the level was when it was played. It runs at the player's own pace
    /// (rate mods change the level, not what they played); where nothing
    /// was played (a pause, a wait) it is silent.
    /// </summary>
    public partial class TakePart : Component
    {
        private readonly string forgeRoot;
        private readonly int id;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly Stopwatch clock = Stopwatch.StartNew();

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private AudioChannel? channel;
        private PlayTake.Info? info;

        public TakePart(string forgeRoot, int id)
        {
            this.forgeRoot = forgeRoot;
            this.id = id;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Task.Run(() => PlayTake.Find(forgeRoot, id) is { } found ? (found.Info, File.ReadAllBytes(found.WavPath)) : default, cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested) return;

                if (task.IsFaulted || task.Result.Info == null)
                {
                    Logger.Log($"kan!: the replay's recording ({id}) is not kept{(task.IsFaulted ? $": {task.Exception?.GetBaseException().Message}" : string.Empty)}");
                    return;
                }

                info = task.Result.Info;
                channel = AudioChannel.Open(audio, $"kan-take-{id}.wav", task.Result.Item2);
                Logger.Log($"kan!: the replay plays the player's recording ({id})");
            }), CancellationToken.None);
        }

        protected override void Update()
        {
            base.Update();

            if (channel == null || info == null || !working.Value.TrackLoaded) return;

            var levelAudio = working.Value.Track;
            var t = channel.Track;
            if (!t.IsLoaded) return;

            double? at = PlayTake.TakeAt(info.Points, levelAudio.CurrentTime);
            t.Volume.Value = at == null ? 0 : 1;
            channel.Sync.Update(t, at ?? t.CurrentTime, levelAudio.IsRunning && at != null, clock.Elapsed.TotalMilliseconds);
            channel.Sync.TakeResyncs();
        }

        protected override void Dispose(bool isDisposing)
        {
            cancel.Cancel();
            channel?.Dispose();
            channel = null;
            base.Dispose(isDisposing);
        }
    }
}
