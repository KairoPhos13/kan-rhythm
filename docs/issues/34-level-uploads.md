# Let level creators upload their levels to kan!'s catalogue

Labels: `server`, `editor`, `help wanted`
Size: L

## Context

Today levels reach kan!'s server only through the owner's importer
(`server/app/src/import/cli.ts`, `DEPLOY.md` §9): a creator exports the level
from kan!forge as `.osz` and the owner imports it. osu!'s in-game submission
uses a separate service kan! does not have (`BeatmapSubmissionServiceUrl` is
null in `KanEndpointConfiguration`).

## What to do

Design first, then build: an authenticated upload (website form or the
editor's submit flow), the same checks as the importer (kan! difficulties
only, size limits), a review queue the owner approves (levels must be free to
share: `charts/docs/free-song-candidates.md` explains the licence rule), and
the creator credited as owner.

## Acceptance criteria

- [ ] A signed-in creator can submit a level without the owner's shell.
- [ ] Nothing is public before the owner approves it.
- [ ] Uploads are size limited and rate limited.

## Notes

Pending a design decision by the owner: website upload or in-game submission.
