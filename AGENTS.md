# Repository rules for AI agents

Follow these rules for every change in this repository. Full details in
`CONTRIBUTING.md`.

1. **English only.** All code, identifiers, comments, commit messages,
   PR descriptions, documentation, and chat-facing summaries of work done
   in this repo must be in English. This overrides the user's conversational
   language: reply to the user in their language, but never commit non-English
   content.
2. **Monorepo components.** Top-level folders (`libkan/`, `apps/`, `game/`,
   `charts/`, `tools/`, `donations/`, `assets/`, `docs/`) are future
   standalone repos. Keep them self-contained; dependencies flow toward
   `libkan/`, never out of it.
3. **Real-time safety.** Code on the audio callback path must not allocate,
   block, lock, or do I/O. Use the lock-free ring buffer in `libkan/`.
4. **Build must stay clean.** C++20, `-Wall -Wextra`, no new warnings.
   Verify with `cmake -B build && cmake --build build -j` and run
   `ctest --test-dir build`.
5. **Tests for DSP.** Any change to detection algorithms needs an offline
   test with known input and expected output.
