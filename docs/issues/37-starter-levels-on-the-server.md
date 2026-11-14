# The starter levels on the server, with their leaderboards

Labels: `server`, `levels`
Size: S

## Context

Owner QA of v0.1.1 (2026-11-14): searching levels from the game finds
nothing on kan!'s server. The search works: by default it lists ranked,
approved and loved levels (`server/app/src/beatmaps/search-query.ts`), and
no level on the server has one of those statuses yet. The five starter
levels ship inside the game (issue [15](15-first-free-bundled-level.md))
but were never published on the server, so they have no online page and
no leaderboard.

The owner asked whether the default levels should be there. They should:
a new player who opens the online listing finds the same five levels they
already have, ranked, with a leaderboard, and credited to their creator.

## What to do

1. Publish the five `.osz` files from `charts/official/dist/` on the
   server, credited to KairoPhos as their creator (the owner's account),
   with the admin tools (`server/app/deploy/DEPLOY.md`, "Levels").
2. Make sure the server's copy is the same level the game imports (same
   beatmap hashes), so a score played on the bundled copy counts on the
   server's leaderboard. Check it end to end: play a starter level signed
   in, the score shows on the level's online leaderboard.
3. Set them to ranked (ADR-0009) and check they appear in the game's
   default search and on the website.
4. Write the steps down in `DEPLOY.md`, so each new pack version
   (`pack.json`) is published the same way.

## Acceptance

- [ ] The game's online search lists the five starter levels by default.
- [ ] Each shows KairoPhos as creator, on the website too.
- [ ] A signed-in play of a bundled starter level appears on its online
      leaderboard.
- [ ] `DEPLOY.md` says how to publish the next pack version.
