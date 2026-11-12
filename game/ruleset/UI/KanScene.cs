// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Utils;
using osuTK.Graphics;
using Kan.Core.Design;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// The level's design as it plays: which section is
    /// on, the colours in force (eased from one section's palette to the
    /// next, never jumping) and how far the screen is into a rest or a
    /// climax. Everything that draws gameplay reads it.
    /// </summary>
    public partial class KanScene : Component
    {
        /// <summary>A rest lets go of the screen this long before its next note, so the note is seen coming.</summary>
        public const double REST_RELEASE_MS = 2600;

        /// <summary>The countdown to the next note at the end of a rest.</summary>
        public const double COUNTDOWN_MS = 3000;

        private const double colour_ease_ms = 700;
        private const double rest_ease_ms = 500;

        private readonly KanPlayState state;
        private int nextCursor;

        public KanDesign Design => state.Design;

        public Color4 Note { get; private set; }
        public Color4 NoteLight { get; private set; }
        public Color4 BackgroundDark { get; private set; }
        public Color4 BackgroundLit { get; private set; }
        public Color4 Trail { get; private set; }
        public Color4 Hot { get; private set; }
        public Color4 Climax { get; private set; }

        /// <summary>0 while playing, 1 deep in a rest (eased).</summary>
        public float Rest { get; private set; }

        /// <summary>0 outside a climax, 1 inside (eased).</summary>
        public float ClimaxAmount { get; private set; }

        /// <summary>The section playing now (null outside every section).</summary>
        public DesignSection? Section { get; private set; }

        /// <summary>The next note's start (ms), or +∞ after the last one.</summary>
        public double NextNoteMs { get; private set; } = double.PositiveInfinity;

        /// <summary>The next note's index, or -1.</summary>
        public int NextNote { get; private set; } = -1;

        /// <summary>A section began (shown by its banner).</summary>
        public event Action<DesignSection>? SectionStarted;

        /// <summary>How strong hit effects are: the design's setting, half again in a climax.</summary>
        public float Sparkle => Design.Sparkle * (1 + 0.5f * ClimaxAmount);

        public KanScene(KanPlayState state)
        {
            this.state = state;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            snapTo(Design.PaletteAt(Clock.CurrentTime).ForMode(KanNotation.Palette));
        }

        protected override void Update()
        {
            base.Update();

            double now = Clock.CurrentTime;

            findNextNote(now);

            var section = Design.SectionAt(now);

            if (!ReferenceEquals(section, Section))
            {
                Section = section;
                if (section != null) SectionStarted?.Invoke(section);
            }

            // A break rests; so does an intro or outro with nothing left to play in it.
            bool restSection = section != null
                               && (section.IsRest || (section.Kind is SectionKind.Intro or SectionKind.Outro && NextNoteMs >= section.EndMs))
                               && !anyNoteSounding(now);
            bool resting = restSection && NextNoteMs - now > REST_RELEASE_MS;

            double restK = Math.Min(1, Math.Abs(Time.Elapsed) / rest_ease_ms);
            Rest += ((resting ? 1 : 0) - Rest) * (float)restK;

            double climaxK = Math.Min(1, Math.Abs(Time.Elapsed) / colour_ease_ms);
            ClimaxAmount += ((section?.Climax == true ? 1 : 0) - ClimaxAmount) * (float)climaxK;

            // The palette the player chose (issue 18): the level's own, or a
            // colour-blind-safe override that wins over the level.
            easeTo(Design.PaletteAt(now).ForMode(KanNotation.Palette), Math.Min(1, Math.Abs(Time.Elapsed) / colour_ease_ms));
        }

        private bool anyNoteSounding(double now)
        {
            int from = Math.Max(0, nextCursor - 4);

            for (int i = from; i < state.Notes.Count && state.Notes[i].StartTime <= now; i++)
            {
                if (state.Notes[i].EndMs > now) return true;
            }

            return false;
        }

        private void findNextNote(double now)
        {
            // Seeking back (replays) restarts the search.
            if (nextCursor > 0 && nextCursor <= state.Notes.Count && state.Notes[nextCursor - 1].StartTime >= now)
                nextCursor = 0;

            while (nextCursor < state.Notes.Count && state.Notes[nextCursor].StartTime < now)
                nextCursor++;

            NextNote = nextCursor < state.Notes.Count ? nextCursor : -1;
            NextNoteMs = NextNote >= 0 ? state.Notes[NextNote].StartTime : double.PositiveInfinity;
        }

        private void snapTo(KanPalette p)
        {
            Note = hex(p.Note);
            NoteLight = hex(p.NoteLight);
            BackgroundDark = hex(p.BackgroundDark);
            BackgroundLit = hex(p.BackgroundLit);
            Trail = hex(p.Trail);
            Hot = hex(p.Hot);
            Climax = hex(p.Climax);
        }

        private void easeTo(KanPalette p, double k)
        {
            Note = mix(Note, p.Note, k);
            NoteLight = mix(NoteLight, p.NoteLight, k);
            BackgroundDark = mix(BackgroundDark, p.BackgroundDark, k);
            BackgroundLit = mix(BackgroundLit, p.BackgroundLit, k);
            Trail = mix(Trail, p.Trail, k);
            Hot = mix(Hot, p.Hot, k);
            Climax = mix(Climax, p.Climax, k);
        }

        private static Color4 mix(Color4 from, string to, double k) => Interpolation.ValueAt(k, from, hex(to), 0, 1);

        private static readonly System.Collections.Generic.Dictionary<string, Color4> parsed = new System.Collections.Generic.Dictionary<string, Color4>();

        /// <summary>Palette colours are parsed once (this runs every frame).</summary>
        private static Color4 hex(string h)
        {
            if (!parsed.TryGetValue(h, out var c))
                parsed[h] = c = Color4Extensions.FromHex(h);
            return c;
        }
    }
}
