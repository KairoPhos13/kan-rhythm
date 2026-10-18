// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Kan.Forge.Score;

namespace Kan.Forge.Design
{
    /// <summary>
    /// A design from the notes alone (owner 2026-10-15: a level nobody
    /// designed — say, transcribed by AI — must already look right; the
    /// design tab only improves it):
    ///
    /// - a silence of <see cref="BREAK_MS"/> or more is a <b>break</b> (one
    ///   before the first note is the intro, one after the last the outro);
    /// - the music between breaks is cut into <b>phrases</b> at shorter
    ///   silences, none shorter than <see cref="MIN_PHRASE_MS"/>;
    /// - the busiest, loudest phrases are the <b>climax</b> (chorus): notes
    ///   per second times mean intensity, the top quarter within 90 % of the
    ///   best — only when the level has phrases to compare.
    /// </summary>
    public static class AutoDesign
    {
        /// <summary>A silence this long is a break.</summary>
        public const double BREAK_MS = 3500;

        /// <summary>Phrases are cut at silences this long…</summary>
        public const double PHRASE_GAP_MS = 1200;

        /// <summary>…but none is shorter than this.</summary>
        public const double MIN_PHRASE_MS = 9000;

        /// <summary>A break starts this long after the last note ends (its tail still rings).</summary>
        private const double ring_ms = 400;

        public static KanDesign Generate(IReadOnlyCollection<ScoreNote> notes, double levelLengthMs, string palette = KanPalette.DEFAULT)
        {
            var design = new KanDesign { Palette = palette, Generated = true };
            var sorted = notes.OrderBy(n => n.StartMs).ToList();

            if (sorted.Count == 0)
            {
                if (levelLengthMs > 0)
                    design.Sections.Add(new DesignSection { StartMs = 0, EndMs = levelLengthMs, Kind = SectionKind.Break });
                return design;
            }

            double lastEnd = sorted.Max(n => n.EndMs);
            double end = Math.Max(levelLengthMs, lastEnd + 1);

            // The music between breaks: runs of notes with no silence of BREAK_MS.
            var runs = new List<(double Start, double End, List<ScoreNote> Notes)>();
            var current = new List<ScoreNote> { sorted[0] };
            double runStart = sorted[0].StartMs, runEnd = sorted[0].EndMs;

            for (int i = 1; i < sorted.Count; i++)
            {
                var n = sorted[i];

                if (n.StartMs - runEnd >= BREAK_MS)
                {
                    runs.Add((runStart, runEnd, current));
                    current = new List<ScoreNote>();
                    runStart = n.StartMs;
                }

                current.Add(n);
                runEnd = Math.Max(runEnd, n.EndMs);
            }

            runs.Add((runStart, runEnd, current));

            // Intro: the silence before the first note (a rest with its countdown).
            if (runs[0].Start >= BREAK_MS)
                design.Sections.Add(new DesignSection { StartMs = 0, EndMs = runs[0].Start, Kind = SectionKind.Intro, BreakStyle = BreakStyle.Countdown });

            var phrases = new List<DesignSection>();

            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                double from = design.Sections.Count == 0 ? 0 : design.Sections[^1].EndMs;

                foreach (var phrase in splitIntoPhrases(run.Notes, from, run.End + ring_ms))
                {
                    design.Sections.Add(phrase);
                    phrases.Add(phrase);
                }

                if (r + 1 < runs.Count)
                    design.Sections.Add(new DesignSection { StartMs = run.End + ring_ms, EndMs = runs[r + 1].Start, Kind = SectionKind.Break });
            }

            // Outro: what is left after the last note (a long tail is a rest).
            double tailStart = design.Sections[^1].EndMs;

            if (end - tailStart >= BREAK_MS)
                design.Sections.Add(new DesignSection { StartMs = tailStart, EndMs = end, Kind = SectionKind.Outro, BreakStyle = BreakStyle.Visualizer });
            else
                design.Sections[^1].EndMs = end;

            markClimax(phrases, sorted);
            return design;
        }

        /// <summary>
        /// A design kept in step with its notes: when its sections are still
        /// automatic (<see cref="KanDesign.Generated"/>) they are generated
        /// again — the notes may have changed — keeping the level's settings
        /// (palette, effects). A design edited by hand is returned as it is.
        /// </summary>
        public static KanDesign Refresh(KanDesign design, IReadOnlyCollection<ScoreNote> notes, double levelLengthMs)
        {
            if (!design.Generated) return design;

            var fresh = Generate(notes, levelLengthMs, design.Palette);
            fresh.Sparkle = design.Sparkle;
            fresh.Glow = design.Glow;
            fresh.Picture = design.Picture;
            fresh.ShowSectionNames = design.ShowSectionNames;
            fresh.SavedAtUtc = design.SavedAtUtc;
            return fresh;
        }

        /// <summary>Cut a run of notes at its longer silences, keeping every phrase at least <see cref="MIN_PHRASE_MS"/>.</summary>
        private static IEnumerable<DesignSection> splitIntoPhrases(List<ScoreNote> notes, double from, double to)
        {
            var cuts = new List<double>();
            double reach = notes[0].EndMs, lastCut = from;

            for (int i = 1; i < notes.Count; i++)
            {
                var n = notes[i];

                if (n.StartMs - reach >= PHRASE_GAP_MS && n.StartMs - lastCut >= MIN_PHRASE_MS && to - n.StartMs >= MIN_PHRASE_MS)
                {
                    cuts.Add(n.StartMs);
                    lastCut = n.StartMs;
                }

                reach = Math.Max(reach, n.EndMs);
            }

            double start = from;

            foreach (double cut in cuts)
            {
                yield return new DesignSection { StartMs = start, EndMs = cut, Kind = SectionKind.Phrase };
                start = cut;
            }

            yield return new DesignSection { StartMs = start, EndMs = to, Kind = SectionKind.Phrase };
        }

        private static void markClimax(List<DesignSection> phrases, List<ScoreNote> notes)
        {
            if (phrases.Count < 2) return;

            var energy = phrases.Select(p =>
            {
                var inside = notes.Where(n => p.Contains(n.StartMs)).ToList();
                if (inside.Count == 0 || p.DurationMs <= 0) return 0.0;

                return inside.Count / (p.DurationMs / 1000) * inside.Average(n => n.Intensity);
            }).ToList();

            double best = energy.Max();
            if (best <= 0) return;

            int quota = Math.Max(1, phrases.Count / 4);

            // Two phrases alike are not a climax: the best must stand out from the rest.
            double median = energy.OrderBy(e => e).ElementAt(energy.Count / 2);
            if (best < median * 1.15) return;

            foreach (int i in Enumerable.Range(0, phrases.Count).OrderByDescending(i => energy[i]).Take(quota))
            {
                if (energy[i] < best * 0.9) break;

                phrases[i].Climax = true;
                phrases[i].Kind = SectionKind.Chorus;
            }
        }
    }
}
