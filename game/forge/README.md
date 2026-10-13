# game/forge/ — kan!forge level-creation pipeline (KAN editor × libkan)

The creation half of kan!forge: audio fetch, source separation, offline
transcription and the transcription draft with temporal-edit propagation —
feeding the KAN editor. Implementation notes of record:
[../docs/editor-pipeline.md](../docs/editor-pipeline.md) (dispatch decision,
edit semantics, error states, remaining slices). Prompt of record:
`docs/implementation/kan-editor-agent-prompt.md` (monorepo root).

**Status:** slice 1 complete and tested (2026-10-13) — entry-window pipeline
with live progress, C-ABI bridge, draft model + edit propagation. The NLE
timeline UI and the transcription editor UI are next (`../TODO.md`).

## Layout

| Path | Content |
|---|---|
| `native/` | `libkan_forge_native` — thin C-ABI adapter over libkan (`kan_forge_api.h` is the contract) + offline self-test; standalone CMake |
| `Native/` | P/Invoke bindings (POD structs, status codes, progress delegate) |
| `Pipeline/` | `ForgeJobRunner` (serialized background jobs, progress marshalled to the UI thread) + the fetch/separate/transcribe jobs |
| `Transcription/` | `KanDraftNote` (measurements + "unjudged" semantics) and `TranscriptionDraft` (cut/move/delete → shift/split/remove) |
| `Editor/` | `KanCreationSection` — the entry-window panel (buttons + progress bars), registered by the kan! ruleset |
| `tests/` | NUnit: propagation semantics + P/Invoke smoke tests |

## Ground rules

- **libkan describes, the game judges** — this component forwards
  measurements and never re-derives verdicts from raw audio
  (docs/architecture/measurement-invariants.md I1–I12).
- libkan is invoked **only** through `native/` + `Native/` (thin adapter
  rule — keeps upstream osu! merges feasible).
- Progress is a callback contract, never console scraping.
- The native ABI is fixed-size POD; no allocations cross the boundary.
