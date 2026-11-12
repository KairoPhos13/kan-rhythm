// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

namespace Kan.Core.Design
{
    /// <summary>
    /// The colours of the judgment states and the other standing cues, as hex
    /// strings so the game, the editor and the accessibility tests all read
    /// the same values (issue 18). These never change with the palette ("the
    /// grades keep their colours in every palette"): a player reads them the
    /// same way in any level. Every state is also told apart without colour
    /// (a word, a mark or motion), so no state depends on these alone.
    ///
    /// Checked 2026-11-12 against simulated colour vision (Machado 2009,
    /// see <see cref="KanColourVision"/>): the four grades stay at least
    /// 0.10 apart in OKLab for every kind of vision. Only "meh" moved for
    /// that (from 9e9ea8, which read the same as the unknown pitch's grey):
    /// it is now a light lavender grey. The known weak pair is a miss's red
    /// against the unknown's grey under protanopia (0.07): a miss is always
    /// a word and a shake, an unknown pitch always a question mark, so
    /// neither is read by colour alone (KanColourBlindTest).
    /// </summary>
    public static class KanGradeColours
    {
        /// <summary>A perfect: near-white (the ring of a perfectly timed note).</summary>
        public const string Perfect = @"fffaf0";

        /// <summary>The faint gold a perfect radiates: its halo, and the fill of a note played in tune.</summary>
        public const string PerfectGlow = @"ffd98a";

        /// <summary>The now line and the grid while the perfect window is open (told from the palette's hot colour by its width).</summary>
        public const string WindowGold = @"ffb300";

        /// <summary>A good.</summary>
        public const string Good = @"7cdb8a";

        /// <summary>A meh: a light lavender grey, clear of both the good's green and the unknown's grey.</summary>
        public const string Meh = @"ccbfe5";

        /// <summary>A miss: clearly red for the feedback word and the line's jolt (the missed notes themselves go black).</summary>
        public const string Miss = @"ff5a6e";

        /// <summary>A note whose pitch is unknown ("play the note"): grey, and drawn as a question mark.</summary>
        public const string Unknown = @"8a8a99";

        /// <summary>The grid's two quiet tones, swapped on every line (a rhythm cue, never judgment).</summary>
        public const string PulseA = @"5fc9bf";

        public const string PulseB = @"9c86e6";

        /// <summary>"Wait for me": the pitch is nearly there (under a semitone and a half), said in words as well.</summary>
        public const string WaitNear = @"ffd54f";
    }
}
