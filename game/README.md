# game/

The **kan!** game client (engine integration, rendering, gameplay logic,
dual-axis scoring) and the integrated **kan!forge** map editor with local AI
stem separation and note transcription.

**Status:** planned — see [TODO.md](TODO.md) for the scoped task list
(Milestones 2–3).

## Ground rules (apply from day one)

- English only (code, comments, docs, commits) — see root `CONTRIBUTING.md`
- Real-time safety: gameplay code consumes audio through
  `libkan`'s lock-free ring buffer; never block or allocate on the audio path
- Dependencies flow toward `libkan/`; the game must not be referenced by it
- This component is designed to be extractable into its own repository later:
  keep it self-contained and declare its dependencies explicitly in CMake
