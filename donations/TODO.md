# donations/: TODO

The donation engine, financial support for the whole project
(osu!-style: the game is free, the community sustains it).

## Design phase (write docs/adr + design doc BEFORE code)

- [ ] Design doc: funding model overview (donation-supported, never pay-to-win,
      no predatory DLCs)
- [ ] Provider evaluation: Stripe, PayPal, Open Collective, GitHub Sponsors
      (fees, availability in target regions, payout model, API quality)
- [ ] `kan!supporter` tier design (cosmetic-only benefits):
  - [ ] Profile medals / badges
  - [ ] Fast direct map downloads in-client
  - [ ] Username cosmetics, profile background
  - [ ] Explicit rule: nothing that affects gameplay fairness or leaderboards
- [ ] Transparency reports plan: periodic public breakdown of where money goes
      (hosting, assets, development)

## Implementation phase (Milestone 4 timeframe)

- [ ] Public API (single source of truth shared by game client and website)
- [ ] Supporter status verification service (receipt → account entitlement)
- [ ] Webhook handlers for provider events (idempotent, auditable)
- [ ] Integration hooks for the game client (see game/TODO.md Milestone 4)

## Security & compliance (non-negotiable)

- [ ] Secrets management policy: API keys ONLY via environment variables /
      secrets manager; document required variables here; never in the repo
- [ ] No donor PII in logs; minimal data retention
- [ ] Financial logic auditable + tested (donations are trust)
- [ ] License compliance check for provider SDKs (no GPL contamination)

## Future

- [ ] Evaluate extraction into its own repository (different deployment
      lifecycle than the game; likely pairs with the future kan-web backend)
