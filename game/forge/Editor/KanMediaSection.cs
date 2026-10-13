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
    /// section (audio track / background / video navigators) with the kan!
    /// creation flow merged in, so there is one organic media area and no
    /// seam between "the existing" and "the new" (owner requirement
    /// 2026-10-13):
    ///
    /// - the pipeline runs on the **same audio the stock navigator selects**;
    /// - a **video carries its audio** into the source track by default
    ///   (many clips have the audio inside the container) with a
    ///   "video provides the audio" switch for the visuals-only case;
    /// - **individual track import stays an option** (own stems/multitrack)
    ///   via the same file-navigator pattern, and the final step renders one
    ///   playable track back into the beatmap audio.
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
        private FormButton separateButton = null!;
        private FormButton transcribeButton = null!;
        private FormButton renderButton = null!;
        private KanProgressBar separateProgress = null!;
        private KanProgressBar transcribeProgress = null!;
        private OsuSpriteText statusText = null!;

        private readonly ForgeTrackSet trackSet = new();
        private FillFlowContainer trackFlow = null!;
        private FormFileSelector trackSelector = null!;

        // Per-action track pickers (owner 2026-10-13): separation ("demix")
        // and transcription run on a *specific* loaded track — the unmixed
        // stems / imported tracks — falling back to the source audio.
        private FormDropdown<string> separateSourceDropdown = null!;
        private FormDropdown<string> transcribeSourceDropdown = null!;
        private readonly Dictionary<string, string> trackSources = new();

        private const string source_audio_label = @"Source audio (beatmap)";

        private static bool devToolsEnabled
            => DebugUtils.IsDebugBuild
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(@"KAN_DEV_TOOLS"));

        [BackgroundDependencyLoader]
        private void loadKan()
        {
            var mergeAudio = new BindableBool(true);

            videoProvidesAudio = new FormCheckBox
            {
                Caption = @"Video provides the audio",
                HintText = @"When the clip's audio lives inside the video, it becomes the source track. Turn off to use the video for visuals only.",
                Current = mergeAudio,
                Margin = new MarginPadding { Top = 12 },
            };
            separateButton = new FormButton
            {
                Caption = @"Separate audio stems (demix)",
                ButtonText = @"run",
                Action = startSeparation,
            };
            separateSourceDropdown = new FormDropdown<string>
            {
                Caption = @"Demix track (a loaded unmixed track — default: the source audio)",
            };
            separateProgress = new KanProgressBar();
            transcribeButton = new FormButton
            {
                Caption = @"Generate transcription",
                ButtonText = @"run",
                Action = startTranscription,
            };
            transcribeSourceDropdown = new FormDropdown<string>
            {
                Caption = @"Transcribe track (a loaded unmixed track — default: the source audio)",
            };
            transcribeProgress = new KanProgressBar();
            trackFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 6),
            };
            trackSelector = new FormFileSelector(SupportedExtensions.AUDIO_EXTENSIONS)
            {
                Caption = @"Add individual track (optional)",
                PlaceholderText = @"own stems, multitrack material, reference takes…",
            };
            renderButton = new FormButton
            {
                Caption = @"Render single track (mixdown to beatmap audio)",
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
                separateSourceDropdown,
                separateButton,
                separateProgress,
                transcribeSourceDropdown,
                transcribeButton,
                transcribeProgress,
                new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                    Text = @"Tracks",
                    Margin = new MarginPadding { Top = 12 },
                },
                trackFlow,
                trackSelector,
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

            // The navigator applies immediately — same behaviour as the
            // stock background/video rows.
            trackSelector.Current.BindValueChanged(change =>
            {
                if (change.NewValue != null)
                    addTrack(change.NewValue);
            });

            rebuildSourceDropdowns();
        }

        /// <summary>
        /// The source audio for the pipeline — merged semantics:
        /// the beatmap's audio track if set (stock navigator), otherwise the
        /// audio inside the beatmap's video when "video provides the audio"
        /// is on.
        /// </summary>
        private string? resolveSourceAudio()
        {
            string? audioFile = Beatmap.BeatmapInfo.Metadata.AudioFile;
            if (!string.IsNullOrEmpty(audioFile))
                return materialise(audioFile);

            if (videoProvidesAudio.Current.Value)
            {
                string? videoFile = findVideoFile();
                if (videoFile != null)
                    return extractVideoAudio(videoFile);
            }

            return null;
        }

        private string? findVideoFile()
            => working.Value.BeatmapSetInfo.Files
                      .Select(f => f.Filename)
                      .FirstOrDefault(MediaAudioExtractor.IsVideo);

        private string? extractVideoAudio(string videoFile)
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

        /// <summary>
        /// Rebuild both per-action pickers: the loaded (unmixed) tracks first,
        /// then the source audio — **mixdowns are never offered** (they are
        /// mixed material; demixing them again is a nonsense workflow).
        /// </summary>
        private void rebuildSourceDropdowns()
        {
            string? selectedSeparate = separateSourceDropdown.Current.Value;
            string? selectedTranscribe = transcribeSourceDropdown.Current.Value;

            trackSources.Clear();
            trackSources[source_audio_label] = string.Empty;

            foreach (var track in trackSet.Tracks)
            {
                if (track.IsMixdown || ForgeTrack.LooksLikeMixdown(track.Path))
                    continue;

                string label = track.Name;
                for (int i = 2; trackSources.ContainsKey(label); i++)
                    label = $"{track.Name} ({i})";

                trackSources[label] = track.Path;
            }

            // If the beatmap's audio is itself a rendered mixdown, the source
            // entry is not a valid demix/transcribe input either.
            string? audioFile = Beatmap.BeatmapInfo.Metadata.AudioFile;
            if (!string.IsNullOrEmpty(audioFile) && ForgeTrack.LooksLikeMixdown(audioFile))
                trackSources.Remove(source_audio_label);

            string[] items = trackSources.Keys.ToArray();

            foreach (var (dropdown, previous) in new[]
                     {
                         (separateSourceDropdown, selectedSeparate),
                         (transcribeSourceDropdown, selectedTranscribe),
                     })
            {
                dropdown.Items = items;
                dropdown.Current.Value = previous != null && trackSources.ContainsKey(previous)
                    ? previous
                    : items.FirstOrDefault() ?? source_audio_label;
            }
        }

        /// <summary>
        /// The input for a pipeline action: the selected loaded track, else
        /// the source audio — never a mixdown (guarded on both ends). Reports
        /// every failure case itself; returns null when there is no valid
        /// input.
        /// </summary>
        private string? resolveActionSource(FormDropdown<string> dropdown)
        {
            string? label = dropdown.Current.Value;

            if (label != null && trackSources.TryGetValue(label, out string? path) && path.Length > 0)
            {
                if (ForgeTrack.LooksLikeMixdown(path))
                {
                    status($"{label} is a rendered mixdown — pick an unmixed track.");
                    return null;
                }

                return path;
            }

            try
            {
                string? source = resolveSourceAudio();

                if (source == null)
                {
                    status("No source audio: pick a track above, or let the video provide it.");
                    return null;
                }

                if (ForgeTrack.LooksLikeMixdown(source))
                {
                    status("The beatmap audio is a rendered mixdown — pick an unmixed track above.");
                    return null;
                }

                return source;
            }
            catch (Exception e)
            {
                status(e.Message);
                return null;
            }
        }

        private void startSeparation()
        {
            string? source = resolveActionSource(separateSourceDropdown);
            if (source == null) return;

            setBusy(true);
            separateProgress.Set(0, "starting");
            string outDir = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         "stems-" + Guid.NewGuid().ToString("N")[..8]);

            runJob(() => ForgeJobs.SeparateStemsAsync(source, outDir, report(separateProgress), Scheduler),
                separateProgress,
                stemDir =>
                {
                    int added = registerStems(stemDir);
                    status($"Stems ready in {stemDir} — {added} tracks added.");
                },
                ex => status($"Separation failed: {ex.Message}"));
        }

        private int registerStems(string stemDir)
        {
            if (!Directory.Exists(stemDir)) return 0;

            int added = 0;
            foreach (string file in Directory.EnumerateFiles(stemDir, "*.wav").OrderBy(f => f))
            {
                trackSet.Import(Path.GetFileNameWithoutExtension(file), file);
                added++;
            }

            if (added > 0) Scheduler.Add(rebuildTrackRows);
            return added;
        }

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

        private void rebuildTrackRows()
        {
            trackFlow.Clear();
            foreach (var track in trackSet.Tracks)
                trackFlow.Add(new TrackRow(track, removeTrack));

            rebuildSourceDropdowns();
        }

        private void startRender()
        {
            var audible = trackSet.Audible.ToList();
            if (audible.Count == 0)
            {
                status("Render: no audible tracks — add tracks or unmute some.");
                return;
            }

            setBusy(true);
            string outWav = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         "render-" + Guid.NewGuid().ToString("N")[..8] + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outWav)!);

            runJob(() => ForgeJobs.RenderMixAsync(audible, outWav, null, Scheduler),
                transcribeProgress,
                path =>
                {
                    string? applied = applyAsBeatmapAudio(path);
                    status(applied != null
                        ? $"Rendered {audible.Count} tracks → {applied} (now the beatmap audio)."
                        : $"Rendered {audible.Count} tracks → {path} (could not set it as beatmap audio).");
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

                // The beatmap audio is now a mixdown: the source entry must
                // leave the demix/transcribe pickers.
                rebuildSourceDropdowns();
                return name;
            }
            catch (Exception e)
            {
                status($"Could not set the rendered track as beatmap audio: {e.Message}");
                return null;
            }
        }

        private void startTranscription()
        {
            string? source = resolveActionSource(transcribeSourceDropdown);
            if (source == null) return;

            setBusy(true);
            transcribeProgress.Set(0, "starting");

            runJob(() => ForgeJobs.TranscribeAsync(source, @"keyboard", report(transcribeProgress), Scheduler),
                transcribeProgress,
                draft => status($"Transcription ready: {describe(draft)}"),
                ex => status($"Transcription failed: {ex.Message}"));
        }

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
            separateButton.Enabled.Value = !busy;
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

        /// <summary>One editor track: name, volume, mute, remove.</summary>
        private partial class TrackRow : FillFlowContainer
        {
            public TrackRow(ForgeTrack track, Action<ForgeTrack> onRemove)
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

                gain.BindValueChanged(v => track.Gain = v.NewValue);
                muted.BindValueChanged(v => track.Muted = v.NewValue);

                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = track.Name,
                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
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
