// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Linq;
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
using Kan.Core.Score;

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

        [Resolved]
        private IBindable<System.Collections.Generic.IReadOnlyList<osu.Game.Rulesets.Mods.Mod>> mods { get; set; } = null!;

        private Guid preparedFor;
        private CancellationTokenSource? preparing;
        private LevelPart? part;

        /// <summary>The level's voice (a companion that sings, its own file): heard with the preview unless No voice is selected.</summary>
        private LevelPart? voice;

        /// <summary>The No voice mod's acronym (the mod lives in the ruleset, which this cannot see).</summary>
        private const string no_voice_acronym = @"NV";

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

            if (part == null && voice == null) return;

            var levelAudio = music.CurrentTrack;
            bool ours = playingLevel?.BeatmapSetInfo?.ID == set && music.TrackLoaded;

            // Away from song select (a play, the editor, results) the part is
            // silent: stop it once and leave it be. Following it there kept it
            // cued every frame, a seek the update thread waits on the audio
            // thread for, the main cause of stutter in play on two cores
            // (issue 40). Back on song select it is placed again as it starts.
            if (!onPreviewScreen())
            {
                if (!parked)
                {
                    part?.Follow(levelAudio.CurrentTime, false, null, 0);
                    voice?.Follow(levelAudio.CurrentTime, false, null, 0);
                    parked = true;
                }

                return;
            }

            parked = false;

            // The level audio fades in and out on track changes, or is silenced (the
            // mic timing, 2026-10-17): so is its part (its whole volume, adjustments included).
            part?.Follow(levelAudio.CurrentTime, ours && levelAudio.IsRunning, ours ? levelAudio : null, (float)levelAudio.AggregateVolume.Value);

            bool voiceOff = mods.Value.Any(m => m.Acronym == no_voice_acronym);
            voice?.Follow(levelAudio.CurrentTime, ours && levelAudio.IsRunning, ours ? levelAudio : null, voiceOff ? 0 : (float)levelAudio.AggregateVolume.Value);
        }

        /// <summary>The part was stopped on leaving song select and is not followed until it is back.</summary>
        private bool parked;

        private bool onPreviewScreen() => game.ScreenStack?.CurrentScreen is SongSelect or PlayerLoader;

        private void prepare(WorkingBeatmap beatmap, Guid set)
        {
            preparedFor = set;
            preparing?.Cancel();
            part?.Dispose();
            part = null;
            voice?.Dispose();
            voice = null;
            parked = false;

            if (set == Guid.Empty) return;

            var cancel = preparing = new CancellationTokenSource();
            var setInfo = beatmap.BeatmapSetInfo;
            string? root = forgeRoot();

            Task.Run(() => (Part: LevelPart.Find(setInfo, root, loadScore), Voice: LevelPart.FindVoice(beatmap)), cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested || preparedFor != set) return;

                if (task.IsFaulted)
                {
                    Logger.Log($"kan!: the level preview could not be prepared: {task.Exception?.GetBaseException().Message}");
                    return;
                }

                part = LevelPart.Open(audio, task.Result.Part, $"kan-preview-{set:N}");
                voice = task.Result.Voice is { } voiceWav ? LevelPart.OpenVoice(audio, voiceWav, $"kan-preview-voice-{set:N}") : null;
                Logger.Log($"kan!: level preview adds {(part?.Kind ?? LevelPart.Source.None).ToString().ToLowerInvariant()}{(voice != null ? " and the voice" : string.Empty)}");
            }), CancellationToken.None);
        }

        protected override void Dispose(bool isDisposing)
        {
            preparing?.Cancel();
            part?.Dispose();
            part = null;
            voice?.Dispose();
            voice = null;

            if (music != null)
                music.TrackChanged -= onTrackChanged;

            base.Dispose(isDisposing);
        }
    }
}
