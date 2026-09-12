# Lifestyle — Deployment Deviations & Temporary State

| Field | Value |
|---|---|
| Document | 07 — Deployment Deviations & Temporary State |
| Version | 1.0 |
| Date | 2026-09-06 |
| Purpose | Everything currently running that differs from [05 — Deployment](05-DEPLOYMENT.md) / [06 — Phase 1 Go-Live](06-PHASE1-GO-LIVE.md), and exactly how to get back to the documented plan |
| Deployment | `bd-prod` on `94.136.186.220`, domain `mylifestylemart.com` |

**Why this file exists.** The live deployment was shaped by two facts the design documents did not
anticipate: the server is *shared* with three other products, and a client demo was needed before
Phase 2. Both forced choices that are correct for today and wrong for the end state. Each one is
recorded below with its trigger for removal, so none of them becomes permanent by forgetting.

Legend — **P** permanent (the design doc is what should change) · **T** temporary (revert on the
stated trigger) · **G** gap (never done, still owed).

---

## 1. Caddy replaced by the host's nginx — **P**

**Documented:** a Caddy container in `deploy/docker-compose.prod.yml` owns ports 80/443, terminates
TLS with a Cloudflare Origin CA certificate, and serves media plus the storefront (docs/05 §5).

**Actual:** the host already runs **nginx** on 80/443 for four live APIs — PropertyMart
`bd-prod`/`bd-staging`/`my-prod` and SasthoSeba staging — each proxying to a container on
`127.0.0.1:5X00`. Starting our Caddy would fight them for those ports.

**Carried by:** [`deploy/docker-compose.host-nginx.yml`](../deploy/docker-compose.host-nginx.yml)
(parks `caddy` behind a compose profile, publishes the API on `127.0.0.1:5600`, bind-mounts media)
and [`deploy/scripts/deploy-host-nginx.sh`](../deploy/scripts/deploy-host-nginx.sh).

> ⚠ **Never run `deploy/scripts/deploy.sh` on this host.** It ends with `up -d --no-deps caddy`,
> and naming a profiled service on the command line is exactly what starts it. Use
> `deploy-host-nginx.sh`.

**To revert** (only if Lifestyle moves to a dedicated host): drop the `-f docker-compose.host-nginx.yml`
override, restore `deploy/Caddyfile`, use `deploy.sh`. Nothing else changes.

**nginx sites live on the server** (not in git — see gap G3):
`api.mylifestylemart.com`, `media.mylifestylemart.com`, `mylifestylemart.com` (apex + www).

---

## 2. Media served from a host bind mount, not a named volume — **P**

The stock stack keeps uploads in the `api-storage` Docker volume and lets Caddy serve it. nginx
cannot read a named volume (`/var/lib/docker` is not traversable by `www-data`), so `/app/.storage`
is bind-mounted to **`/var/data/lifestyle/bd-prod/uploads`**, owned by uid `64198`, matching the
`/var/data/<app>/<env>/uploads` layout the other stacks on this host use.

Consequence for backups: that directory is **not** in the database dump. See G1.

---

## 3. Storefront: browser-only build, no SSR — **T**

**Documented:** the storefront is Angular **SSR**, a Node server behind Caddy, deferred to Phase 2
(docs/05 §9). SSR exists because organic search is the acquisition channel and WhatsApp link
previews do not execute JavaScript.

**Actual:** to have something to show a client, the storefront is built browser-only and served as
static files by nginx at the apex.

**Cost while this stands:** no server-rendered HTML — search engines and WhatsApp previews see an
empty shell. Acceptable for a demo, **not** acceptable at launch.

**Revert trigger:** starting Phase 2 properly. Follow docs/05 §9, then in the apex nginx site
replace the static `location /` with `proxy_pass http://127.0.0.1:4000` (the block is marked
`PHASE 2` in the file).

---

## 4. Apex holding page — **superseded 2026-09-06**

The apex briefly served a static "coming soon" page from `/var/www/lifestyle-coming-soon/`. It now
serves the storefront (item 3) from `/var/www/lifestyle-storefront/`. The old directory is still on
the host and can be deleted.

The apex also proxies `/v1/*` to the API. That is **deliberate and permanent**: the refresh token is
a `SameSite=Strict` cookie, so a storefront served from this host must call the API same-origin or
every session silently breaks (docs/05 §2, note 3).

---

## 5. Demo catalog data in the production database — **T** ⚠

Demo vendors and products were created **through the real API flow** (register → apply → KYC upload
→ submit → admin approve → create product → submit → moderate), so they are indistinguishable from
real records *by construction* — that was the point, but it is also the risk.

**Every demo record is identifiable by its `demo-` slug prefix and `@demo.mylifestylemart.com`
contact email.** Do not rely on memory: query before deleting.

**What exists** — 3 shops and **24 products** (11 created 2026-09-06, 13 more 2026-09-12), all `Published`:

| Shop | Slug | Vendor id |
|---|---|---|
| Arunima Boutique | `demo-arunima` | `01a07709-4e6e-7def-8a6b-c23baef27881` |
| StepUp Footwear | `demo-stepup` | `01a07709-65d4-7f11-8532-f5b6d36290d0` |
| GadgetHub BD | `demo-gadgethub` | `01a07709-783f-7b59-b3ce-98218ec738a2` |

Their login accounts are `demo-<key>@demo.mylifestylemart.com`. The WhatsApp numbers
(`+88017110001xx`) are test ranges, not anyone's real line. Product imagery is generated
placeholder art — gradient silhouettes, not photographs — so nothing here carries a licence.

**Revert trigger:** before the first real vendor is onboarded. Remove demo vendors and their
products, or reset the database entirely — no real data exists yet, so a clean re-`migrate` +
`seed` is the safest option while that remains true.

---

## 6. Google sign-in gated on absent configuration — **T**

Being built for the **buyer and seller surfaces only**; admin stays password + TOTP deliberately, so
a compromised Google account cannot become a compromised platform admin. Accounts auto-link to an
existing password account when Google asserts `email_verified`.

Dormant until `Identity__Google__ClientId` is set in `deploy/.env` — the endpoint returns
`identity.google_not_configured` and the SPA hides the button. **Owed by the user:** a Google Cloud
OAuth 2.0 Client ID with `https://seller.mylifestylemart.com` and `https://mylifestylemart.com` as
authorised JavaScript origins.

---

## 7. Cloudflare Pages project naming — **T** (cosmetic)

`seller.mylifestylemart.com` is served by a project named **`lifestyle`**, not `lifestyle-seller`
as docs/05 §4 specifies; admin is correctly `lifestyle-admin`. Pages cannot rename a project, so
fixing it means creating a new project and re-pointing the domain. Harmless; recorded so the name
is not mistaken for the marketplace/storefront project.

Both build from the **`prod`** branch, not `master` as docs/05 §4 says — deliberate, so the SPAs and
the API ship from one branch and cannot drift apart.

---

## 8. DNS is DNS-only (grey), not proxied — **T**

`api`, `media`, apex and `www` point straight at the origin; only the two Pages CNAMEs are proxied.
This matches the sibling projects and let certbot issue certificates against a directly reachable
origin.

**Revert trigger:** whenever CDN/WAF is wanted — worth doing for `media` first, since edge caching
of product images is the biggest single win for a Bangladesh audience. Flipping to proxied is safe
now (Let's Encrypt certificates are publicly trusted, so Full (strict) validates), but **add
Cloudflare's ranges as `set_real_ip_from` plus `real_ip_header CF-Connecting-IP` in the same
change** — otherwise every request is attributed to Cloudflare and rate limiting keys on their IPs.
A placeholder comment marks the spot in the api site file.

---

## Gaps — never done, still owed

| # | Gap | Consequence |
|---|---|---|
| **G1** | **No off-host backup.** `BACKUP_RSYNC_TARGET` is blank and no cron is installed. `backup.sh` and `restore-test.sh` both pass by hand. The media bind mount (item 2) is not in the dump either. | Losing the host loses everything. docs/03 §10.1 calls this the single most important gap. **Do before the first real vendor.** |
| **G2** | **No monitoring.** No uptime check on `/v1/internal/health`, no disk alert. | You learn of outages from vendors. |
| **G3** | **nginx site files are not in the repo.** They live only on the server; the repo still carries only `deploy/Caddyfile`. | A host rebuild loses them. Copy them into `deploy/nginx/` and reference them from docs/05. |
| **G5** | **Three PropertyMart certificates use `authenticator = standalone`**, which needs port 80 free — nginx holds it. Not Lifestyle's, but on the same box. | Those renewals will likely fail. Convert them to `--webroot`. |
| **G6** | **The server login password was briefly written into two nginx files** by a `sudo -S` stdin mistake, then overwritten. It is also in this session's shell history. | Rotate the `deploy` password. |

---

---

## Storefront coverage — what the marketplace does and does not do

Brought to demo standard 2026-09-12. Working: search (query-string driven, so a filtered grid
survives a reload and can be shared), category filter, sort, deal badges with struck was-prices,
availability bands, the shop directory, a shop profile in the vendor's own accent, and the product
page with variant pills and the WhatsApp order deep link.

**Not built:** paging (the grid takes the first 48 and stops), buyer accounts, saved items, cart or
checkout — v1 ordering is WhatsApp by design (Plan §6.2) — reviews, and vendor custom domains.

**Product imagery is drawn, not photographed.** `scratchpad/art.mjs` renders flat SVG
illustrations per product type and colourway, rasterised to PNG. They read as catalogue art rather
than as broken images, and they carry no licence risk — but they are not photographs, and a client
should be told that rather than left to assume. Replace them as vendors upload real product shots.

## Console coverage — what the portals do and do not do

Built 2026-09-06. Both consoles are real applications now, but neither is complete:

**Seller** — sign in, apply for a shop, upload KYC, submit, see review status, list products,
create a product (category → attribute set → variants), submit for review, edit shop settings.
**Not built:** editing an existing product, stock adjustment (`adjustStock` exists in the service
and is unused), image reordering, logo/banner upload, staff management.

**Admin** — sign in with TOTP, work the vendor queue (approve/reject with reason, open KYC
documents), work the moderation queue (publish, reject, take down). **Not built:** vendor
suspension (`setSuspension` exists in the service and is unused), category management, the audit
log at `/v1/admin/audit`, and any paging beyond the first 100 rows.

Neither console has automated tests.

## Credentials and one-time values

Held outside this file, in the user's hands:

- **Two** super-admin accounts now exist: `naimelias45@gmail.com` (generated password) and
  `admin@mylifestylemart.com`. Both are TOTP-enrolled and both are full super-admins — decide
  whether the first should stay.
- **TOTP secrets** were shown once at enrolment and the API will not show them again. They must be
  in the user's authenticator app.
- `SEED_SUPERADMIN_PASSWORD` is blanked in `deploy/.env`.
- Two Cloudflare API tokens were used (DNS edit, Pages edit). **Revoke both** once this work settles.
