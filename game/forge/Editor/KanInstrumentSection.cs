// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Screens.Edit.Setup;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// Setup → "Instrument" (owner 2026-10-15: "the level can't know its
    /// instrument unless the user transcribes"): what the level is played on.
    /// It tells the game how to listen — one note at a time (winds, voice) or
    /// several (piano, guitar) — and the transcriber what to listen for. It
    /// travels with the level inside its score file (saved a moment after a
    /// change); the track and compose tabs show the same choice.
    /// </summary>
    public partial class KanInstrumentSection : SetupSection
    {
        private const double save_delay_ms = 800;

        public override LocalisableString Title => @"Instrument";

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private string saved = string.Empty;
        private double? saveAt;

        [BackgroundDependencyLoader]
        private void load()
        {
            LevelSession.Open(storage, working.Value);
            saved = ScoreSession.Score.Instrument;

            Children = new Drawable[]
            {
                new InstrumentDropdown
                {
                    Caption = @"played on",
                    HintText = @"The instrument this level is played on. It tells the game how to listen to the player — one note at a time (winds, voice) or several (piano, guitar) — and the transcriber what to listen for.",
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (ScoreSession.Score.Instrument != saved && saveAt == null)
                saveAt = Time.Current + save_delay_ms;

            if (saveAt is double at && Time.Current >= at)
            {
                saveAt = null;
                saved = ScoreSession.Score.Instrument;
                ScoreSession.SaveToLevel(working.Value, beatmaps);
            }
        }
    }
}
