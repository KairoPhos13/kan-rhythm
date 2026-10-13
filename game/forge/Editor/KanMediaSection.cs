// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Edit.Setup;
using osu.Game.Utils;
using Kan.Forge.Media;
using Kan.Forge.Tracks;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor's **Resources** section (stock osu! rows untouched) with the
    /// kan! import surface merged in (owner rules 2026-10-13):
    ///
    /// - **Resources is the ONLY place that reads files** — audio tracks
    ///   (multiple, via the navigator), and the stock rows above for video
    ///   and images. Nothing else anywhere asks for a path.
    /// - With "video provides the audio" on, the video's audio joins the
    ///   track list automatically (as an accompaniment).
    /// - Everything downstream (roles, track editing, demux/transcribe/render
    ///   actions) happens on the **tracks** tab and only *selects* from the
    ///   resources imported or created here.
    /// </summary>
    public partial class KanMediaSection : ResourcesSection
    {
        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private FormCheckBox videoProvidesAudio = null!;
        private FormFileSelector trackSelector = null!;
        private OsuSpriteText statusText = null!;

        private readonly ForgeTrackSet trackSet = ForgeTrackSession.Tracks;
        private readonly HashSet<string> extractedVideos = new();

        private static bool devToolsEnabled
            => DebugUtils.IsDebugBuild
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(@"KAN_DEV_TOOLS"));

        [BackgroundDependencyLoader]
        private void loadKan()
        {
            videoProvidesAudio = new FormCheckBox
            {
                Caption = @"Video provides the audio",
                HintText = @"When the clip's audio lives inside the video, it joins the track list. Turn off to use the video for visuals only.",
                Current = new BindableBool(true),
                Margin = new MarginPadding { Top = 12 },
            };
            trackSelector = new FormFileSelector(SupportedExtensions.AUDIO_EXTENSIONS)
            {
                Caption = @"Import audio tracks (multiple)",
                PlaceholderText = @"reference take, accompaniments, own stems… — pick repeatedly to add more",
            };
            statusText = new OsuSpriteText
            {
                Font = OsuFont.GetFont(size: 11),
                Text = @"Arrange, edit and use the tracks in the 'tracks' tab.",
            };

            var added = new List<Drawable>
            {
                new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                    Text = @"Import resources (the only place that reads files)",
                    Margin = new MarginPadding { Top = 12 },
                },
                new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 10),
                    Text = @"audio tracks below (multiple) · video and images use the rows above",
                },
                trackSelector,
                videoProvidesAudio,
                statusText,
            };

            if (devToolsEnabled)
            {
                added.Add(new FormTextBox
                {
                    Caption = @"Source audio URL (developer tooling)",
                    PlaceholderText = @"https://… — fetched via yt-dlp; not part of the official flow",
                });
            }

            AddRange(added);

            // The navigator applies immediately (like the stock rows) and
            // clears itself so more tracks can be picked right away.
            trackSelector.Current.BindValueChanged(change =>
            {
                if (change.NewValue == null) return;
                addTrack(change.NewValue);
                trackSelector.Current.Value = null;
            });
        }

        // ------------------------------------------------------------------
        // Video -> track list (merge)
        // ------------------------------------------------------------------

        protected override void Update()
        {
            base.Update();
            maybeExtractVideoAudio();
        }

        /// <summary>
        /// When the beatmap gains a video and "video provides the audio" is
        /// on, its audio joins the track list (as an accompaniment — the user
        /// can re-role it). Runs at most every 30 frames.
        /// </summary>
        private int videoCheckCountdown;

        private void maybeExtractVideoAudio()
        {
            if (--videoCheckCountdown > 0) return;
            videoCheckCountdown = 30;

            if (!videoProvidesAudio.Current.Value) return;

            string? videoFile = working.Value.BeatmapSetInfo.Files
                                       .Select(f => f.Filename)
                                       .FirstOrDefault(MediaAudioExtractor.IsVideo);
            if (videoFile == null || extractedVideos.Contains(videoFile)) return;

            extractedVideos.Add(videoFile);

            Task.Run(() => extractVideoAudio(videoFile))
                .ContinueWith(task => Scheduler.Add(() =>
                {
                    if (task.IsFaulted)
                    {
                        status($"Video audio extraction failed: {task.Exception?.GetBaseException().Message}");
                        return;
                    }

                    var track = trackSet.Import(Path.GetFileNameWithoutExtension(videoFile) + @" (video audio)", task.Result);
                    track.IsAccompaniment = true;
                    analyzeLater(track);
                    status($"Video audio joined the track list ({trackSet.Tracks.Count} in the set).");
                }));
        }

        private string extractVideoAudio(string videoFile)
        {
            string? src = materialise(videoFile);
            if (src == null)
                throw new InvalidOperationException("video audio extraction failed: cannot read the video from the beatmap set");

            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         Path.GetFileNameWithoutExtension(videoFile) + "-audio.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outWav)!);

            return MediaAudioExtractor.TryExtractToWav(src, outWav, out string error)
                ? outWav
                : throw new InvalidOperationException($"video audio extraction failed: {error}");
        }

        /// <summary>Copy a beatmap-set file to a temp path the bridge can read.</summary>
        private string? materialise(string fileName)
        {
            var beatmap = working.Value;
            string tmp = Path.Combine(Path.GetTempPath(), "kan-forge", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);

            using (var src = beatmap.GetStream(fileName))
            {
                if (src == null) return null;

                using var dst = File.Create(tmp);
                src.CopyTo(dst);
            }

            return tmp;
        }

        // ------------------------------------------------------------------
        // Import (the single file-reading surface)
        // ------------------------------------------------------------------

        private void addTrack(FileInfo file)
        {
            if (!file.Exists)
            {
                status("Import: the selected file does not exist.");
                return;
            }

            var track = trackSet.Import(Path.GetFileNameWithoutExtension(file.Name), file.FullName);
            track.IsMixdown = ForgeTrack.LooksLikeMixdown(file.Name);
            analyzeLater(track);
            status($"Track '{file.Name}' imported ({trackSet.Tracks.Count} in the set).");
        }

        private void analyzeLater(ForgeTrack track)
            => Task.Run(() => TrackAnalysis.Analyze(track));

        private void status(string message)
        {
            Scheduler.Add(() => statusText.Text = message);
        }
    }
}
