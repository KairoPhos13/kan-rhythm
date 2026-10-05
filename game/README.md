# game/

The kan-rhythm game client: engine integration, rendering, gameplay logic,
scoring, and the player-facing experience.

**Status:** planned — this folder is a placeholder.

## Ground rules (apply from day one)

- English only (code, comments, docs, commits) — see root `CONTRIBUTING.md`
- Real-time safety: gameplay code consumes audio through
  `libkan`'s lock-free ring buffer; never block or allocate on the audio path
- Dependencies flow toward `libkan/`; the game must not be referenced by it
- This component is designed to be extractable into its own repository later:
  keep it self-contained and declare its dependencies explicitly in CMake
