// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Kan.Core.Design;

namespace Kan.Core.Tests
{
    /// <summary>
    /// The colour-blind-safe palettes, checked numerically (issue 18): every
    /// table and every override is simulated through the Machado 2009
    /// matrices and measured in OKLab, so "readable for a colour-blind eye"
    /// is a number, not an impression. The thresholds are the distances the
    /// design achieved (see docs/accessibility-audit.md for the full table):
    /// 0.08 is comfortably above "just told apart" (about 0.02), 0.10 is the
    /// bar for the judgment states, 0.06 the floor for the pairs that are
    /// carried by words or marks as well.
    /// </summary>
    [TestFixture]
    public class KanColourBlindTest
    {
        private const double apart = 0.08;
        private const double states_apart = 0.10;
        private const double labelled_apart = 0.06;
        private const double roles_apart = 0.10;
        private const double roles_typical_apart = 0.09;
        private const double graphic_contrast = 3.0;

        private static readonly KanVision[] visions =
        {
            KanVision.Typical, KanVision.Deuteranopia, KanVision.Protanopia, KanVision.Tritanopia,
        };

        private static readonly KanPaletteMode[] safe_modes =
        {
            KanPaletteMode.DeuteranopiaSafe, KanPaletteMode.ProtanopiaSafe, KanPaletteMode.TritanopiaSafe,
        };

        /// <summary>The closest two colours of a set ever read to one kind of vision.</summary>
        private static double closest(IReadOnlyList<string> colours, KanVision vision)
        {
            double min = double.PositiveInfinity;

            for (int i = 0; i < colours.Count; i++)
                for (int j = i + 1; j < colours.Count; j++)
                    min = Math.Min(min, KanColourVision.Distance(colours[i], colours[j], vision));

            return min;
        }

        // ------------------------------------------------------------------
        // The simulation and the metrics themselves
        // ------------------------------------------------------------------

        [Test]
        public void SimulationLeavesNeutralColoursAlone()
        {
            // A dichromat keeps their greys: the matrices move hues, not lightness.
            foreach (var vision in visions)
            {
                Assert.That(KanColourVision.Distance(@"808080", KanColourVision.Simulate(@"808080", vision), KanVision.Typical),
                    Is.LessThan(0.005), $"grey moved under {vision}");
                Assert.That(KanColourVision.Distance(@"ffffff", KanColourVision.Simulate(@"ffffff", vision), KanVision.Typical),
                    Is.LessThan(0.005), $"white moved under {vision}");
            }
        }

        [Test]
        public void SimulationMatchesKnownValues()
        {
            // A red is what the three deficiencies famously confuse. Known values
            // (Machado 2009 at severity 1.0); a change here changes every result below.
            Assert.That(KanColourVision.Simulate(@"ff0000", KanVision.Typical), Is.EqualTo(@"ff0000"));
            Assert.That(KanColourVision.Simulate(@"ff0000", KanVision.Deuteranopia), Is.EqualTo(@"a39000"));
            Assert.That(KanColourVision.Simulate(@"ff0000", KanVision.Protanopia), Is.EqualTo(@"6d5f00"));
            Assert.That(KanColourVision.Simulate(@"ff0000", KanVision.Tritanopia), Is.EqualTo(@"ff000f"));
        }

        [Test]
        public void ContrastAndDistanceAreWellBehaved()
        {
            Assert.That(KanColourVision.ContrastRatio(@"ffffff", @"000000"), Is.EqualTo(21).Within(0.01));
            Assert.That(KanColourVision.ContrastRatio(@"120e1a", @"120e1a"), Is.EqualTo(1).Within(0.01));
            Assert.That(KanColourVision.Distance(@"1897fd", @"1897fd", KanVision.Deuteranopia), Is.EqualTo(0).Within(1e-9));
            Assert.That(KanColourVision.Distance(@"1897fd", @"ff451d", KanVision.Typical),
                Is.GreaterThan(KanColourVision.Distance(@"1897fd", @"74cbf0", KanVision.Typical)),
                "blue and red should read further apart than blue and sky");
        }

        // ------------------------------------------------------------------
        // The twelve note colours ("colour by note")
        // ------------------------------------------------------------------

        [Test]
        public void EveryNoteColourTableStaysApart()
        {
            foreach (var mode in new[] { KanPaletteMode.AsTheLevel }.Concat(safe_modes))
            {
                var table = KanNoteColours.Table(mode);
                Assert.That(table.Count, Is.EqualTo(12));

                var vision = mode.Vision();
                Assert.That(closest(table, vision), Is.GreaterThanOrEqualTo(apart),
                    $"notes too close in the {mode} table under {vision}");
                Assert.That(closest(table, KanVision.Typical), Is.GreaterThanOrEqualTo(apart),
                    $"notes too close in the {mode} table under typical vision");
            }
        }

        [Test]
        public void NoteColoursKeepClearOfTheStandingCues()
        {
            // The unknown pitch's grey ("?") and the perfect's white sit on the same beads.
            var extras = new[] { KanGradeColours.Unknown, KanGradeColours.Perfect };

            foreach (var mode in new[] { KanPaletteMode.AsTheLevel }.Concat(safe_modes))
            {
                var vision = mode.Vision();
                double min = double.PositiveInfinity;

                foreach (string note in KanNoteColours.Table(mode))
                    foreach (string extra in extras)
                        min = Math.Min(min, KanColourVision.Distance(note, extra, vision));

                Assert.That(min, Is.GreaterThanOrEqualTo(apart), $"a note colour reads as an unknown or a perfect under {vision}");
            }
        }

        // ------------------------------------------------------------------
        // The judgment states
        // ------------------------------------------------------------------

        [Test]
        public void JudgmentStatesStayApartForEveryVision()
        {
            var states = new[]
            {
                KanGradeColours.Perfect, KanGradeColours.Good, KanGradeColours.Meh, KanGradeColours.Miss,
            };

            foreach (var vision in visions)
                Assert.That(closest(states, vision), Is.GreaterThanOrEqualTo(states_apart),
                    $"judgment states too close under {vision}");
        }

        [Test]
        public void ThePairsWordsCarryStillReadApart()
        {
            // Two known weak pairs, each always drawn with a word or a mark of
            // its own ("not heard" vs the unknown's "?"; "that's it" vs
            // "higher/lower" in the wait overlay). They may sit closer than
            // the states, never closer than this.
            var unknown = new[] { KanGradeColours.Miss, KanGradeColours.Unknown };
            var waiting = new[] { KanGradeColours.Good, KanGradeColours.WaitNear, KanGradeColours.Miss };

            foreach (var vision in visions)
            {
                Assert.That(closest(unknown, vision), Is.GreaterThanOrEqualTo(labelled_apart),
                    $"a miss reads as an unknown pitch under {vision}");
                Assert.That(closest(waiting, vision), Is.GreaterThanOrEqualTo(labelled_apart),
                    $"the wait overlay's states read alike under {vision}");
            }
        }

        // ------------------------------------------------------------------
        // The palette override (the player wins over the level)
        // ------------------------------------------------------------------

        [Test]
        public void SafePalettesKeepTheRolesApartForEveryPreset()
        {
            foreach (var mode in safe_modes)
            {
                var vision = mode.Vision();

                foreach (var palette in KanPalette.Presets)
                {
                    var safe = palette.ForMode(mode);
                    var roles = new[] { safe.Note, safe.Trail, safe.Hot, safe.Climax };

                    Assert.That(closest(roles, vision), Is.GreaterThanOrEqualTo(roles_apart),
                        $"{palette.Name}: roles too close under {vision}");
                    Assert.That(closest(roles, KanVision.Typical), Is.GreaterThanOrEqualTo(roles_typical_apart),
                        $"{palette.Name}: roles too close for typical eyes");
                }
            }
        }

        [Test]
        public void SafePalettesKeepTheColoursReadableOnBothBackgrounds()
        {
            foreach (var mode in safe_modes)
            {
                foreach (var palette in KanPalette.Presets)
                {
                    var safe = palette.ForMode(mode);

                    foreach (string foreground in new[] { safe.Note, safe.Trail, safe.Hot })
                        foreach (string background in new[] { palette.BackgroundDark, palette.BackgroundLit })
                            Assert.That(KanColourVision.ContrastRatio(foreground, background), Is.GreaterThanOrEqualTo(graphic_contrast),
                                $"{palette.Name}: {foreground} on {background} under {mode}");
                }
            }
        }

        [Test]
        public void SafePalettesLeaveTheDesignAlone()
        {
            foreach (var mode in safe_modes)
            {
                foreach (var palette in KanPalette.Presets)
                {
                    var safe = palette.ForMode(mode);

                    Assert.That(safe.Name, Is.EqualTo(palette.Name));
                    Assert.That(safe.Title, Is.EqualTo(palette.Title));
                    Assert.That(safe.BackgroundDark, Is.EqualTo(palette.BackgroundDark), $"{palette.Name}: the background's dark end moved");
                    Assert.That(safe.BackgroundLit, Is.EqualTo(palette.BackgroundLit), $"{palette.Name}: the background's lit end moved");

                    // The bead keeps its lighter top, and the mood survives in lightness:
                    // the level's own lightness inside its role's band (holy's pale
                    // notes darken into the readable band, by design).
                    Assert.That(KanColourVision.ToOkLch(safe.Note).L,
                        Is.InRange(0.59, 0.76), // 0.60-0.75 as the hex round-trip leaves it
                        $"{palette.Name}: the note left its role's lightness band");
                    Assert.That(KanColourVision.ToOkLch(safe.NoteLight).L,
                        Is.GreaterThanOrEqualTo(KanColourVision.ToOkLch(safe.Note).L + 0.03),
                        $"{palette.Name}: the note lost its lighter top");
                    Assert.That(Math.Abs(KanColourVision.ToOkLch(safe.Note).L - KanColourVision.ToOkLch(palette.Note).L),
                        Is.LessThanOrEqualTo(0.35),
                        $"{palette.Name}: the note's lightness moved further than its band allows");
                }

                // The override is cached: the same level always draws the same way.
                Assert.That(KanPalette.Presets[0].ForMode(mode), Is.SameAs(KanPalette.Presets[0].ForMode(mode)));
            }

            Assert.That(KanPalette.Presets[0].ForMode(KanPaletteMode.AsTheLevel), Is.SameAs(KanPalette.Presets[0]),
                "as the level must not touch the palette");
        }
    }
}
