// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Text;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens.Edit.Setup;
using osuTK;
using Kan.Core.Score;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Setup → "Cover credits" (owner 2026-10-14): who made the cover the
    /// level is built on, what each of them did, and links to their pages so
    /// players can go and thank and support them. Saved into the level's
    /// files (<see cref="KanCredits.FILE_NAME"/>) a moment after each change.
    /// </summary>
    public partial class KanCreditsSection : SetupSection
    {
        private const double save_delay_ms = 800;

        /// <summary>A form text box's height (caption + value).</summary>
        private const float link_row_height = 50;

        public override LocalisableString Title => @"Cover credits";

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private OsuGame? game { get; set; }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private KanCredits credits = new KanCredits();
        private FillFlowContainer performersFlow = null!;
        private string? savedJson;
        private double? saveAt;

        [BackgroundDependencyLoader]
        private void load()
        {
            credits = loadFromLevel() ?? new KanCredits();
            savedJson = credits.ToJson();

            if (credits.Performers.Count == 0)
                credits.Performers.Add(new CoverPerformer());

            var coverUrl = new Bindable<string>(credits.CoverUrl);
            coverUrl.BindValueChanged(v =>
            {
                credits.CoverUrl = v.NewValue;
                changed();
            });

            var note = new Bindable<string>(credits.Note);
            note.BindValueChanged(v =>
            {
                credits.Note = v.NewValue;
                changed();
            });

            Children = new Drawable[]
            {
                new OsuTextFlowContainer(t =>
                {
                    t.Font = OsuFont.Style.Caption1;
                    t.Colour = colourProvider.Content2;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Horizontal = 9 },
                    Text = "Many levels are built on covers that people record for the love of it and share. "
                           + "The song's artist goes in the metadata above; here, credit who played this version, "
                           + "and link to their pages so players who enjoy it can go and thank and support them.",
                },
                linkRow(@"where the cover was published", @"For example its YouTube video.", coverUrl),
                performersFlow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                },
                new ForgeButton(FontAwesome.Solid.UserPlus, @"add a performer")
                {
                    TooltipText = @"Covers are often made together: credit everyone who took part",
                    Action = () =>
                    {
                        credits.Performers.Add(new CoverPerformer());
                        rebuildPerformers();
                    },
                },
                textBox(@"a message of thanks (optional)", @"Shown with the credits, e.g. ""Thank you for letting us play your arrangement!""", note),
            };

            rebuildPerformers();
        }

        /// <summary>A text box that also saves as soon as it is committed (Enter, or focus moved away).</summary>
        private FormTextBox textBox(string caption, string hint, Bindable<string> current, string placeholder = "")
        {
            var box = new FormTextBox
            {
                Caption = caption,
                HintText = hint,
                PlaceholderText = placeholder,
                Current = current,
            };
            box.OnCommit += (_, _) => saveNow();
            return box;
        }

        private void rebuildPerformers()
        {
            performersFlow.Clear();

            for (int i = 0; i < credits.Performers.Count; i++)
                performersFlow.Add(performerCard(credits.Performers[i], i + 1));

            changed();
        }

        private Drawable performerCard(CoverPerformer performer, int number)
        {
            var name = new Bindable<string>(performer.Name);
            name.BindValueChanged(v =>
            {
                performer.Name = v.NewValue;
                changed();
            });

            var role = new Bindable<string>(performer.Role);
            role.BindValueChanged(v =>
            {
                performer.Role = v.NewValue;
                changed();
            });

            var links = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 5),
            };

            if (performer.Links.Count == 0)
                performer.Links.Add(string.Empty);

            for (int i = 0; i < performer.Links.Count; i++)
                links.Add(performerLink(performer, i));

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 5),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 28,
                        Padding = new MarginPadding { Left = 9 },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = $"Performer {number}",
                                Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new ForgeButton(FontAwesome.Solid.UserMinus, @"remove")
                            {
                                RelativeSizeAxes = Axes.None,
                                Width = 100,
                                Height = 26,
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                TooltipText = @"Remove this performer from the credits",
                                Action = () =>
                                {
                                    credits.Performers.Remove(performer);
                                    rebuildPerformers();
                                },
                            },
                        },
                    },
                    textBox(@"name", @"As they sign their work (artist name, channel name…).", name),
                    textBox(@"what they did", @"For example: ocarina, vocals, arrangement, mixing, video.", role),
                    links,
                    new ForgeButton(FontAwesome.Solid.Link, @"add a link")
                    {
                        Height = 28,
                        TooltipText = @"Their channel, social networks, or a page to support them (Patreon, Ko-fi…)",
                        Action = () =>
                        {
                            performer.Links.Add(string.Empty);
                            links.Add(performerLink(performer, performer.Links.Count - 1));
                        },
                    },
                },
            };
        }

        private Drawable performerLink(CoverPerformer performer, int index)
        {
            var link = new Bindable<string>(performer.Links[index]);
            link.BindValueChanged(v =>
            {
                performer.Links[index] = v.NewValue;
                changed();
            });

            return linkRow(@"link", @"Their channel, a social network, or a page to support them.", link);
        }

        /// <summary>A web link box that names the site it points to, with a button to open it.</summary>
        private Drawable linkRow(string caption, string hint, Bindable<string> link)
        {
            FormTextBox box;
            ForgeButton open;

            var row = new GridContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                ColumnDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.Absolute, 6), new Dimension(GridSizeMode.Absolute, 128) },
                RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                Content = new[]
                {
                    new Drawable[]
                    {
                        box = textBox(caption, hint, link, @"https://…"),
                        Empty(),
                        // A fixed height, the text box's: sized relative to an auto-sized row
                        // it grew without bound and covered the section (2026-10-15).
                        open = new ForgeButton(FontAwesome.Solid.ExternalLinkAlt, @"open")
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = link_row_height,
                            TooltipText = @"Open the link in the browser to check it",
                        },
                    },
                },
            };

            open.Action = () =>
            {
                string? url = KanCredits.NormaliseLink(link.Value);
                if (url != null) game?.OpenUrlExternally(url);
            };

            // The button names the site the link points to, so a typo shows at once.
            link.BindValueChanged(v =>
            {
                string site = KanCredits.SiteName(v.NewValue);
                bool blank = string.IsNullOrWhiteSpace(v.NewValue);
                open.Text = blank ? @"open" : site.Length > 0 ? site : @"not a link";
                open.Enabled.Value = !blank && site.Length > 0;
            }, true);


            return row;
        }

        // ------------------------------------------------------------------
        // Saving into the level
        // ------------------------------------------------------------------

        private void changed() => saveAt = Time.Current + save_delay_ms;

        /// <summary>A box was committed (Enter, or focus moved away): write without waiting.</summary>
        private void saveNow()
        {
            saveAt = null;
            save();
        }

        protected override void Update()
        {
            base.Update();


            if (saveAt != null && Time.Current >= saveAt)
            {
                saveAt = null;
                save();
            }
        }

        private void save()
        {
            string json = credits.ToJson();
            if (json == savedJson) return;

            // Nothing written yet and nothing to write: keep the level clean.
            if (credits.IsEmpty && working.Value.BeatmapSetInfo.GetFile(KanCredits.FILE_NAME) == null) return;

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    beatmaps.AddFile(working.Value.BeatmapSetInfo, stream, KanCredits.FILE_NAME);

                savedJson = json;
            }
            catch (Exception e)
            {
                osu.Framework.Logging.Logger.Log($"kan!forge: could not save the cover credits: {e.Message}", level: osu.Framework.Logging.LogLevel.Important);
            }
        }

        private KanCredits? loadFromLevel()
        {
            try
            {
                var file = working.Value.BeatmapSetInfo.GetFile(KanCredits.FILE_NAME);
                if (file == null) return null;

                using var stream = working.Value.GetStream(file.File.GetStoragePath());
                if (stream == null) return null;

                using var reader = new StreamReader(stream);
                return KanCredits.FromJson(reader.ReadToEnd());
            }
            catch
            {
                return null;
            }
        }

    }
}
