// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System.Collections.Generic;

namespace Kan.Forge.Transcription
{
    /// <summary>
    /// One transcribed note as an *editable draft* — the weak-label rule of
    /// chart-transcription-evaluation.md: measurement output is a proposal,
    /// the human confirms. Measurement fields mirror
    /// <c>kan::TranscribedNote</c> (libkan describes, the game judges); the
    /// nullable "comparison metadata" fields carry facts the judge will use
    /// later but which the offline transcriber does not produce yet —
    /// <see langword="null"/> means "unjudged / unmeasured", never "wrong"
    /// (I2 semantics, docs/architecture/measurement-invariants.md).
    /// </summary>
    public class KanDraftNote
    {
        /// <summary>Onset anchor (audio attack), ms. Fixed at measurement.</summary>
        public float OnsetMs;

        /// <summary>Measured end (raw), ms.</summary>
        public float EndMs;

        /// <summary>Bias-corrected duration, ms (chart-ready).</summary>
        public float DurMs;

        /// <summary>Trajectory pitch center, MIDI note number (-1 = unresolved).</summary>
        public int Midi = -1;

        /// <summary>Pitch deviation from the rounded center, cents.</summary>
        public float Cents;

        public float DynDb = -120.0f;

        public float VibRate;

        public float VibDepth;

        /// <summary>End inferred (decay/pedal), not a release.</summary>
        public bool EndFaded;

        /// <summary>Legato boundary (no strike).</summary>
        public bool Transition;

        /// <summary>
        /// The note was truncated/split by a timeline edit (v1 semantics of
        /// TranscriptionDraft) — shown as a human-fixable fragment, not a
        /// clean measurement.
        /// </summary>
        public bool FragmentedByEdit;

        // ---- comparison metadata (inspectable; not produced by the offline
        // transcriber yet — null = unjudged) ----

        /// <summary>Pitch trajectory saturated (I2): null = unjudged.</summary>
        public bool? PitchSaturated;

        /// <summary>Content presence 0..1 for the note's pitch: null = unjudged.</summary>
        public float? Presence;

        /// <summary>Template similarity for extended techniques: null = unjudged.</summary>
        public float? Similarity;

        // ---- human confirmation (weak-label rule) ----

        public bool PitchEdited;

        public bool TimingEdited;

        public bool ExpressionEdited;

        public KanDraftNote Clone() => (KanDraftNote)MemberwiseClone();
    }

    /// <summary>Fact state per measurement (I2: unmeasurable is unjudged).</summary>
    public enum KanFactState
    {
        Measured,
        Unjudged,
        HumanEdited,
    }

    public static class KanDraftNoteExtensions
    {
        public static KanFactState PitchState(this KanDraftNote n)
            => n.PitchEdited ? KanFactState.HumanEdited
                : n.Midi >= 0 ? KanFactState.Measured : KanFactState.Unjudged;

        public static KanFactState TimingState(this KanDraftNote n)
            => n.TimingEdited ? KanFactState.HumanEdited : KanFactState.Measured;

        public static KanFactState ExpressionState(this KanDraftNote n)
            => n.ExpressionEdited ? KanFactState.HumanEdited
                : n.VibRate > 0 || n.DynDb > -120.0f ? KanFactState.Measured : KanFactState.Unjudged;
    }
}
