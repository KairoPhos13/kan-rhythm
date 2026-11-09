# kan! server — minimum hosting study

- **Status:** research only (no code, no infrastructure changes)
- **Date of study:** 2026-11-09. All web prices were read on **2026-11-09** unless a
  different date is stated next to the source.
- **Owner/public identity:** KairoPhos (see §6 for privacy constraints).
- **Scope:** what it costs to run the kan! server at "curious people can try it" scale
  (tens of registered players, a handful concurrent, 50–300 levels of 3–15 MB each),
  for the two options compared in the feasibility work:
  - **Option A** — osu!'s own stack adapted: `ppy/osu-web` (PHP/Laravel, MySQL, Redis,
    Elasticsearch, queue workers) plus the .NET services (`osu-server-spectator`,
    `osu-queue-score-statistics`, `osu-elastic-indexer`, beatmap difficulty lookup
    cache).
  - **Option B** — a small own server (Node 22 + NestJS + PostgreSQL) speaking the
    same client protocol, per ADR-0007.
- Levels are served from object storage/CDN via short-lived signed URLs; the API only
  answers the download request with a redirect (same pattern as osu!'s download
  mirrors). See §4.
- Estimates are marked *(estimate)*. Third-party price trackers are labeled. No
  osu-web code is reproduced here (it is AGPL-3.0; ADR-0012 already forbids adopting
  it — noted in §7 as a cost factor for Option A).

---

## 1. Resource needs per option

### 1a. Option A — osu! stack adapted

Evidence base: `ppy/osu-web` `compose.yaml`, `SETUP.md`, `DEPLOYMENT.md`
(github.com/ppy/osu-web, read 2026-11-09), and the READMEs of
`ppy/osu-server-spectator` and `ppy/osu-queue-score-statistics` (read 2026-11-09).
Described from their published configuration only.

Processes in the stack (from osu-web's own compose file and deployment docs):

| Process | What it does | RAM (realistic min) | RAM (comfortable) | vCPU | Evidence / note |
|---|---|---|---|---|---|
| PHP/Laravel (Octane) + nginx | web + API v2 | 512 MB–1 GB | 1–2 GB | 1–2 | osu-web runs `octane` behind nginx; a separate nginx container serves static assets |
| Node asset build (`assets`) | webpack/yarn asset pipeline | 0.5–1 GB (build time) | 1–2 GB | 1 | runs at build/deploy time in dev compose; not needed at rest *(estimate)* |
| Queue workers (`job`, `queue:work`) | beatmap processing, notifications, store | 256–512 MB | 512 MB | 0.5 | `DEPLOYMENT.md`: several named queues, one or more runners |
| Scheduler (`schedule:run`) | cron every 10 min | ~50 MB (transient) | — | — | `DEPLOYMENT.md` |
| MySQL 8.4 | main database | 512 MB–1 GB | 1–2 GB | 1 | compose pins `mysql:8.4`; buffer pool ≈256–512 MB at this scale *(estimate)* |
| Redis | cache, session, broadcast, queue | 50–200 MB | 256 MB | 0.1 | *(estimate)* |
| Elasticsearch 7.17 | beatmap search + solo scores | **1–2 GB** | 2–4 GB | 1 | compose sets JVM heap `-Xms512m -Xmx512m`, but osu-web `SETUP.md` warns the ES container "uses a high (1+ GB) amount of RAM" and to give the machine **at least 2 GB**; Elastic's JVM docs: heap ≤ 50% of node RAM (www.elastic.co/docs/reference/elasticsearch/jvm-settings, read 2026-11-09). OpenSearch follows the same 50 % rule |
| `osu-elastic-indexer` (.NET) | score → ES indexing | 200–512 MB | 512 MB | 0.5 | *(estimate)*; .NET baseline ~50–150 MB + buffers |
| `osu-beatmap-difficulty-lookup-cache` (.NET) | difficulty attributes cache | 200–512 MB | 512 MB | burst | *(estimate)*; CPU bursts on cache misses |
| `osu-notification-server` | websocket notifications | 100–300 MB | 300 MB | 0.2 | *(estimate)*; compose runs it against the same MySQL + Redis |
| `osu-queue-score-statistics` (.NET) | medals, pp, user stats on score arrival | 256–512 MB | 512 MB | burst | *(estimate)*; optional real-time difficulty mode downloads and computes chart difficulty |
| `osu-server-spectator` (.NET, SignalR) | multiplayer/spectator + replay upload | 256–512 MB | 512 MB | 0.5 | *(estimate)*; **can be omitted at first** — kan! defers multiplayer (ADR-0012); it can upload replays to S3-compatible storage directly |

**Option A totals:** realistic minimum **~6 GB RAM / 2 vCPU / 20–30 GB disk**;
comfortable **8–12 GB RAM / 2–4 vCPU / 50 GB+ disk** (MySQL data, ES indices, OS +
Docker overhead ~0.5–1 GB). RAM is the binding constraint: MySQL and Elasticsearch
both want file-system cache and ES wants heap = 50 % of its share.

ARM note: osu-web `SETUP.md` states that *older* Elasticsearch versions do not run on
ARM CPUs; ES 7.17 does support arm64, but the `osu-elastic-indexer` fallback to the
ES 6.8 OSS image does not. Graviton/Ampere hosts are viable for the 7.17-era stack but
this is worth testing early.

### 1b. Option B — NestJS + PostgreSQL (ADR-0007 modular monolith)

| Process | RAM (realistic min) | RAM (comfortable) | vCPU | Note |
|---|---|---|---|---|
| Node 22 / NestJS API | 150–300 MB | 512 MB | 0.5–1 | typical small NestJS heap *(estimate)* |
| Worker processes (score ingestion, kp recompute, webhooks) | 100–300 MB each; 1 needed at this scale | 512 MB | 0.5 | ADR-0007 requires CPU-heavy work off the request path *(estimate)* |
| PostgreSQL 16+ | 512 MB–1 GB | 1–2 GB | 1 | `shared_buffers` 256 MB is fine for tens of users and 300 levels *(estimate)* |
| Redis (cache, rate limits, queue) | 50–100 MB | 256 MB | 0.1 | *(estimate)* |
| Reverse proxy + TLS (Caddy/nginx) | ~50 MB | 100 MB | 0.1 | *(estimate)* |

Search at 300 levels can use PostgreSQL full-text search — **no Elasticsearch needed**.

**Option B totals:** realistic minimum **~1.5–2 GB RAM / 1 vCPU / 10–20 GB disk**;
comfortable **2–4 GB RAM / 2 vCPU / 40 GB** (room for replays/exports on disk).

> **Reviewer note (Claude, 2026-11-09):** the table above sizes the full ADR-0007 design. The
> MVP (`mvp-scope`, goals 1-4) has no Redis and no separate worker: one NestJS process
> (~150-250 MB), PostgreSQL tuned small (`shared_buffers` 64-128 MB, ~150-300 MB) and Caddy
> (~30 MB) come to **~0.5-0.7 GB**, so a **1 GB instance with 1-2 GB swap fits the MVP** for
> "curious players" (tens of users). 512 MB does not. Re-measure once the server runs.

---

## 2. Does it fit next to Nebula on the owner's AWS instance?

Nebula lighthouse is not the constraint: the project's own README says "Running a
lighthouse requires very few compute resources, and you can easily use the least
expensive option from a cloud hosting provider… a number of us have used $6/mo
DigitalOcean droplets as lighthouses" (github.com/slackhq/nebula README, read
2026-11-09). Its memory footprint is roughly **10–50 MB RSS** *(estimate; we found no
official published figure — see Summary)*. It shares the host fine.

Fit table (columns = server option; the usual limit is RAM):

| Instance | RAM | Option A (osu! stack) | Option B (NestJS + PG) |
|---|---|---|---|
| Lightsail 512 MB / t4g.nano (0.5 GB) | 0.5 GB | **No** — Elasticsearch alone wants ≥2 GB | **No** — OS + Node + PostgreSQL need ~1.5 GB |
| Lightsail 1 GB / t3.micro / t4g.micro (1 GB) | 1 GB | **No** — MySQL + ES + PHP + .NET services do not fit | **Tight** — runs only with tuning + swap; cold-start and migration pain under any spike |
| Lightsail 2 GB ($12) / t4g.small (2 GB) | 2 GB | **No** — ES heap + MySQL already consume it | **Fits** — comfortable for tens of users |
| 4 GB (Lightsail $24 / t4g.medium / Hetzner CX/CAX) | 4 GB | **Tight** — doable only for a very small deployment, single-digit concurrent users, ES heap pinned low | **Fits comfortably** |
| 8 GB+ | 8 GB+ | Fits | Fits with lots of headroom |

**Swap on EBS as a stop-gap:** a 2–4 GB swap file prevents OOM kills on the 1 GB
instances. On Lightsail it is free (swap lives on the bundled SSD). On EC2 it costs
EBS gp3 storage, list price ≈$0.08/GB-month *(list price, not re-verified on
2026-11-09; aws.amazon.com/ebs/pricing)* ≈ $0.16–0.32/mo for 2–4 GB. Caveats: swap
turns memory pressure into EBS latency spikes, and Elasticsearch should run with swap
disabled — so swap is a reasonable stop-gap for **Option B only**, not for A.

---

## 3. Hosting candidates with current prices

Prices read 2026-11-09. VAT/sales tax extra where applicable.

### AWS

- **Lightsail** (aws.amazon.com/lightsail/pricing): Linux bundles now
  **$5/mo** 0.5 GB RAM / 2 vCPU / 20 GB SSD / 1 TB transfer; **$7/mo** 1 GB / 40 GB /
  2 TB; **$12/mo** 2 GB / 60 GB / 3 TB; **$24/mo** 4 GB / 80 GB / 4 TB. (The old
  $3.50 entry plan is gone.)
- **EC2 t4g/t3** (aws.amazon.com/ec2/pricing/on-demand lists are JS-rendered; figures
  corroborated by several 2026 price trackers — instances.vantage.sh, usage.ai,
  instancefinder.com, cloudquell.com "updated 2026-10-12", all read 2026-11-09):
  t4g.nano $0.0042/h (≈**$3.07/mo**, 0.5 GB), t4g.micro $0.0084/h (≈**$6.13/mo**,
  1 GB), t3.micro $0.0104/h (≈**$7.59/mo**, 1 GB, x86). EBS volume extra; 100 GB/mo
  data-transfer-out free across AWS (aws.amazon.com/ec2/pricing/on-demand, read
  2026-11-09). Lightsail bundles beat t4g on price/GB at the small end.
- **Free tier status in 2026 — important change** (aws.amazon.com/free and
  aws.amazon.com/free/free-tier-faqs, read 2026-11-09): AWS replaced the classic
  "12 months / 750 hours of a micro instance" free tier for **new accounts** with a
  new sign-up experience (AWS FAQ references its launch in **October 2026**; third-
  party trackers date the shift to mid-2025 — exact cutover unverified). New
  accounts get a **Free plan with up to $200 in credits** ($100 on sign-up + up to
  $100 more), valid **6 months** or until credits run out; then the account is
  suspended, data kept 90 days, then deleted unless upgraded to Paid. No payment
  method required for most new customers. 30+ services are "Always Free" within
  monthly limits (e.g. S3 5 GB *(classic always-free limit; not re-verified)*,
  CloudFront 1 TB/mo egress — see §4). **Bottom line: "free AWS forever" no longer
  exists for new accounts; it is 6 months of credits.**
- **RDS free tier** (aws.amazon.com/rds/free, read 2026-11-09 via search + page
  snippet): the free plan covers db.t3.micro / db.t4g.micro (MySQL, PostgreSQL,
  MariaDB, SQL Server Express) **within the same 6-month/$200-credit plan**; the
  classic 750-hours-for-one-year wording still appears on some AWS pages (treat as
  transitional, verify at sign-up). On demand afterwards: db.t4g.micro ≈ $0.016/h
  (≈**$12/mo**) + storage (2026 trackers, selfhost.dev / whichdevtool.com, read
  2026-11-09).

### Oracle Cloud Always Free

(docs.oracle.com "Always Free Resources", read 2026-11-09)

- **Ampere A1 (VM.Standard.A1.Flex):** 1,500 OCPU-hours + 9,000 GB-hours/month free
  = **2 OCPU + 12 GB RAM total**, allocatable as one instance or two. (Beware older
  guides quoting "4 OCPU / 24 GB" — that is outdated.) Up to 2× AMD micro VMs
  (1/8 OCPU, 1 GB) additionally. 200 GB block volume total (default 50 GB boot),
  20 GB object storage, **10 TB/month outbound transfer**, and 3,000 emails/month via
  OCI Email Delivery. All $0 for the life of the account.
- **Reliability caveats (documented):** "out of host capacity" errors when creating
  A1 instances (retry or change AD); **idle-instance reclamation** — Oracle may
  reclaim Always Free compute if, over a 7-day window, CPU 95th-percentile < 20 %,
  network < 20 %, and (A1 only) memory < 20 %. A small cron load keeps this at bay,
  but treat the host as disposable and keep off-site backups. Community reports of
  accounts being closed for low activity exist *(community reports, unverified)*.
- Outbound port 25 is blocked by default (use OCI Email Delivery or an external
  SMTP API).

### Hetzner Cloud

(hetzner.com/cloud plus trackers trackvps.com, vpsdex.com, hostmath.dev,
comparedge.com, read 2026-11-09)

- Hetzner **raised cloud prices on 2026-06-15**; existing servers keep old prices.
  New orders: **CX23** (2 vCPU x86, 4 GB, 40 GB, 20 TB traffic) **€5.49/mo** +
  **€0.50/mo IPv4** ≈ €5.99 excl. VAT; **CAX11** (2 vCPU ARM, 4 GB, 40 GB)
  ≈ **€5.99/mo** + IPv4 (trackers disagree slightly: vpsdex "CAX11 €5.99",
  comparedge "$4.99"); CX33 (8 GB) €8.49; general-purpose/dedicated from €43.49.
  The official landing page shows cost-optimized "from €5.99/mo" (excl. VAT).
- Best RAM-per-euro of the paid small VPS here; EU/US/SG locations; IPv4 surcharge
  and VAT are the gotchas.

### DigitalOcean

(digitalocean.com/pricing/droplets, read 2026-11-09)

- Basic Droplets: **$4/mo** 512 MiB/1 vCPU/10 GiB/500 GiB; **$6/mo** 1 GB/25 GiB/1 TB;
  **$12/mo** 2 GB/50 GiB/2 TB; **$24/mo** 4 GB/80 GiB/4 TB. Per-second billing since
  2026-01-01. Promo credits common for new accounts (their FAQ cites a $5/90-day
  example). More expensive than Hetzner for the same RAM.

### Fly.io / Railway / Render

- **Fly.io** (docs.fly.io/about/pricing, read 2026-11-09): **no free tier** for new
  organizations; $1 trial credit for 7 days. `shared-cpu-1x` 256 MB **$2.19/mo**,
  512 MB **$3.69/mo**, 1 GB **$6.70/mo** (always-on, iad/ewr); volumes $0.15/GB-mo;
  shared IPv4 free (dedicated $2/mo); egress $0.02/GB NA/EU. Managed Postgres starts
  at **$38/mo** — too expensive; a self-run Postgres Machine is the realistic route
  (single-node ≈ $2–7/mo *(estimate)*).
- **Railway** (railway.com/pricing, read 2026-11-09): **Free plan** $0 with $1/mo
  usage credit (max 1 vCPU / 0.5 GB per service, 3 services, **no custom domains**);
  **Hobby $5/mo** including $5 usage. Usage rates: CPU $20/vCPU-mo, RAM $10/GB-mo,
  volumes $0.15/GB-mo, egress $0.05/GB, object storage $0.015/GB-mo with free egress.
  An always-on API + PG (≈0.5 vCPU / 1 GB total) runs ≈ **$15–25/mo** of usage
  *(estimate)* — over the included credits.
- **Render** (render.com/pricing and render.com/docs/free, read 2026-11-09):
  **Free** web services (0.1 CPU / 512 MB) but they **spin down after 15 min idle**
  (~1 min cold start), get 750 free instance-hours/mo per workspace, and lose local
  files on restart. **Free Postgres is 1 GB and expires 30 days after creation**
  (14-day grace, then deleted) — fine for dev, not for a real service. Free Key Value
  is 25 MB, in-memory only. Paid: web 0.5c/512 MB **$7/mo**, 1c/2 GB $25/mo; Postgres
  0.5c/256 MB $6/mo, 1 GB $19/mo. Bandwidth 5 GB included on Hobby then $0.15/GB.

### Home server behind Cloudflare Tunnel

- Cost: **$0/mo** hosting (own hardware + electricity). `cloudflared` is a
  lightweight outbound-only daemon (developers.cloudflare.com/cloudflare-one/
  networks/connectors/cloudflare-tunnel, read 2026-11-09); Cloudflare Tunnel is
  included in Cloudflare's free plan *(free-plan inclusion well known; the pricing
  page itself was not reachable on 2026-11-09)*.
- Uptime: no SLA — home power, router, ISP and reboots all take the service down.
  Residential upload bandwidth limits download throughput if levels are served from
  home (mitigate: serve levels from R2/B2/CDN per §4 and keep only the API at home).
- Privacy: the home IP stays hidden (origin never accepts inbound connections), but
  **all traffic transits Cloudflare** (TLS terminated at their edge on the free
  plan). The home ISP sees traffic volumes. Good for pseudonymity of the origin, not
  for confidentiality against the CDN provider.
- Practical: viable for a demo with a "may be down sometimes" note; not for score
  submission integrity if you also care about uptime.

### Managed database free tiers (for Option B)

| Provider | Free tier | Key limits | Read |
|---|---|---|---|
| **Neon** (neon.com/pricing, page updated 2026-11-04) | Free plan is **permanent** (not a trial), no card | 100 projects; 1 GB Postgres storage per project (20 GB total); 100 CU-hours/project/mo (0.25 CU ≈ 1 GB class); scale-to-zero after 5 min (hundreds of ms resume); 5 GB egress/project | 2026-11-09 |
| **Supabase** (supabase.com/pricing) | Free | 500 MB DB, 5 GB egress, 1 GB file storage, 50 k MAU, 2 active projects; **paused after 1 week of inactivity** | 2026-11-09 |
| **AWS RDS** (aws.amazon.com/rds/free) | Inside the 6-month/$200-credit free plan | db.t3.micro/db.t4g.micro; afterwards ≈$12/mo + storage; legacy "750 h × 1 year" wording still shown on some pages | 2026-11-09 |
| **Render Postgres free** | 1 GB | **expires 30 days after creation**, no backups | 2026-11-09 |

---

## 4. Level storage and downloads

Workload model: **300 levels × 10 MB = 3 GB stored**, **2,000 downloads/month ≈ 20 GB
egress**, ~300–600 write requests/month, ~2,000 read requests/month. The API returns
a redirect to a **short-lived signed URL**; all three stores below support that
(R2 and B2 are S3-compatible presigned URLs; S3 has native presigned URLs; CloudFront
has signed URLs/cookies).

| Option | Monthly cost at this workload | Numbers (read 2026-11-09) | Signed URLs |
|---|---|---|---|
| **Cloudflare R2** | **$0.00** | Free tier: 10 GB-month storage, 1 M Class A ops, 10 M Class B ops, **egress always free** (all classes). Beyond free: $0.015/GB-mo storage, $4.50/M Class A, $0.36/M Class B (developers.cloudflare.com/r2/pricing, page updated 2026-10-30) | Yes (S3 presigned URLs / Workers) |
| **AWS S3 + CloudFront** | **≈$0.07–0.10** *(estimate)* | S3 Standard $0.023/GB-mo → 3 GB ≈ $0.07; PUT $0.005/1k, GET $0.0004/1k; CloudFront egress $0.085/GB (first 10 TB NA/EU) **after a permanent 1 TB/mo free allowance** which covers 20 GB easily; S3→CloudFront origin transfer free (aws.amazon.com/s3/pricing + aws.amazon.com/cloudfront/pricing; the S3 tables are JS-rendered, rates corroborated by 2026 trackers egresscost.com, costgoat.com, cloudzero.com) | Yes (presigned / CloudFront signed) |
| **Backblaze B2 + Cloudflare** | **$0–0.11** | First 10 GB storage free; $6.95/TB-mo beyond; free egress up to 3× average stored (3 GB × 3 = 9 GB < 20 GB → ≈11 GB × $0.01 = $0.11) **or unlimited free egress via partner CDN including Cloudflare**; Class A/B/C API calls free (backblaze.com/cloud-storage/pricing) | Yes (S3-compatible presigned) |

Recommendation within §4: **R2** — the workload fits the free tier outright, egress
is free at any growth, and the signed-URL + redirect design migrates unchanged to S3
or B2 later. Keep the bucket private; issue URLs with 5–15 min expiry from the API.

---

## 5. Domain, TLS, email

- **Domain:** ~**$10–12/yr** for `.com` (~$1/mo). Cloudflare Registrar sells at
  cost ("never pay more than we do… registration, transfer, and renewal prices are
  always at or below what registries and ICANN charge us",
  cloudflare.com/products/registrar, read 2026-11-09; exact .com price tracks
  registry fees, historically ≈$10.44–10.87/yr *(estimate)*). Porkbun: ".COM from
  $11.81" first year, with free WHOIS privacy, free Let's Encrypt certs and up to 20
  free email forwards (porkbun.com, read 2026-11-09). Either is fine.
- **TLS:** free everywhere — Let's Encrypt (Caddy/certbot), Cloudflare free-plan
  universal SSL, or Porkbun's free Let's Encrypt certs. $0.
- **Outgoing email** (password reset / notifications): registration in the client
  does **not** require email verification if the server does not demand it (project
  context), so a first release can ship with no mail at all (admin-assisted reset)
  and add mail later. When needed, cheapest options:
  - **Resend Free:** 3,000 emails/mo, capped at 100/day, 3 domains
    (resend.com/pricing, read 2026-11-09).
  - **Brevo Free:** 300 emails/day (≈9,000/mo), no card required
    (brevo.com/pricing, read 2026-11-09).
  - **Amazon SES:** à-la-carte $0.10/1,000; new accounts default to the Essentials
    plan at $0.16/1,000 since 2026-07-21; there is no permanent SES free tier any
    more — usage is covered by the $200 free-plan credits on new accounts
    (aws.amazon.com/ses/pricing, read 2026-11-09).
  - **Oracle OCI Email Delivery:** 3,000/mo free (Always Free docs) — only relevant
    if the server runs on Oracle. Outbound port 25 is blocked on Oracle by default
    and on Render free services.

---

## 6. Privacy (KairoPhos must not be linked to a legal name)

- **WHOIS/RDAP:** gTLD registrars have been required to redact registrant personal
  data in public registration data since ICANN's 2018 temporary specification
  (now permanent via RDAP) — in practice, name/email/address are redacted for `.com`
  etc. regardless of registrar. On top of that, registrars offer free privacy
  services: **Cloudflare Registrar** — "Privacy by default: free WHOIS redaction"
  (cloudflare.com/products/registrar, read 2026-11-09); **Porkbun** — "Free WHOIS
  Privacy" (porkbun.com, read 2026-11-09); Namecheap also includes it *(not
  re-verified)*. **AWS Route 53** now enables privacy protection by default on
  registration/transfer, with some TLDs unsupported (docs.aws.amazon.com/Route53
  developer guide, seen via search 2026-11-09 — verify the TLD list before use).
- **Account identity:** all candidates above accept a pseudonym/organization name in
  the account profile; payment name on invoices is private billing data and is not
  published (per the study brief). Nothing forces the legal name into any public
  surface.
- **Keeping the public face as KairoPhos:**
  1. Register the domain under the project name with privacy redaction on; never put
     a personal email in the registrant contact (use `something@<project-domain>`).
  2. Use Porkbun's free email forwarding or Resend for that mailbox — no personal
     mailbox in headers.
  3. Run the GitHub organization, docs and API branding as KairoPhos only; sign
     Nebula certs and TLS subjects with the project name, not a personal name.
  4. Cloudflare Tunnel hides the origin IP of a home server (helpful), but the
     privacy trade-off is that Cloudflare sees plaintext at the edge on the free
     plan (§3).
  5. Nothing in the recommended stack requires a publicly visible legal name;
     registrar/host KYC data (e.g. Hetzner identity checks) is billing-side only.
- Avoid: registrars/TLD combinations without privacy for that TLD; WHOIS contacts
  with a personal mailbox; any "hosted by home connection" setup where the home IP
  would be published in DNS (use the Tunnel, never an A record to home).

---

## 7. Recommendation

### Option A (osu! stack) — cheapest sensible setup

- Minimum is ~6–8 GB RAM, so the free 12 GB A1 box is the only *free* fit:
  **Oracle Always Free A1 (2 OCPU / 12 GB, $0/mo)** + **R2 for levels ($0)** +
  domain ≈ $10/yr → **≈ $1/mo**. Accept the documented idle-reclamation and
  capacity risks: automate nightly backups (mysqldump + ES snapshots) to R2/B2 and
  keep the host disposable. Alternative without those risks: Hetzner CX33
  (8 GB) €8.49 + €0.50 IPv4 ≈ **€9/mo (~$10)** — over the "few USD" budget.
  4 GB (CX23/CAX11 ≈ €6/mo) is *tight* for A: ES heap pinned to 512 MB, single-digit
  concurrent users only.
- Skip `osu-server-spectator` initially (multiplayer is deferred); run the indexer,
  difficulty cache and score-statistics workers as systemd units in containers.
- Migration path: A scales upward (bigger VPS first, then split MySQL/ES), but
  remember ADR-0012 — osu-web is AGPL-3.0 and cannot be adopted; "A adapted" means
  clean-room reimplementation of the PHP surface anyway, which is the real cost of
  this option, not the hosting.

### Option B (NestJS + PostgreSQL) — cheapest sensible setup

- Fits a 1 GB box, comfortable on 2 GB. **$0 option:** Oracle Always Free A1
  (1 OCPU / 4–8 GB slice, $0) + Neon free Postgres (or PG self-hosted on the same
  box) + R2 free levels + Resend free mail + domain ≈ $10/yr → **≈ $1/mo** (same
  reclamation caveat as above).
- **Low-hassle option:** Hetzner CAX11/CX23 (4 GB) €5.49–5.99 + €0.50 IPv4 ≈
  **€6/mo (~$6.50)** hosting everything (PG included) + R2 $0 + domain ~$1/mo →
  **≈ $7.50/mo**. Or Lightsail 1 GB $7/mo if the owner wants to stay inside AWS.
  A fully free-but-fragile split (Render/Railway free + Neon free + R2) is possible
  but has cold starts and free-plan expiry traps — not recommended for scores.
- Migration path: keep the signed-URL object-storage design and the ADR-0007 worker
  split; when demand grows, move Postgres to Neon Launch (~$15/mo) or RDS, grow the
  VPS to 4–8 GB, put Cloudflare caching in front of level downloads, and split
  workers onto their own service. No rewrite needed at any step.

---

## Summary

- **Fit on the existing AWS box next to Nebula** (lighthouse ≈10–50 MB *(estimate)*;
  README says it needs "very few compute resources"):

  | Instance | A (osu! stack) | B (NestJS+PG) |
  |---|---|---|
  | 0.5 GB (t4g.nano / Lightsail 512 MB) | no | no |
  | 1 GB (t3.micro / t4g.micro / Lightsail 1 GB) | no | tight (swap; EBS swap ≈$0.16–0.32/mo, free inside a Lightsail bundle) |
  | 2 GB | no | fits |
  | 4 GB | tight | comfortable |

- **Recommendation A:** Oracle Always Free A1 (2 OCPU/12 GB, $0) + R2 + domain ≈
  **~$1/mo**, with nightly off-box backups (reclamation risk); Hetzner 8 GB ≈
  $10/mo if risk-free. AGPL (ADR-0012) makes A a reimplementation project anyway.
- **Recommendation B:** Hetzner CAX11/CX23 ≈ **~$7.5/mo all-in** (or Oracle A1 +
  Neon free + R2 ≈ **~$1/mo** with caveats). 300 levels on R2 cost $0.
- **Could not verify:** the Lightsail allowance inside the new AWS free plan; exact
  Hetzner CAX11 price (trackers differ by ±€1); Cloudflare Tunnel free-plan fine
  print; the Route 53 privacy TLD list; an official Nebula lighthouse RAM figure;
  the AWS free-tier cutover date (FAQ says Oct 2026, trackers say Jul 2025) and any
  surviving legacy 12-month RDS/EC2 offer; EBS gp3/S3 request rates were corroborated
  only via 2026 price trackers.
