// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Screens.Edit;
using Kan.Core.Tracks;
using Kan.Forge.Editor.Compose;
using Kan.Ruleset;

namespace Kan.Forge.Tests
{
    /// <summary>
    /// The tempo grid of a repair step (2026-11-12 audit B11): the step's
    /// history outlives the compose screen, so the grid lives on the
    /// editor's beatmap and restores for whoever undoes the step (the track
    /// tab included). Before the fix this file does not compile: the grid
    /// was a private part of the compose screen and quietly refused to
    /// restore once that screen was gone.
    /// </summary>
    [TestFixture]
    public class EditorGridTest
    {
        [Test]
        public void TheGridRestoresWhoeverAsks()
        {
            var beatmap = new EditorBeatmap(new Beatmap { BeatmapInfo = new BeatmapInfo { Ruleset = new KanRuleset().RulesetInfo } });
            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            beatmap.ControlPointInfo.Add(2000, new TimingControlPoint { BeatLength = 400 });

            var grid = new EditorGrid(beatmap);
            object state = grid.Capture();

            grid.Apply(TimelineEdit.InsertSilence(1000, 1000));
            Assert.That(grid.Points.Select(p => p.TimeMs), Is.EqualTo(new[] { 0.0, 3000 }),
                        "the silence pushes the next tempo change out");

            grid.Restore(state);
            Assert.That(grid.Points.Select(p => p.TimeMs), Is.EqualTo(new[] { 0.0, 2000.0 }),
                        "undo brings the grid back, with no screen involved");
            Assert.That(grid.Points.Select(p => p.BeatMs), Is.EqualTo(new[] { 500.0, 400.0 }));
        }
    }
}
