# BASS (un4seen) licensing — status analysis & recommendation

- **Status:** analysis of record for Phase 0 (2026-10-12). Ship-blocking item
  per [osu-integration.md §4](../architecture/osu-integration.md) and
  [ADR-0010](../adr/0010-osu-lazer-base-client.md) §5: resolve before any
  donation-driven release.
- **Scope:** BASS / BASSMIX / BASSFX as used by **osu!framework** inside the
  kan! client (`game/client/`, osu!lazer fork). Our own instrument-capture and
  judgment path (`libkan`, `kanfx`, miniaudio — MIT-0) does **not** use BASS
  and is unaffected.

## 1. What exactly is BASS doing in our stack

| Use | Component | Licence relevance |
|---|---|---|
| Backing-track / music playback, sample & SFX playback, mixing, resampling | BASS + BASSMIX (via `ppy.osu.Framework`, ManagedBass C# wrapper is MIT, the native `libbass*.so`/`.dll` binaries are proprietary un4seen code) | **Covered by the BASS terms analysed here** |
| Tempo/pitch FX helper (BASSFX) | BASS FX add-on | Same un4seen licence family (add-on terms) |
| Instrument capture, onset/pitch detection, judgment taps | libkan (miniaudio, MIT-0) — see [osu-integration.md §2](../architecture/osu-integration.md) | Not affected |
| Dynamic consonance pitch shift | kanfx (Signalsmith DSP, MIT) | Not affected |

Observed runtime versions in the baseline build on this machine
(`~/.local/share/kan/logs/*.runtime.log`, 2026-10-12): **BASS 2.4.17.48,
BASSFX 2.4.12.16, BASSMIX 2.4.12.4** (Linux x86_64, via `ppy.osu.Framework`
2026.1005.0).

## 2. The terms (source: un4seen.com/bass.html, section "Licensing", fetched 2026-10-12)

Verbatim key sentence:

> "BASS is free for non-commercial use. If you are a non-commercial entity
> (eg. an individual) and you are not making any money from your product
> (through sales, advertising, etc) then you can use BASS in it for free.
> Otherwise, one of the following licences will be required."

Licence options (prices as published 2026-10-12):

| Licence | Price (EUR) | Grants |
|---|---|---|
| Shareware | €125 | Unlimited shareware products selling for ≤ €40 each (non-shareware products: ≤ €10; for subscriptions the limit applies to the annual fee). Duplicate licences raise the limit. Aimed at individuals selling their own software. |
| Single Commercial | €950 (iOS/Android €475) | One commercial product. |
| Unlimited Commercial | €3450 | Unlimited commercial products at one development site. |

Common terms: per-platform licences (40% reduction on each additional
platform), no royalties, future BASS updates included, no
reselling/sublicensing, end-user products only. Add-ons (BASSmix, BASS FX)
are distributed under the same non-commercial/commercial licence scheme and
covered by equivalent purchases; the exact add-on coverage for a given
purchase should be confirmed with un4seen when buying.

## 3. kan!'s funding model (donations/README.md, 2026-10-12)

- The game stays **free**; the community sustains it via voluntary donations
  (`kan!supporter`), with planned transparency reports.
- Planned donor counter-consideration: supporter **cosmetics, badges,
  fast-download hooks** (`donations/README.md`, `game/TODO.md` Milestone 4).
- Currently **no revenue at all**: the donation engine is "planned"
  (`donations/TODO.md`), nothing is collected or sold today.

## 4. Analysis

1. **Today (private development, no money flow, free download):** both
   conditions of the free-use clause are met — "non-commercial entity (an
   individual)" and "not making any money from your product". Baseline use of
   BASS during development is within the free-use permission.
2. **`kan!supporter` is the pressure point.** Giving in-game cosmetics,
   badges or download benefits in exchange for money looks like selling
   digital goods even when framed as "donations": it is money made *from the
   product*. Under the quoted wording ("making any money from your product
   (through sales, advertising, **etc**)"), paid supporter perks make the
   free-use permission doubtful. Purely voluntary donations with zero
   counter-consideration are a weaker case, but still money flowing to the
   product's development; un4seen's wording does not carve them out.
3. **Entity status is the second pressure point.** If the project is ever run
   by a legal entity, pays contractors from donations, or the product shows
   ads/affiliates, "non-commercial entity" fails regardless of §4.2.
4. **Do not rely on osu!/ppy precedent.** How ppy licenses BASS for
   osu!/osu!lazer is not documented publicly enough to lean on; our own
   determination must come from un4seen or our own licence.
5. **Replacement cost is high.** osu!framework's audio stack is BASS-based
   end to end (track/sample playback, mixing, device handling). Replacing it
   means forking osu!framework and re-validating latency behaviour — by far
   the most expensive option, and it would extend fork divergence to the
   framework layer (against ADR-0010's divergence discipline).

## 5. Options (as required by ADR-0010 §5)

| Option | Cost | Risk | When |
|---|---|---|---|
| **A. Verify applicability** — written enquiry to un4seen describing the model (free game, voluntary donations, optional supporter cosmetics, no ads) asking whether the non-commercial permission applies, and whether supporter perks change the answer. Keep the reply on file. | Free | They may answer "licence required" | **Now — before any public release or donation collection** |
| **B. Purchase one-time licence(s)** — Single Commercial, per platform | €950 (Linux/Windows/macOS ≈ €950 + 570 + 570 = **~€2090** with the 40% additional-platform reduction), plus add-on coverage as confirmed with un4seen | Low legally once purchased; recurring budget question is avoided (one-time) | If A fails, or as soon as `kan!supporter` ships paid perks |
| **C. Replace the backend** — swap BASS out of osu!framework (miniaudio/SDL output; keep kanfx for consonance shift) | High engineering cost + framework-level fork divergence | Medium (latency/quality re-validation) | Only if A and B are both impossible |

## 6. Recommendation (2026-10-12)

1. **Do A immediately** (it costs nothing and can only clarify): contact
   un4seen via https://www.un4seen.com/ ("Contact") or the un4seen forum,
   describing the model above in writing, and archive the reply here.
2. **Budget B as the expected outcome** if supporter perks ship in any paid
   form: one-time €950/platform is affordable for a donation-funded project
   and is the cleanest legal posture. Put it in the `donations/` transparency
   plan as a project asset cost.
3. **Do not do C now.** Keep the architecture C-ready instead: music/SFX
   playback is the only BASS-dependent surface (§1), instrument capture and
   judgment are already ours (miniaudio, MIT-0), and the consonance pitch
   shift already lives in kanfx. A future swap stays contained to the
   playback layer.
4. **Hard gate (unchanged):** no donation-driven release ships until this
   item is closed with either a written non-commercial confirmation from
   un4seen or purchased licence(s) — per osu-integration.md §4. Until then
   development builds stay non-public or non-monetised.

## 7. Sources

- un4seen Developments, "BASS" page, section *Licensing* —
  https://www.un4seen.com/bass.html (fetched 2026-10-12; prices and terms
  quoted in §2 above)
- un4seen Developments, "BASS add-ons" (BASSmix, BASS FX entries) — same page
  (fetched 2026-10-12)
- `donations/README.md` + `donations/TODO.md` (kan-rhythm monorepo,
  2026-10-12) — funding model
- Runtime log of the baseline build `~/.local/share/kan/logs/`
  (2026-10-12) — exact BASS versions in use
- `docs/adr/0010-osu-lazer-base-client.md` §5, `docs/architecture/osu-integration.md`
  §2/§4 — scope boundaries of this item
