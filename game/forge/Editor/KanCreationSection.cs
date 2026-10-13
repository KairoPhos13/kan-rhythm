// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
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
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Setup;
using Kan.Forge.Pipeline;
using Kan.Forge.Tracks;
using Kan.Forge.Transcription;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The level-creation panel of the KAN editor entry window (prompt §2.1):
    /// "Separate audio stems" and "Generate transcription" run in the
    /// background with live progress bars driven by the libkan progress API,
    /// while the user keeps filling the form. The generated stems (or any
    /// individual tracks the user imports) become editor **tracks** with
    /// per-track volume and mute (owner requirements 2026-10-13), and the
    /// final step **renders one playable track** into the beatmap's audio.
    ///
    /// Source audio: the beatmap's audio track, or a URL override. The URL
    /// path uses yt-dlp and is **developer-build tooling only** — it is not
    /// part of the official flow (see game/docs/editor-pipeline.md §7 for the
    /// policy).
    /// </summary>
    public partial class KanCreationSection : SetupSection
    {
        public override LocalisableString Title => @"kan! creation";

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        private FormTextBox? sourceUrlBox;
        private FormButton separateButton = null!;
        private FormButton transcribeButton = null!;
        private KanProgressBar separateProgress = null!;
        private KanProgressBar transcribeProgress = null!;
        private OsuSpriteText statusText = null!;

        private readonly ForgeTrackSet trackSet = new();
        private FillFlowContainer trackFlow = null!;
        private FormTextBox addTrackPathBox = null!;
        private FormButton renderButton = null!;

        private static bool devToolsEnabled
            => DebugUtils.IsDebugBuild
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(@"KAN_DEV_TOOLS"));

        [BackgroundDependencyLoader]
        private void load()
        {
            var content = new System.Collections.Generic.List<Drawable>();

            if (devToolsEnabled)
            {
                content.Add(sourceUrlBox = new FormTextBox
                {
                    Caption = @"Source audio URL (developer tooling)",
                    PlaceholderText = @"https://… — fetched via yt-dlp; not part of the official flow",
                });
            }
            else
            {
                content.Add(new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 11),
                    Text = @"Source audio: the beatmap's audio track (local files only in this build).",
                });
            }

            content.Add(separateButton = new FormButton
            {
                Caption = @"Separate audio stems",
                ButtonText = @"run",
                Action = startSeparation,
            });
            content.Add(separateProgress = new KanProgressBar());
            content.Add(transcribeButton = new FormButton
            {
                Caption = @"Generate transcription",
                ButtonText = @"run",
                Action = startTranscription,
            });
            content.Add(transcribeProgress = new KanProgressBar());

            content.Add(new OsuSpriteText
            {
                Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                Text = @"Tracks",
                Margin = new MarginPadding { Top = 10 },
            });
            content.Add(trackFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 6),
            });
            content.Add(addTrackPathBox = new FormTextBox
            {
                Caption = @"Add individual track (audio file path)",
                PlaceholderText = @"/path/to/your/track.wav",
            });
            content.Add(new FormButton
            {
                Caption = @"Import track",
                ButtonText = @"add",
                Action = importTrack,
            });
            content.Add(renderButton = new FormButton
            {
                Caption = @"Render single track (mixdown to beatmap audio)",
                ButtonText = @"render",
                Action = startRender,
            });
            content.Add(statusText = new OsuSpriteText
            {
                Font = OsuFont.GetFont(size: 12),
                Text = @"Ready.",
            });

            Children = content.ToArray();
        }

        /// <summary>The source audio: URL override (dev tooling), else the beatmap audio.</summary>
        private string? resolveSourceAudio()
        {
            string url = sourceUrlBox?.Current.Value?.Trim() ?? string.Empty;
            if (url.Length > 0) return url;

            string? audioFile = Beatmap.BeatmapInfo.Metadata.AudioFile;
            if (string.IsNullOrEmpty(audioFile))
                return null;

            var beatmap = working.Value;
            if (beatmap == null) return null;

            string tmp = Path.Combine(Path.GetTempPath(), "kan-forge", audioFile);
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);

            using (var src = beatmap.GetStream(audioFile))
            {
                if (src == null) return null;

                using var dst = File.Create(tmp);
                src.CopyTo(dst);
            }

            return tmp;
        }

        private void startSeparation()
        {
            string? source = resolveSourceAudio();
            if (source == null)
            {
                status("No source audio: set the beatmap audio" + (devToolsEnabled ? " or paste a URL." : "."));
                return;
            }

            setBusy(true);
            separateProgress.Set(0, "starting");
            string outDir = Path.Combine(Path.GetTempPath(), "kan-forge",
                                         "stems-" + Guid.NewGuid().ToString("N")[..8]);

            runJob(async () =>
            {
                string path = source;

                if (isUrl(source))
                {
                    Directory.CreateDirectory(outDir);
                    string wav = Path.Combine(outDir, "source.wav");
                    path = await ForgeJobs.FetchAudioAsync(source, wav, report(separateProgress), Scheduler);
                }

                return await ForgeJobs.SeparateStemsAsync(path, outDir, report(separateProgress), Scheduler);
            },
            separateProgress,
            stemDir =>
            {
                int added = registerStems(stemDir);
                status($"Stems ready in {stemDir} — {added} tracks added.");
            },
            ex => status($"Separation failed: {ex.Message}"));
        }

        /// <summary>Register the generated stems as editor tracks (volume/mute per channel).</summary>
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

        private void importTrack()
        {
            string path = addTrackPathBox.Current.Value?.Trim() ?? string.Empty;
            if (path.Length == 0 || !File.Exists(path))
            {
                status("Import: give the path of an existing audio file.");
                return;
            }

            trackSet.Import(Path.GetFileNameWithoutExtension(path), path);
            addTrackPathBox.Current.Value = string.Empty;
            rebuildTrackRows();
            status($"Track '{Path.GetFileName(path)}' added ({trackSet.Tracks.Count} in the set).");
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

            runJob(async () => await ForgeJobs.RenderMixAsync(audible, outWav, null, Scheduler),
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

        /// <summary>Add the rendered mix to the beatmap set and make it the audio track.</summary>
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

        private void startTranscription()
        {
            string? source = resolveSourceAudio();
            if (source == null)
            {
                status("No source audio: set the beatmap audio" + (devToolsEnabled ? " or paste a URL." : "."));
                return;
            }

            setBusy(true);
            transcribeProgress.Set(0, "starting");

            runJob(async () =>
            {
                string path = source;

                if (isUrl(source))
                {
                    string wav = Path.Combine(Path.GetTempPath(), "kan-forge",
                                              Guid.NewGuid().ToString("N")[..8] + ".wav");
                    Directory.CreateDirectory(Path.GetDirectoryName(wav)!);
                    path = await ForgeJobs.FetchAudioAsync(source, wav, report(transcribeProgress), Scheduler);
                }

                return await ForgeJobs.TranscribeAsync(path, @"keyboard", report(transcribeProgress), Scheduler);
            },
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

        private static bool isUrl(string source)
            => source.StartsWith("http", StringComparison.OrdinalIgnoreCase);

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
