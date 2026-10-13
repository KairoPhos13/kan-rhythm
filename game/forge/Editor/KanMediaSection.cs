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
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
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
        private FillFlowContainer explorer = null!;

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
                Text = @"Arrange, edit and use the tracks in the 'track' tab.",
            };
            explorer = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 6),
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
                new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                    Text = @"The set (selected tracks highlighted)",
                    Margin = new MarginPadding { Top = 12 },
                },
                explorer,
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
            hideStockAudioSelector();

            // The navigator applies immediately (like the stock rows) and
            // clears itself so more tracks can be picked right away.
            trackSelector.Current.BindValueChanged(change =>
            {
                if (change.NewValue == null) return;
                addTrack(change.NewValue);
                trackSelector.Current.Value = null;
            });
        }

        /// <summary>
        /// Owner rule 2026-10-13: Resources has ONE audio import surface (the
        /// navigator above). The upstream "Audio Track" selector is hidden —
        /// the 'music' role connects the chosen track to the beatmap audio
        /// instead. Implemented via reflection on an upstream private field:
        /// if a future upstream rename hides the change, the row merely
        /// reappears (harmless), and the behaviour is tracked in the
        /// fork-divergence log.
        /// </summary>
        private void hideStockAudioSelector()
        {
            try
            {
                var field = typeof(ResourcesSection).GetField("audioTrackChooser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field?.GetValue(this) is Drawable chooser)
                    chooser.Expire();
            }
            catch
            {
                // Never break the editor over cosmetics.
            }
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
            rebuildExplorer();

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
                    if (track == null) return;

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
            if (track == null)
            {
                status($"'{file.Name}' is already in the set.");
                return;
            }

            track.IsMixdown = ForgeTrack.LooksLikeMixdown(file.Name);
            analyzeLater(track);
            status($"Track '{file.Name}' imported.");
        }

        private void analyzeLater(ForgeTrack track)
            => Task.Run(() => TrackAnalysis.Analyze(track));

        private void status(string message)
        {
            Scheduler.Add(() => statusText.Text = message);
        }

        /// <summary>
        /// The whole set as a compact explorer (owner 2026-10-13): every
        /// track with its waveform as the row background and its roles as
        /// badges — selected tracks highlighted, never just a count.
        /// </summary>
        private void rebuildExplorer()
        {
            explorer.Clear();

            if (trackSet.Tracks.Count == 0)
            {
                explorer.Add(new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 10),
                    Alpha = 0.7f,
                    Text = @"no resources yet — import audio above (the video's audio joins automatically)",
                });
                return;
            }

            foreach (var track in trackSet.Tracks)
                explorer.Add(new ExplorerRow(track, removeFromSet));
        }

        private void removeFromSet(ForgeTrack track)
        {
            trackSet.Remove(track);
            rebuildExplorer();
            status($"'{track.Name}' removed from the set.");
        }

        /// <summary>Compact read-only row: waveform background + name + role badges + remove.</summary>
        private partial class ExplorerRow : Container
        {
            public ExplorerRow(ForgeTrack track, Action<ForgeTrack> onRemove)
            {
                RelativeSizeAxes = Axes.X;
                Height = 34;
                CornerRadius = 5;
                Masking = true;

                var badges = new List<string>();
                if (track.IsMusic) badges.Add(@"music");
                if (track.IsReference) badges.Add(@"reference");
                if (track.IsAccompaniment) badges.Add(@"accompaniment");
                if (track.DemuxPending) badges.Add(@"demux");
                if (track.IsSeparated) badges.Add(@"separated");
                if (track.IsMixdown) badges.Add(@"mixdown");
                if (track.LikelyEmpty) badges.Add(@"⚠ empty?");

                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = track.HasAnyRole
                            ? new Colour4(0.25f, 0.55f, 0.5f, 0.35f)
                            : new Colour4(0, 0, 0, 0.25f),
                    },
                    new WaveformPreview(track)
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0.25f,
                    },
                    new OsuSpriteText
                    {
                        Text = badges.Count > 0 ? $"{track.Name}   ·   {string.Join(" · ", badges)}" : track.Name,
                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 8 },
                    },
                    new FormButton
                    {
                        ButtonText = @"✕",
                        Width = 60,
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = 6 },
                        Action = () => onRemove(track),
                    },
                };
            }
        }
    }
}
