# Admin tools: reset a player's password, list and remove accounts

Labels: `server`
Size: S

## Context

kan! sends no email, so a forgotten password is reset by hand on request (the
website's "Forgot your password?" note sends players to a GitHub issue).

## What to do

`dist/admin/cli.js` with `users`, `reset-password <username>` (new random
password, tokens revoked) and `delete-user <username> --yes`, documented in
`server/app/deploy/DEPLOY.md`.

## Acceptance criteria

- [ ] The owner can reset a password with one command.
- [ ] No emails or hashes are ever printed.

## Notes

In progress (2026-11-10). A real reset by email needs an email service first.
