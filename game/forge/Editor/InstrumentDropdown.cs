// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using Kan.Forge.Pipeline;

namespace Kan.Forge.Editor
{
    /// <summary>
    /// The level's instrument (the transcriber's profile), shared by every
    /// place that transcribes: the track tab's Transcribe card and the
    /// compose tab's section tools.
    /// </summary>
    internal partial class InstrumentDropdown : FormDropdown<string>
    {
        public InstrumentDropdown()
        {
            Caption = @"instrument (what the transcriber listens for)";
            HintText = @"Pick the instrument that plays the reference: it tells the transcriber whether one note sounds at a time (winds, voice) or several (piano, guitar), and the game how to listen to you.";
            Items = ForgeJobs.Profiles;

            var current = new Bindable<string>(ForgeTrackSession.Instrument);
            current.BindValueChanged(v =>
            {
                ForgeTrackSession.Instrument = v.NewValue;
                ScoreSession.Score.Instrument = v.NewValue;
            });
            Current = current;
        }

        protected override LocalisableString GenerateItemText(string item) => ForgeJobs.ProfileLabel(item);

        protected override void Update()
        {
            base.Update();

            // Follow a change made in the other tab.
            if (Current.Value != ForgeTrackSession.Instrument)
                Current.Value = ForgeTrackSession.Instrument;
        }
    }
}
