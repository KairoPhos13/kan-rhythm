// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Edit.Setup;
using Kan.Forge.Pipeline;
using Kan.Forge.Transcription;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The level-creation panel of the KAN editor entry window (prompt §2.1):
    /// source-audio pipeline actions — "Separate audio stems" and "Generate
    /// transcription" — each running in the background with a live progress
    /// bar driven by the libkan progress API, while the user keeps filling the
    /// form. Registered by the kan! ruleset through
    /// <c>Ruleset.CreateEditorSetupSections()</c> (plugin surface, zero fork
    /// divergence).
    ///
    /// Source audio: the beatmap's audio track, or a URL override (fetched via
    /// the bridge's download path, "download" progress phase).
    /// </summary>
    public partial class KanCreationSection : SetupSection
    {
        public override LocalisableString Title => @"kan! creation";

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private FormTextBox sourceUrlBox = null!;
        private FormButton separateButton = null!;
        private FormButton transcribeButton = null!;
        private KanProgressBar separateProgress = null!;
        private KanProgressBar transcribeProgress = null!;
        private OsuSpriteText statusText = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                sourceUrlBox = new FormTextBox
                {
                    Caption = @"Source audio (URL, optional)",
                    PlaceholderText = @"https://… — leave empty to use the beatmap's audio",
                },
                separateButton = new FormButton
                {
                    Caption = @"Separate audio stems",
                    ButtonText = @"run",
                    Action = startSeparation,
                },
                separateProgress = new KanProgressBar(),
                transcribeButton = new FormButton
                {
                    Caption = @"Generate transcription",
                    ButtonText = @"run",
                    Action = startTranscription,
                },
                transcribeProgress = new KanProgressBar(),
                statusText = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 12),
                    Text = @"Ready.",
                },
            };
        }

        /// <summary>The source audio: URL override, else the beatmap audio materialised to a temp file.</summary>
        private string? resolveSourceAudio()
        {
            string url = sourceUrlBox.Current.Value?.Trim() ?? string.Empty;
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
                status("No source audio: set the beatmap audio or paste a URL.");
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
            stemDir => status($"Stems ready in {stemDir}"),
            ex => status($"Separation failed: {ex.Message}"));
        }

        private void startTranscription()
        {
            string? source = resolveSourceAudio();
            if (source == null)
            {
                status("No source audio: set the beatmap audio or paste a URL.");
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
