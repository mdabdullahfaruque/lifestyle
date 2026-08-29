# Lifestyle — Deployment Guide

| Field | Value |
|---|---|
| Document | 05 — Deployment |
| Version | 0.2 |
| Date | 2026-08-30 |
| Applies to | Phase 0 + Phase 1 backend, Angular workspace |
| Edge | **Cloudflare** (DNS, CDN, WAF) + **Cloudflare Pages** (seller & admin SPAs) |
| Origin | Your server: Docker Compose — Caddy, API, PostgreSQL, Redis, media on disk (storefront SSR joins in Phase 2) |
| Related | [03 — Infrastructure & Multi-Region](03-INFRASTRUCTURE-MULTI-REGION.md) · [04 — Codebase Structure](04-CODEBASE-STRUCTURE.md) |
| Artifacts | [`deploy/`](../deploy/) · [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml) |

This is the PropertyMart pattern (doc 03 §2) — Cloudflare in front, Pages for frontends, Docker
stacks on one host — extended with the pieces Lifestyle needs that PropertyMart does not: SSR,
wildcard vendor subdomains, vendor custom domains, and off-host backups.

---

## 1. Scope — what this deployment ships

**The storefront is deferred to Phase 2, by your decision** — and that matches the roadmap, which
always had "Storefront, browse & search" as Phase 2 (Plan §6). What goes live now is everything
Phase 1 exercises: vendor onboarding, KYC, approval, catalog entry, moderation.

| Piece | Where it runs | Status |
|---|---|---|
| **api** | Container on your server | **now** |
| **seller** SPA | **Cloudflare Pages** ✅ your existing workflow | **now** |
| **admin** SPA | **Cloudflare Pages** ✅ | **now** |
| media | Your server's disk, served by Caddy at `media.…` | **now** |
| **storefront** | Container on your server (it is SSR — see §9) | **Phase 2** |

Until Phase 2, `www.example.com` and vendor subdomains serve a small **"coming soon"** page from
Caddy (marked `noindex`, so search engines do not index the placeholder). Everything
storefront-shaped — the Dockerfile, the compose service, the Caddy proxy block, the CI step — is
already written, tested, and parked behind `PHASE 2` markers; §9 is the checklist for switching it
on. Nothing about the Phase 1 rollout has to be revisited when you do.

---

## 2. Target topology

```
                          ┌────────────────── Cloudflare ──────────────────┐
   seller.example.com ────┤  Pages project: lifestyle-seller               │
   admin.example.com  ────┤  Pages project: lifestyle-admin                │
   www.example.com    ────┤  proxied (orange) ──┐                          │
   *.example.com      ────┤  proxied (orange) ──┤ DNS · CDN · WAF · DDoS   │
   media.example.com  ────┤  proxied (orange) ──┤                          │
   api.example.com    ────┤  proxied (orange) ──┤                          │
                          └─────────────────────┼──────────────────────────┘
                                                │ Full (strict) TLS
                                                ▼
                          ┌────────────── your server ─────────────────────┐
                          │  Caddy :443  (Cloudflare Origin CA cert)       │
                          │    ├── /v1/*    → api        :8080             │
                          │    ├── media.…  → /srv/media (uploads, ro)     │
                          │    └── /*       → "coming soon" page           │
                          │                   (Phase 2: storefront :4000)  │
                          │  postgres · redis   (no published ports)       │
                          └────────────────────────────────────────────────┘
```

### 2.1 The proxy (Cloudflare's CDN) — you are not using it yet, and it is free

You said you are currently DNS-only. The proxy is not a separate product to buy: it is the
**orange-cloud toggle on each DNS record**, and it is included on the **Free plan** — CDN caching,
DDoS absorption, basic WAF, and it hides your origin IP. For an e-commerce storefront serving
Malaysia and Bangladesh from a Mumbai box, edge caching of images and static assets is exactly the
multi-region help you are after, at zero cost. (Pages traffic is already on Cloudflare's edge
regardless — this is about the records that point at *your server*.)

**Recommendation: turn the proxy on** for `api`, `www`, `@`, `media` and `*`. The guide below is
written for that mode, with the DNS-only differences called out. Two verifications before relying
on it, since plan features change: that your Free plan proxies the **wildcard** record (doc 03 §6.2
flags this as historically plan-gated), and the current upload body-size limit (must exceed our
10 MB media cap — it does today).

| | Proxied (orange) — recommended | DNS-only (grey) — your current state |
|---|---|---|
| CDN caching of media/assets | ✅ at Cloudflare's edge | ❌ every request hits Mumbai |
| DDoS / WAF | ✅ | ❌ |
| Origin IP | Hidden | Public in DNS |
| Origin TLS | Cloudflare **Origin CA** cert, 15-year, no renewals (§5.2) | Public **ACME** certs — delete the `import origin_tls` lines in the Caddyfile and Caddy does the rest |
| Wildcard `*.example.com` | One proxied record | No wildcard ACME cert without DNS-01; instead remove `*.{$ROOT_DOMAIN}` from the Caddyfile site list and let the `:443` on-demand catch-all issue **per-subdomain** certs (the `tls-check` endpoint approves registered shop slugs — built and tested) |
| SSL/TLS mode setting | **Full (strict)** — mandatory, see below | n/a |

Rolling it out is incremental: flip one record at a time (start with `media`), watch, continue.
Rate limiting in the app keys on client IP, so whenever you flip a record, the Caddyfile's
`trusted_proxies` (already configured) is what keeps real client IPs in the logs.

### The three settings that will bite you

**1. "Full (strict)" SSL mode, nothing less** *(proxied mode)*. Cloudflare's *Flexible* mode sends
traffic to your origin over **plain HTTP** — session cookies and passwords crossing the internet in
clear text, while the browser's padlock says everything is fine. Set SSL/TLS mode to
**Full (strict)** and give Caddy a **Cloudflare Origin CA certificate** (§5.2). The Caddyfile
expects it at `deploy/origin/cert.pem` + `key.pem`.

**2. The `Host` header decides which shop renders.** Tenant resolution (FRD §5.2) reads it.
Cloudflare preserves `Host` by default when proxying — good. But if you ever add an "Origin Rule"
that rewrites the Host header, or cache HTML without the host in the cache key, every storefront
quietly becomes the marketplace, returning 200 the whole way.

**3. The API is served under every hostname**, not just `api.example.com` — deliberately. The
refresh token is a `SameSite=Strict` HttpOnly cookie (FRD §4.2); on a vendor's own `xyz.com`,
calling `api.example.com` is cross-site and the browser will not send it — sessions would silently
fail exactly on the surface vendors care about. The Caddyfile routes `/v1/*` on every host, and the
storefront's `environment.production.ts` ships `apiBaseUrl: ''` to match.

---

## 3. Server prerequisites

```bash
curl -fsSL https://get.docker.com | sh
sudo apt-get install -y rsync         # used by backup.sh for the off-host copy (§6)
sudo usermod -aG docker "$USER"       # log out and back in

sudo mkdir -p /srv/lifestyle /var/backups/lifestyle
sudo chown "$USER" /srv/lifestyle /var/backups/lifestyle
git clone <repo> /srv/lifestyle && cd /srv/lifestyle
```

Cloudflare DNS for the zone:

| Record | Type | Target | Proxy |
|---|---|---|---|
| `api` | A | server IP | Proxied¹ |
| `@`, `www` | A | server IP | Proxied |
| `*` | A | server IP | Proxied (see §2.1) |
| `seller` | CNAME | Pages project | Proxied (Pages manages this) |
| `admin` | CNAME | Pages project | Proxied |
| `media` | A | server IP | Proxied |

¹ Proxying the API keeps your origin IP hidden and the WAF in front of it. One caveat: Cloudflare
enforces an upload body limit (100 MB on lower plans) — well above our 10 MB media cap, so fine
today; revisit if that cap ever grows.

**Firewall the origin.** With everything proxied, ports 80/443 only need to accept Cloudflare:

```bash
# ufw example — allow SSH, then 443 from Cloudflare's published ranges only
sudo ufw allow OpenSSH
for ip in $(curl -s https://www.cloudflare.com/ips-v4) $(curl -s https://www.cloudflare.com/ips-v6); do
  sudo ufw allow from "$ip" to any port 80,443 proto tcp
done
sudo ufw enable
```

**Exception:** if any vendor custom domain is DNS-only (§7, option B), their traffic comes from
the open internet, not Cloudflare — then 443 must stay open to all, and hiding the origin IP is
moot anyway since their DNS publishes it.

---

## 4. Frontends — Cloudflare Pages (your existing workflow)

You already run frontends on Pages with its git integration; the seller and admin apps work the
same way. Two projects, both watching this repository:

| Project | Production branch | Build command | Build output | Root dir |
|---|---|---|---|---|
| `lifestyle-seller` | `master` | `npx ng build seller --configuration production` | `dist/seller/browser` | `web` |
| `lifestyle-admin` | `master` | `npx ng build admin --configuration production` | `dist/admin/browser` | `web` |

Set the Node version to 24 (`NODE_VERSION` build variable). Attach `seller.example.com` and
`admin.example.com` as custom domains on the respective project — with the domain on Cloudflare
Registrar, Pages wires the DNS records for you.

Two files the repo must carry for Pages:

- **SPA fallback** — `web/projects/{seller,admin}/public/_redirects` containing
  `/* /index.html 200`. Angular copies `public/` into the build output. Without it, refreshing a
  deep link like `/products/123` returns Pages' 404.
- **Production API URL** — the SPAs are served from Pages hostnames, so they call
  `https://api.example.com` cross-origin:

  ```ts
  // web/projects/{seller,admin}/src/environments/environment.production.ts
  export const environment = { production: true, apiBaseUrl: 'https://api.example.com' };
  ```

  `Platform__CorsOrigins` in the compose file already lists exactly these two hosts — credentialed
  CORS with a wildcard is refused by design. *(The storefront keeps `apiBaseUrl: ''`: it is served
  by Caddy, same-origin, which is what lets the `SameSite=Strict` refresh cookie work on vendor
  custom domains.)*

**One consequence of auto-deploy to keep in mind:** Pages ships the SPAs on every push to
`master`, while the API ships when you tag (§8). A merged frontend change can therefore go live
against an API that does not have its counterpart yet. The discipline that makes this safe is the
one the FRD already commits to (§19.1): API changes within v1 are **additive only**, and a
frontend change that needs a new endpoint merges only after the API release carrying it is
deployed. If that ordering ever becomes annoying, the fallback is turning off auto-deploy and
publishing from the tag workflow instead — but start with what you already run.

---

## 5. Origin — the backend stack

### 5.1 First deployment

```bash
cd /srv/lifestyle
cp deploy/.env.example deploy/.env && chmod 600 deploy/.env
$EDITOR deploy/.env               # every blank; secrets via: openssl rand -base64 48

# Origin certificate — see 5.2 — into deploy/origin/{cert.pem,key.pem}

docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d postgres redis
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env \
  run --rm --no-deps api dotnet Lifestyle.Api.dll migrate
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env \
  run --rm --no-deps api dotnet Lifestyle.Api.dll seed

docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d
```

Then **delete `SEED_SUPERADMIN_PASSWORD` from `.env`** and sign in to enrol that account in TOTP —
the admin surface refuses sign-in without it.

Every release after that is `./deploy/scripts/deploy.sh my-prod <tag>` — backup, migrate, roll
containers health-gated, with the rollback command printed if a step fails.

### 5.2 Origin certificate

Cloudflare dashboard → **SSL/TLS → Origin Server → Create Certificate** → cover `example.com` and
`*.example.com` → place the PEMs at `deploy/origin/cert.pem` and `deploy/origin/key.pem` (mode
`600`). Then set the zone's SSL mode to **Full (strict)**.

This certificate is only trusted by Cloudflare — which is correct, since with the firewall from §3
nobody else can reach the origin. It lasts 15 years: no renewal automation to break. The `:443`
catch-all for DNS-only vendor domains still uses ACME independently.

### 5.3 Cloudflare cache rules

Cloudflare does not cache HTML by default — for the seller/admin SPAs and the API that default is
right, and in Phase 1 there is no storefront HTML to cache. **Never** add a cache rule covering
`/v1/*`. Storefront HTML caching is a Phase 2 topic (§9).

---

## 6. Media and backups — on your server, copies off it

**Media** stays on your server: `STORAGE_PROVIDER=local`. The API writes uploads to a Docker
volume; Caddy serves that volume **read-only** at `media.example.com` with immutable cache headers
and a sandboxing CSP (uploads are attacker-supplied bytes and must never execute in the platform's
origin). With the `media` record proxied (§2.1), Cloudflare caches every image at its edge — after
the first request per region, image traffic never reaches Mumbai. This is the strongest single
argument for the proxy on a multi-region audience.

Watch disk: vendor media shares the NVMe with the database, and doc 03 §7.2 calls disk the
constraint that bites first. `df -h` belongs in whatever monitoring you add (§10). If media
outgrows the box, `STORAGE_PROVIDER=s3` points the existing code at any S3-compatible service with
no code change — a decision for later, not now.

**Backups** are non-negotiable and must leave the machine (doc 03 §10.1: PropertyMart's backups sit
on the disk they protect, which survives a dropped table and nothing else). Since you are not using
cloud storage, the zero-new-services answer is **rsync over SSH to any second machine** — another
cheap VPS, or a PC that is on overnight:

```bash
# on the backup machine: a restricted user that can receive files
sudo useradd -m -s /usr/sbin/nologin lifestyle-backup
# then in deploy/.env on the server:
BACKUP_RSYNC_TARGET=lifestyle-backup@backup-host:/home/lifestyle-backup/lifestyle
```

Give the server a dedicated SSH key for that user, and prefer an append-only arrangement (e.g.
`rrsync -wo` in the key's `authorized_keys` options) so a compromised web server cannot delete the
history it is trying to hide. Then:

```bash
# crontab -e
0 2 * * * /srv/lifestyle/deploy/scripts/backup.sh        >> /var/log/lifestyle-backup.log  2>&1
0 4 * * 0 /srv/lifestyle/deploy/scripts/restore-test.sh  >> /var/log/lifestyle-restore.log 2>&1
```

`restore-test.sh` restores the newest dump into a scratch database and asserts the schema and
seeded roles exist — an empty dump also "restores fine". Alert on its exit code. A backup that has
never been restored is an assumption, not a backup.

With `STORAGE_PROVIDER=local`, the media volume needs the same treatment: add an rsync of
`/var/lib/docker/volumes/lifestyle-*_api-storage` (or a `docker cp` from the volume) to the nightly
cron — the database dump does not contain the images.

---

## 7. Vendor custom domains — decide before selling the feature

`{slug}.example.com` is covered by the wildcard. For `xyz.com` (Plan §4.3) there are two workable
routes, and doc 03 §6.2 anticipated both:

**A — Cloudflare for SaaS (Custom Hostnames).** The vendor CNAMEs to you; Cloudflare issues their
certificate and proxies their traffic with CDN + WAF, forwarding to your origin. Purpose-built for
this shape. Costs per hostname beyond the free allowance and needs a small API integration to
register hostnames when a vendor adds a domain. **Verify current pricing and plan gating** — this
product has changed repeatedly.

**B — DNS-only, straight to Caddy.** The vendor points an A record at your server; Caddy issues a
certificate on demand, gated by `GET /v1/internal/tls-check` (which answers 200 only for a
registered, approved vendor domain — the integration tests cover this). No per-hostname cost, no
registration step, working HTTPS as soon as DNS resolves. The vendor's domain gets no Cloudflare
protection, and your origin IP is visible in their DNS.

**Recommendation: start with B** — it ships with what is already built — **and adopt A when custom
domains become a real population** or a vendor demands DDoS/CDN on their own domain. The two
coexist fine: A-domains arrive via Cloudflare, B-domains hit the `:443` catch-all.

---

## 8. CI/CD

Two pipelines, deliberately separate:

- **SPAs** — Cloudflare Pages' own git integration, which you already use: every push to `master`
  builds and publishes seller and admin. Nothing to add beyond the project settings in §4.
- **Backend** — [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml), triggered by a
  `v*` tag:
  1. **images** — builds `lifestyle-api` once, pushes to GHCR tagged `<version>-<sha>`. What was
     tested is what ships. (The storefront image step is parked in the workflow behind a PHASE 2
     comment.)
  2. **server** — SSH to the host, `deploy.sh <deployment> <tag>` (backup → migrate →
     health-gated roll), then an external smoke test against `/v1/internal/health`.

GitHub configuration: secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY`; variable
`ROOT_DOMAIN`; an `environment` per deployment if you want a manual approval gate before the
server job. No Cloudflare credentials are needed anywhere in GitHub — Pages deploys itself.

### Migrations and rollback

Migrations run **before** the new code, as a one-off container, never at startup (docs/04 §4.4) —
safe only because migrations are **expand-only**: add nullable → backfill → contract in a *later*
release. Never drop or rename in the release that stops using a column. Rollback is then just
`deploy.sh <deployment> <previous-tag>` with no down-migration. CI's
`migrations has-pending-model-changes` gate stops a model change reaching the server without its
migration.

---

## 9. Phase 2 — switching the storefront on

Everything is built and parked; this is the whole procedure when the storefront phase starts.

**Why it cannot go on Pages, recorded for when you decide:** `apps/storefront` is Angular **SSR** —
its build emits `dist/storefront/server/server.mjs`, a Node server, not static files. SSR is there
because organic search is the storefront's acquisition channel (Plan §2.5) and WhatsApp link
previews (the v1 order path, Plan §6.2) do not execute JavaScript — a client-only storefront loses
the preview card on every shared product link. If at Phase 2 you still prefer to avoid SSR, the
honest alternative is stripping the storefront to a SPA and accepting those two losses; running the
SSR on Workers is *not* a good middle path, because every render would then cross from Cloudflare's
edge to the database in Mumbai.

The switch itself (every touch point is marked `PHASE 2` in the file):

1. `.github/workflows/deploy.yml` — un-comment the **Storefront SSR image** build step.
2. `deploy/docker-compose.prod.yml` — un-comment the `storefront` service and restore it to
   Caddy's `depends_on`.
3. `deploy/Caddyfile` — replace `import coming_soon` with `import storefront_proxy` on the
   marketplace/wildcard site and the `:443` catch-all.
4. `deploy/scripts/deploy.sh` — add `storefront` back to the pull and add the roll step after the
   API.
5. Tag a release. Verify: `www` renders the marketplace, `{slug}.…` renders that shop (the
   `Host`-forwarding behaviour is already in the proxy snippet and is what makes this work).
6. Optionally add the storefront HTML cache rule: Edge TTL 30–60 s on `www.…/*` and `*.…/*`,
   bypass on cookie `lf_rt`, never `/v1/*`. A stale price is worse than a slower page — when in
   doubt, cache only static assets.

---

## 10. Go-live checklist

- [ ] Cloudflare SSL mode is **Full (strict)** — not Flexible, not Full
- [ ] `https://api.example.com/v1/internal/health` → `Healthy`
- [ ] `/v1/internal/tls-check?domain=anything.invalid` → **404** (200 = open certificate relay)
- [ ] `www` and any subdomain serve the "coming soon" page (storefront lands in Phase 2 — §9)
- [ ] Seller SPA deep link survives a hard refresh (`_redirects` in place)
- [ ] Sign in on seller SPA, reload, still signed in (cross-origin cookie + CORS working)
- [ ] Buyer token rejected by `/v1/admin/*`
- [ ] `nmap -p 5432,6379 <server-ip>` from outside: closed
- [ ] Direct-to-IP requests are refused or useless (firewall §3, or at minimum origin cert only)
- [ ] Backup ran, the copy landed on the backup machine, **and** `restore-test.sh` passed
- [ ] `SEED_SUPERADMIN_PASSWORD` removed from `.env`; admin enrolled in TOTP
- [ ] Media upload → image URL serves from `media.example.com`

## 11. Still open

| Gap | Consequence | When |
|---|---|---|
| **No monitoring/alerting** (doc 03 §10.2) | You learn of outages from vendors. An uptime check + disk alert is an hour's work and the highest-value item here | Before launch |
| **Single server** (doc 03 §10.4) | Host loss = total outage. Tolerable at launch with tested off-host backups; not once money flows | Before Phase 4 |
| **Seller/admin UI screens** | The Pages projects deploy shells; onboarding and moderation are API-only today | Next |
| **Email** | No `IEmailSender` implementation yet | Before buyer accounts matter |
| **Cloudflare IP list drift** | Caddy's `trusted_proxies` and the ufw rules embed today's ranges; stale ranges mis-attribute client IPs | Re-check quarterly |
