# Nightly database backups to R2, with a restore drill

Labels: `server`
Size: S

## Context

Real accounts exist since 2026-11-10. `server/app/deploy/backup.sh` and
`DEPLOY.md` §10 describe a nightly `pg_dump` to the `kan-backups` bucket, but it
is not set up on the host yet.

## What to do

rclone remote for R2 on the host (keys from `deploy/.env`, never in git), the
cron entry, one restore drill into a throwaway database.

## Acceptance criteria

- [ ] A dump lands in `kan-backups` every night; old dumps expire.
- [ ] One restore drill done and noted in `DEPLOY.md`.
