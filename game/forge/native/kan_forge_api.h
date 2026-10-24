/* kan_forge_api — thin C-ABI bridge between the KAN client (C#) and libkan.
 *
 * Part of the level-creation pipeline (kan!forge): audio fetch, source
 * separation, offline transcription. libkan describes, the game judges —
 * this bridge only forwards measurements (docs/architecture/
 * note-description-stream.md) and progress (a method, never console
 * scraping). ABI rules: fixed-size POD structs, plain C strings, no
 * allocations across the boundary (the caller owns all buffers).
 *
 * Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
 */
#ifndef KAN_FORGE_API_H
#define KAN_FORGE_API_H

#include <stdint.h>

#if defined(_WIN32)
#define KAN_FORGE_API __declspec(dllexport)
#else
#define KAN_FORGE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Status codes — error states are first-class (human-readable message is
 * written to the optional err buffer in every failure case). */
typedef enum kan_forge_status {
    KAN_FORGE_OK = 0,
    KAN_FORGE_ERR_ARGS = 1,       /* bad arguments */
    KAN_FORGE_ERR_FETCH = 2,      /* no downloader available / download failed */
    KAN_FORGE_ERR_SEPARATOR = 3,  /* bs_roformer-cli or GGUF model missing */
    KAN_FORGE_ERR_DECODE = 4,     /* missing file / unsupported audio codec */
    KAN_FORGE_ERR_TRANSCRIBE = 5, /* transcription failed */
    KAN_FORGE_ERR_CAPACITY = 6    /* output buffer too small (see *out_count) */
} kan_forge_status;

/* One transcribed note: measurements only, no verdicts. Booleans are
 * int32_t for ABI stability. Mirrors kan::TranscribedNote. */
typedef struct kan_forge_note {
    float onset_ms;
    float end_ms;     /* measured end (raw) */
    float dur_ms;     /* bias-corrected duration (chart-ready) */
    int32_t midi;     /* trajectory center, rounded (-1 = unresolved) */
    float cents;
    float dyn_db;
    float vib_rate;
    float vib_depth;
    int32_t end_faded;  /* end inferred (decay/pedal), not a release */
    int32_t transition; /* legato boundary (no strike) */
} kan_forge_note;

/* Progress contract (libkan set_progress): invoked with phase
 * "download" | "separate" | "transcribe" and percent 0..100. The callback
 * may run on a worker thread — dispatch to the UI is the caller's job.
 * One active callback at a time (serialized by the caller). */
typedef void (*kan_forge_progress_fn)(void* user, const char* phase, int32_t percent);

KAN_FORGE_API void kan_forge_set_progress(kan_forge_progress_fn fn, void* user);

/* Fetch audio from a URL (yt-dlp/youtube-dl) to a WAV file. */
KAN_FORGE_API kan_forge_status kan_forge_fetch_audio(const char* url,
                                                     const char* out_wav,
                                                     char* err, int32_t err_cap);

/* Source separation into <out_dir>/roformer/{bass,drums,guitar,other,piano,vocals}.wav. */
KAN_FORGE_API kan_forge_status kan_forge_separate_stems(const char* audio_path,
                                                        const char* out_dir,
                                                        char* err, int32_t err_cap);

/* Offline transcription of one audio file into caller-owned note buffer.
 * `profile` is one of "keyboard", "wind_bowed", "plucked" (libkan
 * InstrumentProfile families). On success *out_count receives the number of
 * notes written (capacity error returns KAN_FORGE_ERR_CAPACITY with the
 * required count). A zero-note transcription is OK — the caller decides how
 * to present it ("no resolved notes" is a state, not a crash). */
KAN_FORGE_API kan_forge_status kan_forge_transcribe_file(const char* audio_path,
                                                         const char* profile,
                                                         kan_forge_note* out,
                                                         int32_t cap,
                                                         int32_t* out_count,
                                                         char* err, int32_t err_cap);

/* The same, choosing the transcriber: "describer" (libkan's note
 * describer, what kan_forge_transcribe_file runs) or "neural" (Basic
 * Pitch, libkan basic_pitch.hpp; owner 2026-10-15). Neural notes carry
 * onset, end, pitch and cents; dyn_db, vibrato, end_faded and transition
 * are not measured (defaults). Unknown engine: KAN_FORGE_ERR_ARGS. */
KAN_FORGE_API kan_forge_status kan_forge_transcribe_file_with(const char* audio_path,
                                                              const char* profile,
                                                              const char* engine,
                                                              kan_forge_note* out,
                                                              int32_t cap,
                                                              int32_t* out_count,
                                                              char* err, int32_t err_cap);

/* Library version string (for diagnostics). */
KAN_FORGE_API const char* kan_forge_version(void);

/* Environment probes (1 = available): used by the UI to disable actions
 * with a human-readable hint instead of failing halfway. */
KAN_FORGE_API int32_t kan_forge_has_separator(void);
KAN_FORGE_API int32_t kan_forge_has_downloader(void);

/* Render one playable track from the editor's track set: decode each input
 * to mono 48 kHz, apply its gain (mute = gain 0 on the caller side), sum and
 * write a single WAV. Output length covers the longest input; peak
 * protection scales the mix down instead of clipping. This is the "render a
 * single track for playback" step of the editor flow. */
KAN_FORGE_API kan_forge_status kan_forge_mix_tracks(const char* const* paths,
                                                    const float* gains,
                                                    int32_t count,
                                                    const char* out_wav,
                                                    char* err, int32_t err_cap);

/* Same as kan_forge_mix_tracks, with each input placed on a shared timeline
 * (owner 2026-10-14: tracks can be moved in time). offsets_ms[i] >= 0 is
 * where input i starts; NULL offsets = every input at 0. The output covers
 * max(offset + length). */
KAN_FORGE_API kan_forge_status kan_forge_mix_tracks_at(const char* const* paths,
                                                       const float* gains,
                                                       const double* offsets_ms,
                                                       int32_t count,
                                                       const char* out_wav,
                                                       char* err, int32_t err_cap);

/* Track editor (non-destructive: the source is never modified — each edit
 * produces a NEW track the caller adds to its list under a user-chosen
 * name). Ranges are milliseconds in source time.
 *   KEEP: keep [start, end), silence the rest (original timing preserved)
 *   MUTE: silence [start, end), keep the rest (original timing preserved)
 *   TRIM: crop to [start, end) — tight file, original offset is lost */
typedef enum kan_forge_edit_mode {
    KAN_FORGE_EDIT_KEEP = 0,
    KAN_FORGE_EDIT_MUTE = 1,
    KAN_FORGE_EDIT_TRIM = 2
} kan_forge_edit_mode;

KAN_FORGE_API kan_forge_status kan_forge_edit_track(const char* audio_path,
                                                    float start_ms, float end_ms,
                                                    int32_t mode,
                                                    const char* out_wav,
                                                    char* err, int32_t err_cap);

/* Content analysis for the track list: one decode pass produces the data
 * behind both the waveform preview and the "no meaningful audio" marking.
 * - peaks: bucket_count peak magnitudes (0..1) spread evenly over the file
 * - out_peak / out_rms: whole-file peak and RMS (0..1)
 * A bucket_count of 0 (or peaks == NULL) skips the bucket pass. */
KAN_FORGE_API kan_forge_status kan_forge_analyze_track(const char* audio_path,
                                                       float* peaks, int32_t bucket_count,
                                                       float* out_peak, float* out_rms,
                                                       char* err, int32_t err_cap);

/* Programme loudness (ITU-R BS.1770-4, libkan/loudness.hpp) of a file,
 * decoded to mono 48 kHz: out_lufs (-INFINITY for silence) and the sample
 * peak (0..1). The editor shows how loud a track sits against the game's
 * reference. */
KAN_FORGE_API kan_forge_status kan_forge_measure_loudness(const char* audio_path,
                                                          float* out_lufs, float* out_peak,
                                                          char* err, int32_t err_cap);

/* Level a file to target_lufs (mono 48 kHz WAV out), its peak held at or
 * under ceiling_dbfs: a quiet file with sharp peaks comes up only as far as
 * they allow. out_before / out_after: its loudness before and after
 * (either may be NULL). Silence is written unchanged. */
KAN_FORGE_API kan_forge_status kan_forge_normalize_loudness(const char* audio_path, const char* out_wav,
                                                            float target_lufs, float ceiling_dbfs,
                                                            float* out_before, float* out_after,
                                                            char* err, int32_t err_cap);

/* ---- Live description (gameplay) ---------------------------------------
 * The same libkan describer as kan_forge_transcribe_file, fed block by block
 * as audio arrives (microphone or a file played in real time), so live and
 * offline give the same notes. Measurements only: the game pairs them with
 * the score and judges (libkan describes, the game judges).
 *
 * Threading: one session is driven from ONE consumer thread (never the
 * audio callback: process does the describer's measurement work). open and
 * close are setup calls; process does not allocate. */
typedef struct kan_forge_live kan_forge_live;

typedef enum kan_forge_live_kind {
    KAN_FORGE_LIVE_HIT = 0,   /* a note started: onset_ms */
    KAN_FORGE_LIVE_PITCH = 1, /* the note's pitch: midi + cents, stage */
    KAN_FORGE_LIVE_CLOSE = 2, /* the note ended: end_ms */
    KAN_FORGE_LIVE_FRAME = 3, /* continuous pitch reading every 10 ms:
                                 midi (fractional, 0 = no pitch) + level */
    KAN_FORGE_LIVE_CANDIDATE = 4 /* an expected note measured at its attack
                                    (see kan_forge_live_expect): midi = the
                                    expectation, cents = refined deviation,
                                    presence, similarity, flags */
} kan_forge_live_kind;

typedef struct kan_forge_live_event {
    uint32_t note_id;  /* the note this fact is about (0 for frames) */
    int32_t kind;      /* kan_forge_live_kind */
    int32_t stage;     /* pitch: 0 provisional, 1 final, 2 unresolved */
    float t_ms;        /* stream time of the fact (ms since the first sample) */
    float onset_ms;    /* hit: the onset */
    float midi;        /* pitch: note number; frame: fractional pitch */
    float cents;       /* pitch: deviation from midi */
    float end_ms;      /* close: the end */
    float level;       /* frame: RMS 0..1 */
    float presence;    /* candidate: how much of the energy the expected note explains, 0..1 */
    float similarity;  /* candidate: fit of the whole expectation to the sound, 0..1 */
    uint32_t flags;    /* libkan NoteFlags (audio facts, never verdicts) */
    uint32_t group;    /* hit/candidate: the attack (strike group) the fact belongs to —
                          links a candidate measurement to its attack even when
                          libkan files it under no record (monophonic deferral) */
} kan_forge_live_event;

/* sample_rate: the rate of the samples passed to process. NULL on failure. */
KAN_FORGE_API kan_forge_live* kan_forge_live_open(const char* profile, float sample_rate);

/* Feed n mono samples; writes up to cap events and returns how many. Events
 * beyond cap are dropped (pass >= 256 slots per 4800 samples). */
KAN_FORGE_API int32_t kan_forge_live_process(kan_forge_live* live, const float* samples,
                                             int32_t n, kan_forge_live_event* out,
                                             int32_t cap);

/* Expectation for the NEXT attack (libkan candidate mode): the note(s) the
 * score expects. The describer then measures those pitches at the attack
 * (CANDIDATE facts, and the record's pitch refined around them) instead of
 * searching blind. A hypothesis, never a verdict: a different note played
 * shows as low presence/similarity or an unresolved pitch. Returns 1 when
 * set, 0 when a previous expectation is still waiting for its attack. */
KAN_FORGE_API int32_t kan_forge_live_expect(kan_forge_live* live, const float* midis, int32_t count);

KAN_FORGE_API void kan_forge_live_close(kan_forge_live* live);

#ifdef __cplusplus
}
#endif

#endif /* KAN_FORGE_API_H */
