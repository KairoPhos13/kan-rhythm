# Contributing to kan-rhythm

Thanks for helping make kan! better. This document defines the ground rules for
working in this repository. Read it before opening your first PR.

## 1. Language policy: English only

English is the language of collaboration. **Everything** committed to this
repository must be written in English:

- Source code: identifiers, comments, log/error messages, doc comments
- Commit messages and branch names
- Pull request titles and descriptions, and code review comments
- Documentation (README files, `docs/`, design notes)
- Issue reports

This applies even though the core team may speak other languages natively.
If you are not fluent, write your best English; reviewers will help with
wording, never penalize. Do not commit machine-translated walls of text
without reading them; keep it simple and clear instead.

User-facing strings in the game client may be localized later through a
proper i18n system; the source-of-truth strings stay in English.

## 2. Repository layout

This is a monorepo. Each top-level component is designed so it can be
extracted into its own repository later. Keep components self-contained
and never reach into another component's internals.

| Folder        | Component                                                  |
|---------------|------------------------------------------------------------|
| `libkan/`     | Header-only real-time DSP library (onset + pitch detection)|
| `kanfx/`      | Real-time audio FX graph (buses, consonance pitch-shift, ducker) |
| `apps/`       | Executables (`kan_cli` benchmark, future tools)            |
| `game/`       | **kan!** client + **kan!forge** editor                     |
| `charts/`     | `.kan`/`.kchart` song data + format specification          |
| `tools/`      | CLI utilities (incl. **kan!check** playability linter)     |
| `server/`     | Backend: users, catalog, scores, leaderboards (`kp`)       |
| `donations/`  | Donation engine: financial support (`kan!supporter`)      |
| `assets/`     | Graphics, sounds, fonts                                    |
| `docs/`       | Specification, roadmap, ADRs                               |

Every component keeps a `TODO.md` with its scoped, milestone-linked task list.

Dependencies point one way: `apps/`, `game/`, and `tools/` may depend on
`libkan/`; `libkan/` must not depend on anything else in the monorepo.

## 3. Real-time audio rules (non-negotiable)

Code that runs in, or feeds, the audio callback thread must:

- **Never** allocate or free memory (no `new`, `malloc`, `std::vector`, `std::string`)
- **Never** block (no mutexes, condition variables, I/O, `sleep`)
- **Never** call into the OS or third-party libraries that may do either
- Communicate with other threads only through lock-free structures
  (see `libkan/include/libkan/ring_buffer.hpp`)

Anything that violates these rules belongs in the consumer thread, not the
producer/callback path.

## 4. Build & test

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
ctest --test-dir build --output-on-failure
```

For the game, run `make check` in `game/`: it builds kan!'s C# with warnings
as errors and runs the native self-test and both C# suites
(see `game/README.md`).

Every PR must build cleanly with `-Wall -Wextra` (warnings are errors in
spirit; do not add new ones) and pass all tests. DSP changes must include
an offline test with a known expected result (see `libkan/tests/dsp_test.cpp`).

## 5. Commits & PRs

- Write commit messages in English, imperative mood: `Add chart validator skeleton`
- Prefer small, focused commits; one logical change per commit
- PR title = the commit message of a squash merge
- Reference the related issue when there is one
- Parallel agent/human lanes use the **git worktree model**: one directory =
  one branch = one component lane. See [docs/agent-workflow.md](docs/agent-workflow.md)

## 6. Licensing

Code is MIT (see `LICENSE`). Do not add dependencies with licenses
incompatible with MIT (GPL/AGPL) without discussing it first.
miniaudio (MIT-0 / public domain) is the reference for acceptable licensing.

The kan! client is built on a fork of `ppy/osu` (osu!lazer, MIT; ADR-0010).
Working with third-party open-source code obliges us to respect it in kind:

- Preserve every upstream copyright header and license text; never strip
  attribution from adapted code.
- Ship `THIRD_PARTY_NOTICES` in the client; mark substantially modified
  upstream files.
- Contribute general improvements back upstream (PRs to `ppy/osu`).
- Track license-sensitive third-party components (e.g. BASS, free for
  non-commercial use) and resolve their status before any public release.
