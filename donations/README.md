# donations/

The donation engine, the financial support layer for the whole kan-rhythm
project (osu!-style: the game stays free, the community sustains it via
`kan!supporter`).

**Status:** planned, see [TODO.md](TODO.md) for the scoped task list.

## Vision

A self-contained service/module that lets players and supporters fund the
project transparently. Possible scope (to be defined in a design doc under
`docs/` before implementation):

- Integration with donation/payment providers (e.g. Stripe, PayPal,
  Open Collective, GitHub Sponsors)
- Donor recognition integrated with the game (credits, badges, leaderboards)
- Transparency reports: where the money goes (hosting, assets, development)
- A public API so the game client and website can share one source of truth

## Ground rules

- English only (see root `CONTRIBUTING.md`)
- **Security first:** secrets/API keys never live in this repo; use
  environment variables or a secrets manager, and document the required
  variables in this README
- Handle money-related data with care: no donor PII in logs, follow the
  provider's compliance guidance
- Financial logic must be auditable and well tested; donations are trust,
  and trust needs verification
- Designed to be extracted into its own repository later (it likely has a
  very different deployment lifecycle than the game)
