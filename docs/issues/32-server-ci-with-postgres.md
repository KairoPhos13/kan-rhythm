# Run the server's full test suite in CI, with requests shaped like the game's

Labels: `server`
Size: S

## Context

The game posts its forms as `multipart/form-data` (osu!framework), but the e2e
test sent them urlencoded, so logging in from the game broke and only the
owner found it. The e2e suite needs PostgreSQL and never runs in CI.

## What to do

- `.github/workflows/server.yml`: PostgreSQL service, `npm ci`, typecheck,
  lint and the whole jest suite.
- e2e: registration and token requests as multipart forms, plus the password
  rules and the website CORS.

## Acceptance criteria

- [ ] CI runs the server suite on every change under `server/`.
- [ ] A multipart regression would fail CI.

## Notes

In progress (2026-11-10).
