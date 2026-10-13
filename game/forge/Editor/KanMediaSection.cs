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
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Edit.Setup;
using osu.Game.Utils;
using Kan.Forge.Media;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Forge.Transcription;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The editor's **media &amp; creation** panel — the stock osu! Resources
    /// section (audio / background / video) with the kan! creation flow
    /// merged in as one organic area (owner requirements 2026-10-13):
    ///
    /// - **one place to load tracks** (the navigator below; pick repeatedly
    ///   for multiple tracks) and **one place for video** (the stock row
    ///   above — with "video provides the audio", the video's audio joins the
    ///   list as a track);
    /// - **one unified track list** where each track takes a role:
    ///   *reference* (the original take — transcription uses it
    ///   automatically; the future Mod Auto plays to it), *accompaniment*
    ///   (what the render mixes), *demux* (queued for source separation);
    /// - **demux results rejoin the list** and can take any role, but are
    ///   never demuxable again (a track that has been demuxed neither) —
    ///   only unmixed, undemuxed material can be separated;
    /// - **nothing is mandatory**: demux and transcription are independent
    ///   optional actions — needed in one case, skippable in another. The
    ///   render only needs its accompaniment selection.
    ///
    /// Everything below the stock rows is kan! code; the upstream rows are
    /// untouched (registered through Ruleset.CreateEditorSetupSections).
    /// </summary>
    public partial class KanMediaSection : ResourcesSection
    {
        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private FormCheckBox videoProvidesAudio = null!;
        private FormButton demuxButton = null!;
        private FormButton transcribeButton = null!;
        private FormButton renderButton = null!;
        private KanProgressBar demuxProgress = null!;
        private KanProgressBar transcribeProgress = null!;
        private OsuSpriteText statusText = null!;

        private readonly ForgeTrackSet trackSet = new();
        private FillFlowContainer trackFlow = null!;
        private FormFileSelector trackSelector = null!;

        private readonly HashSet<string> extractedVideos = new();

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
            trackFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 6),
            };
            trackSelector = new FormFileSelector(SupportedExtensions.AUDIO_EXTENSIONS)
            {
                Caption = @"Add audio track",
                PlaceholderText = @"reference take, accompaniments, own stems… — pick repeatedly to add more",
            };
            demuxButton = new FormButton
            {
                Caption = @"Demux selected tracks (optional)",
                ButtonText = @"run",
                Action = startDemux,
            };
            demuxProgress = new KanProgressBar();
            transcribeButton = new FormButton
            {
                Caption = @"Transcribe the reference track (optional)",
                ButtonText = @"run",
                Action = startTranscription,
            };
            transcribeProgress = new KanProgressBar();
            renderButton = new FormButton
            {
                Caption = @"Render single track (mixdown of the accompaniments)",
                ButtonText = @"render",
                Action = startRender,
            };
            statusText = new OsuSpriteText
            {
                Font = OsuFont.GetFont(size: 12),
                Text = @"Ready.",
            };

            var added = new List<Drawable>
            {
                videoProvidesAudio,
                new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                    Text = @"Tracks",
                    Margin = new MarginPadding { Top = 12 },
                },
                trackFlow,
                trackSelector,
                demuxButton,
                demuxProgress,
                transcribeButton,
                transcribeProgress,
                renderButton,
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

        private static bool devToolsEnabled
            => DebugUtils.IsDebugBuild
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(@"KAN_DEV_TOOLS"));

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

            runJob(() => Task.Run(() => extractVideoAudio(videoFile)),
                demuxProgress,
                wav =>
                {
                    var track = trackSet.Import(Path.GetFileNameWithoutExtension(videoFile) + @" (video audio)", wav);
                    track.IsAccompaniment = true;
                    rebuildTrackRows();
                    status($"Video audio joined the track list ({trackSet.Tracks.Count} in the set).");
                },
                ex => status($"Video audio extraction failed: {ex.Message}"));
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
        // Track list
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
            rebuildTrackRows();
            status($"Track '{file.Name}' added ({trackSet.Tracks.Count} in the set).");
        }

        private void removeTrack(ForgeTrack track)
        {
            trackSet.Remove(track);
            rebuildTrackRows();
        }

        private void onRoleChanged()
        {
            // Deferred: role toggles fire from inside a row's own controls,
            // and rebuilding disposes that row.
            Scheduler.Add(rebuildTrackRows);
        }

        private void rebuildTrackRows()
        {
            trackFlow.Clear();
            foreach (var track in trackSet.Tracks)
                trackFlow.Add(new TrackRow(trackSet, track, removeTrack, onRoleChanged));
        }

        // ------------------------------------------------------------------
        // Optional actions — none of them requires another
        // ------------------------------------------------------------------

        private void startDemux()
        {
            var queue = trackSet.DemuxQueue.ToList();
            if (queue.Count == 0)
            {
                status(trackSet.Tracks.Count == 0
                    ? "Demux is optional — add a track and mark it 'demux' first."
                    : "Demux is optional — mark at least one eligible track 'demux' first.");
                return;
            }

            setBusy(true);
            demuxProgress.Set(0, "starting");

            runJob(() => Task.Run(() => demuxAll(queue)),
                demuxProgress,
                stemsAdded =>
                {
                    rebuildTrackRows();
                    status($"Demux done — {stemsAdded} stem tracks joined the list.");
                },
                ex => status($"Demux failed: {ex.Message}"));
        }

        private int demuxAll(List<ForgeTrack> queue)
        {
            int stemsAdded = 0;

            foreach (var track in queue)
            {
                string outDir = Path.Combine(Path.GetTempPath(), "kan-forge",
                                             "stems-" + Guid.NewGuid().ToString("N")[..8]);

                Scheduler.Add(() => demuxProgress.Set(0, $"demux {track.Name}"));
                ForgeJobs.SeparateStemsAsync(track.Path, outDir).GetAwaiter().GetResult();

                // The source has been demuxed (never again), and the results
                // join the list as separated tracks (any role except demux).
                track.DemuxPending = false;
                track.IsSeparated = true;

                if (!Directory.Exists(outDir)) continue;

                foreach (string file in Directory.EnumerateFiles(Path.Combine(outDir, "roformer"), "*.wav")
                                                 .OrderBy(f => f))
                {
                    var stem = trackSet.Import(Path.GetFileNameWithoutExtension(file), file);
                    stem.IsSeparated = true;
                    stemsAdded++;
                }
            }

            return stemsAdded;
        }

        private void startTranscription()
        {
            var reference = trackSet.Reference;
            if (reference == null)
            {
                status("Transcription is optional — mark one track as 'reference' first.");
                return;
            }

            setBusy(true);
            transcribeProgress.Set(0, "starting");

            runJob(() => ForgeJobs.TranscribeAsync(reference.Path, @"keyboard", report(transcribeProgress), Scheduler),
                transcribeProgress,
                draft => status($"Transcription ready from '{reference.Name}': {describe(draft)}"),
                ex => status($"Transcription failed: {ex.Message}"));
        }

        private void startRender()
        {
            var accomp = trackSet.Accompaniments.ToList();
            if (accomp.Count == 0)
            {
                status("Render: select at least one 'accompaniment' track first.");
                return;
            }

            setBusy(true);
            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         "render-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outWav)!);

            runJob(() => ForgeJobs.RenderMixAsync(accomp, outWav, null, Scheduler),
                transcribeProgress,
                path =>
                {
                    string? applied = applyAsBeatmapAudio(path);
                    status(applied != null
                        ? $"Rendered {accomp.Count} accompaniments → {applied} (now the beatmap audio)."
                        : $"Rendered {accomp.Count} accompaniments → {path} (could not set it as beatmap audio).");
                },
                ex => status($"Render failed: {ex.Message}"));
        }

        private string? applyAsBeatmapAudio(string wavPath)
        {
            try
            {
                var set = working.Value.BeatmapSetInfo;

                string name = @"audio-render.wav";
                for (int i = 1; set.Files.Any(f => f.Filename == name); i++)
                    name = $@"audio-render-{i}.wav";

                using (var stream = File.OpenRead(wavPath))
                    beatmaps.AddFile(set, stream, name);

                working.Value.BeatmapInfo.Metadata.AudioFile = name;
                return name;
            }
            catch (Exception e)
            {
                status($"Could not set the rendered track as beatmap audio: {e.Message}");
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Plumbing
        // ------------------------------------------------------------------

        private async void runJob<T>(Func<Task<T>> body, KanProgressBar progress,
                                     Action<T> onDone, Action<Exception> onFail)
        {
            try
            {
                T result = await body();
                progress.Complete();
                onDone(result);
            }
            catch (Exception ex)
            {
                progress.Fail();
                onFail(ex);
            }
            finally
            {
                setBusy(false);
            }
        }

        private static Action<ForgeProgress> report(KanProgressBar progress)
            => p => progress.Set(p.Percent, p.Phase);

        private void setBusy(bool busy)
        {
            demuxButton.Enabled.Value = !busy;
            transcribeButton.Enabled.Value = !busy;
            renderButton.Enabled.Value = !busy;
        }

        private void status(string message)
        {
            Scheduler.Add(() => statusText.Text = message);
        }

        private static string describe(TranscriptionDraft draft)
        {
            if (draft.Notes.Count == 0)
                return @"no resolved notes (empty draft — check the source audio)";

            int unresolved = 0;
            foreach (var n in draft.Notes)
                if (n.Midi < 0) unresolved++;

            return $"{draft.Notes.Count} notes ({unresolved} unresolved pitch)";
        }

        /// <summary>
        /// One row of the unified track list: name, roles (reference /
        /// accompaniment / demux), volume, mute, remove.
        /// </summary>
        private partial class TrackRow : FillFlowContainer
        {
            public TrackRow(ForgeTrackSet set, ForgeTrack track, Action<ForgeTrack> onRemove, Action onRoleChanged)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new osuTK.Vector2(0, 4);

                var gain = new BindableNumber<float>(track.Gain)
                {
                    MinValue = 0,
                    MaxValue = 2,
                    Precision = 0.05f,
                };
                var muted = new BindableBool(track.Muted);
                var reference = new BindableBool(track.IsReference);
                var accomp = new BindableBool(track.IsAccompaniment);
                var demux = new BindableBool(track.DemuxPending);

                gain.BindValueChanged(v => track.Gain = v.NewValue);
                muted.BindValueChanged(v => track.Muted = v.NewValue);
                reference.BindValueChanged(v =>
                {
                    // The reference role is exclusive across the set.
                    if (v.NewValue)
                        set.SetReference(track);
                    else
                        track.IsReference = false;

                    onRoleChanged();
                });
                accomp.BindValueChanged(v => track.IsAccompaniment = v.NewValue);
                demux.BindValueChanged(v =>
                {
                    if (!track.CanDemux && v.NewValue)
                    {
                        demux.Value = false;
                        return;
                    }

                    track.DemuxPending = v.NewValue;
                });

                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = track.Name,
                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                    },
                    new FormCheckBox
                    {
                        Caption = track.CanDemux
                            ? @"demux (separate into stems)"
                            : @"demux (already separated — not again)",
                        Current = demux,
                    },
                    new FormCheckBox
                    {
                        Caption = @"reference (the take to transcribe)",
                        Current = reference,
                    },
                    new FormCheckBox
                    {
                        Caption = @"accompaniment (mixed into the beatmap audio)",
                        Current = accomp,
                    },
                    new FormSliderBar<float>
                    {
                        Caption = @"volume",
                        Current = gain,
                        KeyboardStep = 0.05f,
                    },
                    new FormCheckBox
                    {
                        Caption = @"mute",
                        Current = muted,
                    },
                    new FormButton
                    {
                        ButtonText = $@"remove '{track.Name}'",
                        Action = () => onRemove(track),
                    },
                };
            }
        }

        /// <summary>Determinate progress bar with a phase caption.</summary>
        private partial class KanProgressBar : Container
        {
            private readonly Box fill;
            private readonly OsuSpriteText label;

            public KanProgressBar()
            {
                AutoSizeAxes = Axes.Y;
                RelativeSizeAxes = Axes.X;

                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 6,
                        CornerRadius = 3,
                        Masking = true,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Colour4(0, 0, 0, 0.4f),
                            },
                            fill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0,
                                Colour = new Colour4(1, 1, 1, 0.8f),
                            },
                        },
                    },
                    label = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: 10),
                        Margin = new MarginPadding { Top = 8 },
                    },
                };
            }

            public void Set(int percent, string phase)
            {
                fill.Width = Math.Clamp(percent, 0, 100) / 100.0f;
                label.Text = $"{phase}… {percent}%";
            }

            public void Complete()
            {
                fill.Width = 1;
                label.Text = @"done";
            }

            public void Fail() => label.Text = @"failed";
        }
    }
}
