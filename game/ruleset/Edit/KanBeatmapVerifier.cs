// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks;
using osu.Game.Rulesets.Edit.Checks.Components;
using Kan.Core.Play;
using Kan.Core.Score;
using Kan.Core.Tracks;
using Kan.Forge.Editor;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.UI;

namespace Kan.Ruleset.Edit
{
    /// <summary>
    /// The verify tab as kan!'s checklist before a level is shared (owner
    /// 2026-10-25: osu!'s checks counted no notes — kan!'s live in the score,
    /// not the beatmap — and reported none played and all the audio unused).
    /// It reads the score and the mix being edited, else the level's files.
    /// </summary>
    public class KanBeatmapVerifier : IBeatmapVerifier
    {
        private readonly List<ICheck> checks = new List<ICheck>
        {
            new CheckScore(),
            new CheckRange(),
            new CheckMonophonic(),
            new CheckGuessedPitch(),
            new CheckPastTheAudio(),
            new CheckLongOutro(),
            new CheckLevelAudio(),
            new CheckTempoGrid(),
        };

        public IEnumerable<Issue> Run(BeatmapVerifierContext context) => checks.SelectMany(c => c.Run(context));

        /// <summary>
        /// The general checks that count the beatmap's own hit objects (none in a
        /// kan! level): replaced by these ones, which read the score.
        /// </summary>
        public static bool AppliesGeneralCheck(ICheck check)
            => check is not (CheckDrainLength or CheckUnusedAudioAtEnd or CheckUnsnappedObjects or CheckZeroLengthObjects or CheckBreaks or CheckMutedObjects);

        /// <summary>The level's score: the one being edited when this level is open, else the level's own.</summary>
        internal static KanScore? ScoreOf(BeatmapVerifierContext context)
        {
            var set = context.CurrentDifficulty.Working.BeatmapInfo.BeatmapSet as BeatmapSetInfo;
            if (set == null) return null;

            return ScoreSession.LevelKey == set.ID.ToString(@"N") ? ScoreSession.Score : KanLevelFiles.LoadScore(set);
        }

        internal static double AudioLengthOf(BeatmapVerifierContext context)
        {
            var working = context.CurrentDifficulty.Working;
            return working.TrackLoaded ? working.Track.Length : 0;
        }

        private static string instrumentName(string instrument) => instrument switch
        {
            @"wind_bowed" => @"a wind or bowed instrument",
            @"plucked" => @"a plucked instrument",
            _ => $"{("aeiou".Contains(instrument[0]) ? "an" : "a")} {instrument}",
        };

        // ------------------------------------------------------------------

        private class CheckScore : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, @"The level has notes");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateNoNotes(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                if (ScoreOf(context) is not { Notes.Count: > 0 })
                    yield return new TemplateNoNotes(this).Create();
            }

            private class TemplateNoNotes : IssueTemplate
            {
                public TemplateNoNotes(ICheck check)
                    : base(check, IssueType.Problem, @"The level has no notes yet: write them in compose, or start from a transcription.")
                {
                }

                public Issue Create() => new Issue(this);
            }
        }

        private class CheckRange : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, @"Notes the instrument plays");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateOutside(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                if (ScoreOf(context) is not { } score) yield break;

                var (lowest, highest) = RangeFit.DefaultRange(score.Instrument);

                foreach (var n in score.Notes.Where(n => !n.PitchUnknown && (n.Midi < lowest || n.Midi > highest)))
                {
                    yield return new TemplateOutside(this).Create(n.StartMs, KanNotation.Label(n.Midi), n.Midi < lowest ? @"below" : @"above",
                        instrumentName(score.Instrument), KanNotation.Label(lowest), KanNotation.Label(highest));
                }
            }

            private class TemplateOutside : IssueTemplate
            {
                public TemplateOutside(ICheck check)
                    : base(check, IssueType.Warning, @"{0} is {1} the usual range of {2} ({3}–{4}): not every player reaches it.")
                {
                }

                public Issue Create(double time, string note, string side, string instrument, string low, string high)
                    => new Issue(time, this, note, side, instrument, low, high);
            }
        }

        /// <summary>
        /// One note at a time on an instrument that plays one at a time (owner
        /// 2026-10-25: "an instrument meant to be monophonic, yet two pitches at
        /// once in the score").
        /// </summary>
        private class CheckMonophonic : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, @"One note at a time on a one-note instrument");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateTogether(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                if (ScoreOf(context) is not { } score || !KanScore.IsMonophonic(score.Instrument)) yield break;

                foreach (var (sounding, starting) in score.SoundingTogether())
                    yield return new TemplateTogether(this).Create(starting.StartMs, KanNotation.Label(sounding.Midi), KanNotation.Label(starting.Midi), instrumentName(score.Instrument));
            }

            private class TemplateTogether : IssueTemplate
            {
                public TemplateTogether(ICheck check)
                    : base(check, IssueType.Warning, @"{0} and {1} sound at once, but {2} plays one note at a time.")
                {
                }

                public Issue Create(double time, string first, string second, string instrument)
                    => new Issue(time, this, first, second, instrument);
            }
        }

        private class CheckGuessedPitch : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, @"Notes with a known pitch");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateGuessed(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                if (ScoreOf(context) is not { } score) yield break;

                foreach (var n in score.Notes.Where(n => n.PitchUnknown))
                    yield return new TemplateGuessed(this).Create(n.StartMs, KanNotation.Label(n.Midi));
            }

            private class TemplateGuessed : IssueTemplate
            {
                public TemplateGuessed(ICheck check)
                    : base(check, IssueType.Warning, @"This note's pitch ({0}) is only a guess: the transcription could not hear it. Set it in compose.")
                {
                }

                public Issue Create(double time, string note) => new Issue(time, this, note);
            }
        }

        private class CheckPastTheAudio : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, @"Notes within the level audio");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplatePast(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                double length = AudioLengthOf(context);
                if (length <= 0 || ScoreOf(context) is not { } score) yield break;

                foreach (var n in score.Notes.Where(n => n.StartMs >= length))
                    yield return new TemplatePast(this).Create(n.StartMs, KanNotation.Label(n.Midi));
            }

            private class TemplatePast : IssueTemplate
            {
                public TemplatePast(ICheck check)
                    : base(check, IssueType.Problem, @"{0} starts after the level audio ends: it can never be played.")
                {
                }

                public Issue Create(double time, string note) => new Issue(time, this, note);
            }
        }

        /// <summary>osu!'s "unused audio at the end", from the score's last note.</summary>
        private class CheckLongOutro : ICheck
        {
            private const double share = 0.2, at_least_ms = 10000;

            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, @"Not much audio after the last note");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateOutro(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                double length = AudioLengthOf(context);
                if (length <= 0 || ScoreOf(context) is not { Notes.Count: > 0 } score) yield break;

                double after = length - score.Notes.Max(n => n.EndMs);
                if (after > at_least_ms && after > share * length)
                    yield return new TemplateOutro(this).Create(score.Notes.Max(n => n.EndMs), after / 1000);
            }

            private class TemplateOutro : IssueTemplate
            {
                public TemplateOutro(ICheck check)
                    : base(check, IssueType.Warning, @"The audio goes on {0:0} s after the last note. Keep an outro that adds to the song, or set the level audio's end in the track tab.")
                {
                }

                public Issue Create(double time, double seconds) => new Issue(time, this, seconds);
            }
        }

        private class CheckLevelAudio : ICheck
        {
            /// <summary>Further than this (dB) from its target, a level stands out.</summary>
            private const double off_db = 1.5;

            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Audio, @"The level audio is the mix, at its loudness");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateStale(this), new TemplateLoudness(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                // Only while the level is open in the editor: the mix lives in its work folder.
                var set = context.CurrentDifficulty.Working.BeatmapInfo.BeatmapSet as BeatmapSetInfo;
                if (set == null || ForgeTrackSession.LevelKey != set.ID.ToString(@"N") || !ForgeTrackSession.Tracks.Companions.Any()) yield break;

                if (!BeatmapAudio.IsUpToDate(ForgeTrackSession.Tracks))
                {
                    yield return new TemplateStale(this).Create();
                    yield break;
                }

                if (ForgeTrackSession.LevelAudioLufs is double lufs)
                {
                    double target = LevelLoudness.TargetLufs(ForgeTrackSession.Tracks.LoudnessOffsetDb);
                    if (Math.Abs(lufs - target) > off_db)
                        yield return new TemplateLoudness(this).Create(lufs, lufs - target);
                }
            }

            private class TemplateStale : IssueTemplate
            {
                public TemplateStale(ICheck check)
                    : base(check, IssueType.Warning, @"The level audio is older than the mix: leave the track tab, or save, to render it.")
                {
                }

                public Issue Create() => new Issue(this);
            }

            private class TemplateLoudness : IssueTemplate
            {
                public TemplateLoudness(ICheck check)
                    : base(check, IssueType.Warning, @"The level audio is at {0:0.0} LUFS, {1:+0.0;-0.0} dB from its target: its peaks kept it from levelling. It will sound louder or softer than other levels.")
                {
                }

                public Issue Create(double lufs, double off) => new Issue(this, lufs, off);
            }
        }

        private class CheckTempoGrid : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Timing, @"A tempo grid");

            public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[] { new TemplateNoGrid(this) };

            public IEnumerable<Issue> Run(BeatmapVerifierContext context)
            {
                if (!context.CurrentDifficulty.Playable.ControlPointInfo.TimingPoints.Any())
                    yield return new TemplateNoGrid(this).Create();
            }

            private class TemplateNoGrid : IssueTemplate
            {
                public TemplateNoGrid(ICheck check)
                    : base(check, IssueType.Warning, @"The level has no tempo: compose has no beats to snap notes to, and the staff no bars. Set the BPM in compose or the timing tab.")
                {
                }

                public Issue Create() => new Issue(this);
            }
        }
    }
}
