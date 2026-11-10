# Stay quiet online when kan!'s server has no realtime services

Labels: `game`, `server`
Size: S

## Context

kan!'s server (`server/app`, live at `https://kan-api.kairophos.link`) answers
accounts, levels and scores, but has no realtime services (metadata,
spectator, multiplayer, notifications, chat). After logging in, the game keeps
trying to connect to them every minute and the login panel looks like it is
still waiting, although the account is fully signed in.

## What to do

- Client: a connector whose endpoint is empty never connects and reports a
  settled "not available" state (fork F-27, `game/docs/fork-divergence.md`).
- Server: empty answers for `GET /api/v2/notifications`,
  `POST /api/v2/chat/ack` and `GET /api/v2/seasonal-backgrounds` in the shapes
  the client parses (`server/docs/lazer-protocol.md`).

## Acceptance criteria

- [ ] After login the panel shows the signed-in account, with nothing spinning.
- [ ] No realtime connect attempts or 404s in the game's network log.
- [ ] Client build 0 warnings; server unit tests for the new routes.

## Notes

In progress (2026-11-10).
