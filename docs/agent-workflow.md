# Git workflow for parallel agents (worktree model)

kan! is developed by multiple agents/humans in parallel. To make branch
collisions structurally impossible, we use **git worktrees**: one shared
repository, one physical directory per lane, one branch per directory.

## Layout

| Directory                                   | Branch         | Lane (component)          |
|---------------------------------------------|----------------|---------------------------|
| `kan-rhythm/` (main repo folder)            | `main`         | libkan + apps/kan_cli (+ root CMakeLists) |
| `kan-rhythm-agents/charts/`                 | `agent/charts` | charts/                   |
| `kan-rhythm-agents/tools/`                  | `agent/tools`  | tools/                    |
| `kan-rhythm-agents/docs/`                   | `agent/docs`   | docs/ (+ its ADR numbers) |
| `kan-rhythm-agents/donations/`              | `agent/donations` | donations/             |
| `kan-rhythm-agents/kanfx/`                  | `agent/kanfx`  | kanfx/                    |
| `kan-rhythm-agents/server/`                 | `agent/server` | server/ (+ its ADR numbers) |

Rules:

1. **One directory = one agent = one branch.** An agent never `cd`s into
   another lane's directory and never switches branches in a directory it
   does not own. Git enforces this: a branch checked out in one worktree
   cannot be checked out in another.
2. **The main folder stays on `main`** and is the only lane allowed to edit
   `libkan/`, `apps/`, and the root `CMakeLists.txt`. It commits small and
   frequent so lanes can rebase cheaply.
3. **Stay in your component.** Cross-component needs go into your final
   report as proposals (e.g. "root CMake wiring snippet", "libkan API
   request"), never as edits.
4. **Sync often:** inside your worktree, `git fetch && git rebase main`
   (or `git merge main`) at the start of every work session.
5. **Merging back** is done by the maintainer (or the libkan/main lane)
   from the main folder:
   ```bash
   git merge --no-ff agent/<name>    # after reviewing
   git branch -d agent/<name>        # optional: retire merged branch
   ```
   Suggested order when several land at once: docs → charts → tools →
   kanfx → server → donations (each rebase trivial because lanes are
   folder-disjoint).
6. **Never force-push `main`.** Never commit build artifacts (`build/` is
   gitignored).

## Worktree cheat sheet

```bash
# create a lane
git worktree add ../kan-rhythm-agents/<name> -b agent/<name>

# list lanes
git worktree list

# remove a finished lane (after its branch merged)
git worktree remove ../kan-rhythm-agents/<name>
```

All worktrees share the same object store and remote. A commit in any lane
is instantly visible to `git log agent/<name>` from everywhere; only the
maintainer merges into `main`.
