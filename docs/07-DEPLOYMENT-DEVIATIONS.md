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

## 6a. Admin two-factor is OFF — **T**, owner's decision 2026-09-13

`REQUIRE_ADMIN_2FA=false` in `deploy/.env`, so `admin@mylifestylemart.com` signs in with email
and password alone. The setting skips **both** halves of the gate — mandatory enrolment and the
code — because leaving the code required would still lock out the account, which is already
enrolled.

Every other guard is unchanged: a wrong password is still a 401, a non-admin still gets
`identity.surface_not_permitted`, and the seller surface still asks for a code from anyone who
turned two-factor on.

**Why it is acceptable now:** the platform holds three demo shops and no buyers, and there is
nothing behind the admin console worth stealing. **Revert trigger:** before the first real vendor
uploads a KYC document. An admin can approve vendors, moderate the catalogue and take shops down,
so at that point a stolen password alone must not be enough. One line in `deploy/.env` plus a
container recreate puts it back.

## 7. Migration files are generated, never hand-edited — **P**

A hand edit to a file under `Persistence/Migrations/` desynchronises the migration, its
`.Designer.cs` and `AppDbContextModelSnapshot.cs`, and the damage surfaces later as a bogus or
empty migration. Use `dotnet ef migrations add` / `remove`. Recorded as a rule in CLAUDE.md §8.

Notes that would otherwise go in a migration file belong here. For `20260912221930_ExternalLogins`:
it is expand-only (`password_hash` widened to nullable, new table added), but its `Down` path is
lossy by necessity — re-tightening the column has to put something in the null rows, and EF uses an
empty string, leaving any Google-created account with an unusable hash. Roll forward, not down.

**The three migrations are not squashed.** Squashing would need either a destructive reset of the
live database or hand-editing `__ef_migrations_history` — the second being exactly the manual
migration surgery this rule exists to prevent. Squash at the next clean-slate moment, before real
data exists, not while `bd-prod` is serving.

## 6. Google sign-in — **built 2026-09-13**

Live on the **buyer and seller** surfaces. The admin console refuses it outright — in the validator
and again in the handler — because an admin can approve vendors, moderate the catalogue and take
shops down, and a second way in means a compromised Google account is a compromised platform.

An account resolves by Google's immutable `sub` first, then by matching verified email, then by
creating one. Linking on email is safe **only** because the validator rejects a token whose
`email_verified` is false; without that check this would be an account-takeover primitive.

Enabled by `GOOGLE_CLIENT_ID` in `deploy/.env`. Blank disables it — the endpoint answers
`identity.google_not_configured` and the button does not render, rather than half-working.

**Owed:** the client id must be listed in Google Cloud with `https://seller.mylifestylemart.com`
(and the marketplace origin, once buyers can sign in there) as authorised JavaScript origins, or
Google refuses to render the button. **A Google-only account cannot set a password** —
`ChangePassword` answers `identity.password_not_set`; a first-password flow is not built.

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
| **G1** | **Backups never leave the host — accepted risk, owner's decision 2026-09-13.** A nightly dump + media archive and a weekly restore test run on cron (2am / 4am Sunday), and the dump carries the uploads alongside it, but `BACKUP_RSYNC_TARGET` and `BACKUP_S3_BUCKET` are empty, so every copy sits on the same disk as the data. | Defensible **only** while the platform holds nothing real: today that is three demo shops and no buyers. It stops being defensible the moment a vendor uploads a KYC document, because losing the host then loses identity documents that cannot be re-created. **Revisit before onboarding the first real vendor** — set one of the two targets in `deploy/.env` and the existing script does the rest. |
| **G2** | **No monitoring.** No uptime check on `/v1/internal/health`, no disk alert. | You learn of outages from vendors. |
| **G3** | **nginx site files are not in the repo.** They live only on the server; the repo still carries only `deploy/Caddyfile`. | A host rebuild loses them. Copy them into `deploy/nginx/` and reference them from docs/05. |
| **G5** | **Three PropertyMart certificates use `authenticator = standalone`**, which needs port 80 free — nginx holds it. Not Lifestyle's, but on the same box. | Those renewals will likely fail. Convert them to `--webroot`. |
| **G6** | **The server login password was briefly written into two nginx files** by a `sudo -S` stdin mistake, then overwritten. It is also in this session's shell history. | Rotate the `deploy` password. |

---

---

## Localisation

The storefront is **bilingual (English + Bangla)**, locale remembered per visitor, defaulting from
the browser. Prices render in the reader's numerals — ৳১,৮৯০ for a Bangla reader — via `Intl` on a
`bn-BD` tag. Platform category names are translated by slug; the WhatsApp order message is composed
in the buyer's language, since it is read by a Bangladeshi seller.

**Not localised, deliberately:** vendor-supplied text — product names, descriptions, shop
`about` — is whatever the seller typed, in whatever language they typed it. Translating it would
either invent content or mistranslate a seller's own words. Per-locale vendor content is a schema
feature, not a UI one, and is not built.

**The seller and admin consoles are English-only.** The i18n mechanism is shared and ready, so
translating them is mechanical, but their strings are not in a catalogue yet. That is a real gap
for Bangladeshi sellers and should not be left indefinitely.

## Storefront coverage — what the marketplace does and does not do

Brought to demo standard 2026-09-12. Working: search (query-string driven, so a filtered grid
survives a reload and can be shared), category filter, sort, deal badges with struck was-prices,
availability bands, the shop directory, a shop profile in the vendor's own accent, and the product
page with variant pills and the WhatsApp order deep link.

**Not built:** buyer accounts, saved items, cart or checkout — v1 ordering is WhatsApp by design
(Plan §6.2) — reviews, and vendor custom domains. Paging now works (24 at a time, with a count).

**Product imagery is drawn, not photographed.** `scratchpad/art.mjs` renders flat SVG
illustrations per product type and colourway, rasterised to PNG. They read as catalogue art rather
than as broken images, and they carry no licence risk — but they are not photographs, and a client
should be told that rather than left to assume. Replace them as vendors upload real product shots.

## Console coverage — what the portals do and do not do

Built 2026-09-06. Both consoles are real applications now, but neither is complete:

**Seller** — sign in, apply for a shop, upload KYC, submit, see review status, list products,
create a product (category → attribute set → variants), submit for review, edit shop settings.
Product editing now works: details, per-variant price and visibility, stock as a delta, image
add/reorder/remove, submit, unpublish and delete. Editing is refused while a product is with a
moderator. Shop logo and banner upload now work. **Not built:** staff management, bulk CSV import.

**Admin** — sign in with TOTP, work the vendor queue (approve/reject with reason, open KYC
documents, suspend and reinstate a shop), work the moderation queue (publish, reject, take down).
Category management (create, rename, reorder, show/hide, assign attribute set) and the audit log
(filterable, paged, read-only) are now built. **Not built:** paging beyond the first 100 rows on
the vendor and moderation queues.

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
