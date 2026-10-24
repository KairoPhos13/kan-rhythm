// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Kan.Core.Design;
using Kan.Core.Score;

namespace Kan.Core.Tests
{
    /// <summary>A level nobody designed already has breaks, phrases and a climax.</summary>
    [TestFixture]
    public class KanDesignTest
    {
        /// <summary>Notes every <paramref name="stepMs"/> from <paramref name="fromMs"/> to <paramref name="toMs"/>.</summary>
        private static IEnumerable<ScoreNote> run(double fromMs, double toMs, double stepMs, float intensity = 0.5f)
        {
            for (double t = fromMs; t < toMs; t += stepMs)
                yield return new ScoreNote { StartMs = t, DurationMs = stepMs * 0.8, Midi = 72, Intensity = intensity };
        }

        [Test]
        public void SilencesBecomeIntroBreakAndOutro()
        {
            var notes = run(6000, 30000, 500).Concat(run(40000, 60000, 500)).ToList();
            var design = AutoDesign.Generate(notes, 70000);

            Assert.That(design.Generated, Is.True);
            Assert.That(design.Sections.First().Kind, Is.EqualTo(SectionKind.Intro));
            Assert.That(design.Sections.First().EndMs, Is.EqualTo(6000));

            var rest = design.Sections.Single(s => s.Kind == SectionKind.Break);
            Assert.That(rest.StartMs, Is.EqualTo(29900 + 400), "the last note ends at 29.9 s; its tail rings 400 ms");
            Assert.That(rest.EndMs, Is.EqualTo(40000));

            Assert.That(design.Sections.Last().Kind, Is.EqualTo(SectionKind.Outro));
            Assert.That(design.Sections.Last().EndMs, Is.EqualTo(70000));
        }

        [Test]
        public void SectionsCoverTheLevelWithoutGaps()
        {
            var notes = run(1000, 25000, 400).Concat(run(31000, 52000, 300)).Concat(run(53500, 90000, 450)).ToList();
            var design = AutoDesign.Generate(notes, 95000);

            Assert.That(design.Sections.First().StartMs, Is.EqualTo(0));
            Assert.That(design.Sections.Last().EndMs, Is.EqualTo(95000));

            for (int i = 1; i < design.Sections.Count; i++)
                Assert.That(design.Sections[i].StartMs, Is.EqualTo(design.Sections[i - 1].EndMs), $"gap before section {i}");
        }

        [Test]
        public void ShortSilencesCutPhrasesButNoTinyOnes()
        {
            // 1.5 s silences every 5 s: too close to cut each one (phrases ≥ 9 s).
            var notes = new List<ScoreNote>();
            for (int k = 0; k < 8; k++)
                notes.AddRange(run(k * 5000, k * 5000 + 3500, 500));

            var design = AutoDesign.Generate(notes, 40000);
            var phrases = design.Sections.Where(s => !s.IsRest).ToList();

            Assert.That(phrases.Count, Is.GreaterThan(1));
            Assert.That(phrases.Take(phrases.Count - 1).All(p => p.DurationMs >= AutoDesign.MIN_PHRASE_MS), Is.True);
        }

        [Test]
        public void TheBusiestLoudestPhraseIsTheClimax()
        {
            var notes = run(0, 20000, 600, 0.4f)
                        .Concat(run(25000, 45000, 600, 0.4f))
                        .Concat(run(50000, 70000, 200, 0.9f)) // the chorus: dense and loud
                        .Concat(run(75000, 95000, 600, 0.4f)).ToList();

            var design = AutoDesign.Generate(notes, 96000);
            var climax = design.Sections.Where(s => s.Climax).ToList();

            Assert.That(climax, Has.Count.EqualTo(1));
            Assert.That(climax[0].Kind, Is.EqualTo(SectionKind.Chorus));
            Assert.That(climax[0].Contains(60000), Is.True);
        }

        [Test]
        public void AnEvenLevelHasNoClimax()
        {
            var notes = run(0, 20000, 500).Concat(run(25000, 45000, 500)).Concat(run(50000, 70000, 500)).ToList();
            Assert.That(AutoDesign.Generate(notes, 70000).Sections.Any(s => s.Climax), Is.False);
        }

        [Test]
        public void SplitMergeAndBoundaryKeepTheTimelineWhole()
        {
            var design = new KanDesign
            {
                Sections =
                {
                    new DesignSection { StartMs = 0, EndMs = 10000 },
                    new DesignSection { StartMs = 10000, EndMs = 20000, Climax = true },
                },
            };

            Assert.That(design.SplitAt(15000), Is.True);
            Assert.That(design.Sections, Has.Count.EqualTo(3));
            Assert.That(design.Sections[2].Climax, Is.True, "the second half keeps the settings");
            Assert.That(design.SplitAt(15500), Is.False, "too short");

            design.MoveBoundary(design.Sections[0], 50000);
            Assert.That(design.Sections[0].EndMs, Is.EqualTo(15000 - KanDesign.MIN_SECTION_MS));
            Assert.That(design.Sections[1].StartMs, Is.EqualTo(design.Sections[0].EndMs));

            Assert.That(design.MergeWithNext(design.Sections[1]), Is.True);
            Assert.That(design.Sections, Has.Count.EqualTo(2));
            Assert.That(design.Sections[1].EndMs, Is.EqualTo(20000));
        }

        [Test]
        public void EveryPaletteIsComplete()
        {
            Assert.That(KanPalette.Presets.Select(p => p.Name).Distinct().Count(), Is.EqualTo(KanPalette.Presets.Count), "names are unique");

            foreach (var p in KanPalette.Presets)
            {
                foreach (string hex in new[] { p.Note, p.NoteLight, p.BackgroundDark, p.BackgroundLit, p.Trail, p.Hot, p.Climax })
                    Assert.That(System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9a-f]{6}$"), Is.True, $"{p.Name}: {hex}");

                // The staff's light lines and names need a dark background.
                int r = System.Convert.ToInt32(p.BackgroundDark[..2], 16), g = System.Convert.ToInt32(p.BackgroundDark[2..4], 16), b = System.Convert.ToInt32(p.BackgroundDark[4..], 16);
                Assert.That((r + g + b) / 3, Is.LessThan(64), $"{p.Name}: dark background");
                Assert.That(KanPalette.Get(p.Name), Is.SameAs(p));
            }
        }

        [Test]
        public void PartsAreNumberedWithoutGaps()
        {
            // Owner 2026-10-17: "verse 9, verse 11" — the intro and breaks were counted.
            var design = new KanDesign
            {
                Sections =
                {
                    new DesignSection { StartMs = 0, EndMs = 5000, Kind = SectionKind.Intro },
                    new DesignSection { StartMs = 5000, EndMs = 10000, Kind = SectionKind.Phrase },
                    new DesignSection { StartMs = 10000, EndMs = 15000, Kind = SectionKind.Break },
                    new DesignSection { StartMs = 15000, EndMs = 20000, Kind = SectionKind.Phrase },
                    new DesignSection { StartMs = 20000, EndMs = 25000, Kind = SectionKind.Chorus },
                    new DesignSection { StartMs = 25000, EndMs = 30000, Kind = SectionKind.Phrase, Label = @"Coda" },
                },
            };

            var (playable, names) = DesignParts.Of(design.Sections, new double[] { 6000, 16000, 21000, 26000 });

            Assert.That(playable, Is.EqualTo(new[] { 2, 4, 5, 6 }));
            Assert.That(names[2], Is.EqualTo(@"Phrase 1"));
            Assert.That(names[4], Is.EqualTo(@"Phrase 2"), "the break between is not counted");
            Assert.That(names[5], Is.EqualTo(@"Chorus"), "a kind that comes once is not numbered");
            Assert.That(names[6], Is.EqualTo(@"Coda"), "the author's name as it is");
        }

        [Test]
        public void OldDesignsCallTheirPhrasesPhrases()
        {
            const string v1 = @"{""Version"":1,""Palette"":""ocean"",""Sections"":[{""StartMs"":0,""EndMs"":5000,""Kind"":""Intro""},{""StartMs"":5000,""EndMs"":9000,""Kind"":""Verse""}]}";
            var design = KanDesign.FromJson(v1)!;

            Assert.That(design.Sections[1].Kind, Is.EqualTo(SectionKind.Phrase));
            Assert.That(design.Version, Is.EqualTo(KanDesign.VERSION));
            Assert.That(KanDesign.FromJson(design.ToJson())!.Sections[0].Kind, Is.EqualTo(SectionKind.Intro));
        }

        [Test]
        public void SurvivesTheFile()
        {
            var design = AutoDesign.Generate(run(5000, 30000, 500).ToList(), 40000, @"ocean");
            design.Sections[0].BreakStyle = BreakStyle.Hide;

            var back = KanDesign.FromJson(design.ToJson());

            Assert.That(back, Is.Not.Null);
            Assert.That(back!.ContentKey(), Is.EqualTo(design.ContentKey()));
            Assert.That(back.PaletteAt(1000).Name, Is.EqualTo(@"ocean"));
            Assert.That(KanPalette.Get(@"nope").Name, Is.EqualTo(KanPalette.DEFAULT));
        }
    }
}
