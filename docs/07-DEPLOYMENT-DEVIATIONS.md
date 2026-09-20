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

## 9. Seller and admin consoles are served from the host, not Cloudflare Pages — **T**, live 2026-09-13

The plan (docs/06 step D1) puts both consoles on Pages, and the two projects exist —
`lifestyle` and `lifestyle-admin`. They are not what serves the live hostnames.

Direct upload is refused with the Pages-scoped token on hand: `wrangler` fails its `/accounts`
lookup with *Invalid access token*, and `GET /accounts/{id}/pages/projects/lifestyle/upload-token`
answers `success: false, jwt: "Authentication error"`. Git-integration deploys would have built
`origin/prod`, which is many commits behind local — so a Pages deploy could only have published a
console *older* than the one already there. With seller sign-up blocked on getting a current
bundle out, the consoles moved to the host that already serves the storefront.

Both are static bundles under `/var/www/lifestyle-{seller,admin}`, behind site files vendored in
`deploy/nginx/`. Each proxies `/v1/*` same-origin the way the apex does, and carries
`X-Robots-Tag: noindex`.

DNS was moved with them: `seller` and `admin` are now **A records to 94.136.186.220, DNS-only**,
not proxied CNAMEs to `*.pages.dev`. Let's Encrypt certificates were issued by certbot
`--webroot` on 2026-09-13 and expire 2026-12-12. The two Pages projects still exist and still
hold the custom-domain entries; they serve nothing.

Publishing is two steps, and the split is deliberate — **the bundle is built on the workstation,
never on the VPS**. There is no node on that host, and an Angular build would peak past what is
comfortable to spend on a box already running five production stacks in 12 GB:

```bash
cd web && npx ng build seller && tar -C dist/seller/browser -czf /tmp/seller.tgz .
scp /tmp/seller.tgz deploy@94.136.186.220:/tmp/
ssh deploy@94.136.186.220 'cd ~/lifestyle-bd-prod && ./deploy/scripts/publish-console.sh seller /tmp/seller.tgz'
```

**Cost of keeping it:** no CDN in front of the consoles, and no build-on-push — publishing is a
manual two-step until Pages works again.

**Revert trigger:** a Cloudflare token with `Account · Cloudflare Pages · Edit`, *and* `prod`
pushed up to date. Then `wrangler pages deploy web/dist/<app>/browser`, re-add the custom domain
in the Pages project, point `seller` and `admin` back at their `*.pages.dev` CNAMEs (proxied),
`rm -rf /var/www/lifestyle-{seller,admin}`, and remove the two site files.

---

## 10. No vendor gets a working shop URL — **G**, found 2026-09-16

**Not even the three demo shops have one.** `demo-arunima.mylifestylemart.com` is NXDOMAIN today —
confirmed by direct lookup, not inferred from docs. Every vendor's shop address, the one the seller
onboarding form promises ("Your shop will be at `uttara-dhaka.mylifestylemart.com`" —
[onboarding.ts](../web/projects/seller/src/app/onboarding/onboarding.ts) `addressPreview()`), does
not resolve for anyone.

**Nothing on the code side is missing.** `TenantResolutionMiddleware.cs` already resolves any
`{slug}.mylifestylemart.com` generically from the database — no per-shop code or config — and the
storefront's `ShellStore.resolve()` derives shop-vs-marketplace chrome and branding purely from the
API's answer to the `Host` it was called on. A vendor's site works the moment the edge routes their
subdomain to the same storefront bundle and API the apex already serves.

**Why it never got built:** docs/05 §Deployment's plan was Caddy with on-demand TLS, checked against
the `tls-check` endpoint, which issues a certificate per registered slug automatically as each shop
is approved. §1 above replaced Caddy with the host's nginx to avoid fighting three other products
for ports 80/443 — a necessary and permanent call — but nothing replaced the wildcard/on-demand
piece that decision took with it. It was never a deliberate deferral; it was dropped silently and
nobody re-solved it, which is exactly the failure mode this document exists to catch.

**What is missing, concretely:**

1. **DNS** — a `*.mylifestylemart.com` record. DNS-only (grey), matching every other record on this
   zone (§8) — Cloudflare's free plan does not proxy wildcards anyway (docs/03 §6.2).
2. **A wildcard TLS certificate.** Every cert on this host so far is certbot `--webroot` (HTTP-01),
   which **cannot** issue a wildcard — Let's Encrypt requires a DNS-01 challenge for `*.` names, which
   needs API write access to the zone (the `certbot-dns-cloudflare` plugin plus a scoped Cloudflare
   API token: `Zone → DNS → Edit`, restricted to this zone). Nothing currently holds that token.
3. **An nginx site.** Written and vendored, **not installed**:
   [`deploy/nginx/wildcard.mylifestylemart.com.conf`](../deploy/nginx/wildcard.mylifestylemart.com.conf) —
   a `server_name ~^[^.]+\.mylifestylemart\.com$` block, safe to add because a literal `server_name`
   (seller, admin, api, media, the apex) always wins over a regex match in nginx regardless of load
   order, so it cannot hijack those five hosts. It reuses the apex's storefront-serving config
   unchanged; the app itself needs no per-subdomain build.

**Not this gap:** vendor-owned custom domains (`theirshop.com`) are a separate, larger feature —
`CheckCustomDomain.cs` / the `tls-check` endpoint exists for it but nothing issues those certificates
either. This gap is only about the platform-provided `{slug}.mylifestylemart.com` address every
vendor is promised at signup.

**Revert trigger:** none — this is net-new infrastructure, not a reversion. Closed by: the DNS
record, a Cloudflare API token scoped to this zone, one `certbot certonly --dns-cloudflare …
-d '*.mylifestylemart.com'` run (plus its renewal hook — DNS-01 renewals cannot use the existing
webroot hook), and installing the site file per the README's normal procedure. Until then, a vendor's
shop is reachable only at `mylifestylemart.com/shop/{slug}` (built, working, but not what onboarding
promises) — worth using as a stand-in if a demo is needed before this is closed.

---

## 11. Password reset and first-password — **built 2026-09-16**

Closes the code half of G4, and the "a Google-only account cannot set a password" note in §6.

`POST /v1/auth/forgot-password` mails a single-use link; `POST /v1/auth/reset-password` redeems it;
`POST /v1/me/set-password` gives an account that has only ever used Google its first password.
Seller console screens for all three; the settings screen now reads `hasPassword` off the session
and offers "set" or "change" accordingly instead of dead-ending on `identity.password_not_set`.

**No migration.** Reset tokens live in `IDistributedCache` — Redis in production — keyed by the
token's SHA-256 hash, the same way `EnrolTwoFactor` holds enrolment secrets. Only the hash is
stored, so the store cannot be read to reset anyone's password. They expire in an hour
(`Identity:PasswordResetMinutes`) and are consumed on first use.

Three behaviours worth knowing, each deliberate:

- **`forgot-password` always answers 202** — unknown address, suspended account, dead relay alike.
  Anything else is a free oracle for which addresses sell here. `Register` makes the opposite call
  on purpose, because there an honest user needs to be told the address is taken.
- **A reset clears the lockout.** Someone resetting has usually just burned their attempts
  guessing, and refusing the password they set seconds ago is a support ticket with no security
  benefit — they have proved control of the mailbox, which is the stronger claim.
- **It does not sign the caller in.** Issuing a session would mean stepping around the TOTP gate on
  an account that may have it enabled — precisely what a stolen link would want.

**Owed:** `SMTP_HOST` and friends in `deploy/.env` (see `.env.example`). A blank host does not fail
the boot; it selects a sender that logs each message and delivers none, which is visible in the log
and invisible to the vendor waiting for a link. Set it before the first real vendor.

Verified against the live development database on 2026-09-16, by walking the flow rather than by
unit test: request, redeem, single-use refusal on replay, old password dead, new password signs in,
lockout cleared, and `set-password` refused on an account that already has one.

---

## 12. Vendors are told what happened to their application — **built 2026-09-17**

Approving or rejecting a shop used to be silent. An approved owner had to keep signing in to
notice; a rejected one was never told why, and so could not fix it and reapply. The decision was
recorded all along — only the telling was missing.

`VendorApprovedEvent` already existed and was already mapped; **nothing had ever registered a
handler for it**. `VendorRejected` was in-module only, so it gained an integration event, a map
entry, and `DisplayName`/`OwnerUserId` on the domain event.

**Both run off the outbox, not inline in the review handler.** That is the point: the mail goes out
only if the decision actually committed. Sending before `SaveChanges` risks congratulating someone
whose approval then rolled back; sending after risks losing it if the process dies. Delivery is
at-least-once, so a duplicate notice is possible and accepted — deduplicating would cost a table and
a migration to prevent an email that says the same true thing twice.

`IEmailSender` **moved from `Identity/Internal/` to `SharedKernel/Abstractions/`** the moment
Vendors needed it, which is exactly what the admission rule prescribes (docs/04 §3.2: two or more
modules, no dependency on any module).

Everything interpolated into the HTML bodies is `HtmlEncode`d. A shop's display name is chosen by
the vendor and a rejection reason typed by a reviewer; scripts do not run in a mail client, but
unencoded markup can still restructure a message into one carrying a link that appears to be ours.

**`SHOP_URL_TEMPLATE` substitutes `{slug}` and defaults to the path form**,
`https://{root}/shop/{slug}` — because §10 means `https://{slug}.{root}` would send every newly
approved vendor to a dead link. Switch that one variable once the wildcard is live.

Verified on 2026-09-17 against the live development database: applied, approved, and the mail
dispatched from the outbox to the owner's address; then a second application rejected, and the
reviewer's reason present in the message.

**Still not built:** email verification, and delivering credentials for an admin-created shop (§9)
— though password reset (§11) now gives such an owner a self-service route that needs no password
hand-off at all.

---

## Gaps — never done, still owed

| # | Gap | Consequence |
|---|---|---|
| **G1** | **Backups never leave the host — accepted risk, owner's decision 2026-09-13.** A nightly dump + media archive and a weekly restore test run on cron (2am / 4am Sunday), and the dump carries the uploads alongside it, but `BACKUP_RSYNC_TARGET` and `BACKUP_S3_BUCKET` are empty, so every copy sits on the same disk as the data. | Defensible **only** while the platform holds nothing real: today that is three demo shops and no buyers. It stops being defensible the moment a vendor uploads a KYC document, because losing the host then loses identity documents that cannot be re-created. **Revisit before onboarding the first real vendor** — set one of the two targets in `deploy/.env` and the existing script does the rest. |
| **G2** | **No monitoring.** No uptime check on `/v1/internal/health`, no disk alert. | You learn of outages from vendors. |
| **G3** | ~~nginx site files are not in the repo.~~ **Closed 2026-09-06** — all five live site files are vendored in `deploy/nginx/` with an install note. Keep copying a change back into the repo in the same session you make it on the server, or this reopens quietly. | — |
| **G4** | ~~**No `IEmailSender`.**~~ **Built 2026-09-16/17 — see §11 and §12.** Password reset, first-password for Google-only accounts, and vendor approval/rejection notices all work end to end. **Still owed: `SMTP_HOST` in `deploy/.env`.** Until it is set, every message is written to the log instead of being delivered, and none of it reaches anyone. Still not built: email verification, and delivering the password for an admin-created account (§9) — though §11 gives such an owner a self-service route instead. | Set the SMTP variables and all of it works. Left blank, a vendor still hears nothing and a forgotten password still has no self-service route — but the code is no longer what stands in the way. |
| **G5** | **Three PropertyMart certificates use `authenticator = standalone`**, which needs port 80 free — nginx holds it. Not Lifestyle's, but on the same box. | Those renewals will likely fail. Convert them to `--webroot`. |
| **G6** | **The server login password was briefly written into two nginx files** by a `sudo -S` stdin mistake, then overwritten. It is also in this session's shell history. | Rotate the `deploy` password. |
| **G7** | **No vendor shop subdomain resolves — see §10.** DNS wildcard, wildcard TLS cert and the nginx site are all missing; the code side is done. | Onboarding promises `{slug}.mylifestylemart.com` and cannot deliver it. **Blocks the "must" of a new shop having its own URL** — needs a Cloudflare API token before it can be closed. |
| **G8** | **KYC documents, shop logos and banners uploaded before 2026-09-19 were deleted 24 hours after upload.** `MediaOrphanSweeper` deletes any media with no `OwnerType`, and Vendors never claimed its uploads — it had no reference to Media and could not. Fixed on 2026-09-19 (Vendors now calls `AttachAsync` with `vendor_document` / `vendor_branding`), but **the fix is forward-only**. | Any vendor document or logo older than 24 hours at the time of the fix is gone from storage, with a `VendorDocument` row still pointing at it — and per G1 there is no off-host backup to restore from. **Owed:** a one-off backfill that attaches any still-present media referenced by `VendorDocument.MediaId`, `LogoMediaId` or `BannerMediaId` (it rescues anything uploaded within the last 24 hours), then a check of which live applications now have dangling ids so those vendors can be asked to re-upload. |
| **G9** | **Identity documents have been uploaded through a public path.** The image library on `bd-prod` currently lists a passport bio page, a passport personal-data page and an NID. Only `onboarding.uploadDocument` marks an upload `private`; the product-image pickers and the logo/banner picker all upload public, and nothing stops a seller choosing an ID document in one of them. A public upload is served from the media host with no authentication. | Those three files are on public, CDN-cacheable URLs. **Delete them from the image library** (the delete also removes them from storage). Longer term the seller console should refuse an upload that is obviously a document, or at least warn — there is no technical detection today, so the current defence is the KYC picker being the only private path and sellers not misusing the others. |

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

The bulk import and image library screens added on 2026-09-19 follow the same rule and so add to
this debt — deliberately, to match the consoles around them rather than leave one screen half
translated. It is now the largest English-only surface a seller has to work through, and it is the
screen where a misunderstanding costs the most: a seller who misreads "this will take your product
off the storefront" confirms something they did not mean. **Translate the consoles starting with
`products/import`.**

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
moderator. Shop logo and banner upload now work.

**Seller onboarding was unreachable until 2026-09-13** and is worth recording, because the shape of
the bug will recur. The console always signed in with `surface: 'seller'`; a new account has no
vendor, so `PermissionResolver` refused that surface — and the application form that *creates* the
vendor sat behind that login. There was also no registration screen at all. The API was never
wrong: the vendor-application endpoints deliberately accept any signed-in token so approval is
reachable. The console now registers, signs an applicant in on `buyer`, and upgrades the session
in place once the shop is approved.

**Changing a password** was added 2026-09-13 (Shop settings → Password). It matters because a
shop an admin opens starts with a password that admin generated and read out; until the owner
replaces it, two people can sign in as them. The endpoint revokes every session, so the console
signs out afterwards and the login screen says why.

**Not built:** staff management, bulk CSV import, password *reset* — there is no email sender
(G4), so an owner who forgets theirs needs an admin to intervene, and no screen does that yet.

**Admin** — sign in with TOTP, work the vendor queue (approve/reject with reason, open KYC
documents, suspend and reinstate a shop), work the moderation queue (publish, reject, take down).
Category management (create, rename, reorder, show/hide, assign attribute set) and the audit log
(filterable, paged, read-only) are now built.

**Creating a shop directly** was added 2026-09-13 (`POST /v1/admin/vendors`, "New shop" on the
Shops screen). It is for a seller signed up over the phone or in person: it finds or creates the
owner's account, creates the shop and approves it in one transaction, and grants the seller role
through the same Identity call the review queue uses. **It collects no KYC documents** — the
administrator is standing in for the verification — so it is audited under its own action,
`vendor.created_by_admin`, and `Vendor.ApproveOnCreation` refuses any shop that did reach the
queue. When the owner had no account, the response carries a generated password shown **once**;
there is no `IEmailSender`, so handing it over is manual. That is the part to revisit when mail
works — see G4.

**Not built:** paging beyond the first 100 rows on the vendor and moderation queues.

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
