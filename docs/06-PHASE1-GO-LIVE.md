# Lifestyle — Phase 1 Go-Live Runbook

| Field | Value |
|---|---|
| Document | 06 — Phase 1 Go-Live Runbook |
| Version | 1.0 |
| Date | 2026-08-30 |
| Scope | API + seller/admin SPAs + media + "coming soon" pages. **No storefront** (Phase 2, doc 05 §9) |
| Companion | [05 — Deployment](05-DEPLOYMENT.md) explains *why*; this document is only *do this, see that* |
| Time | ~2–3 hours first time, most of it waiting on builds and DNS |

**How to read this.** Steps are strictly in order — each part depends on the one before. Every
step says what you should see; if you see something else, stop and check the
[troubleshooting table](#part-h--troubleshooting) before continuing. Placeholders you must
replace, everywhere they appear:

| Placeholder | Meaning | Example |
|---|---|---|
| `example.com` | your root domain (bought at Cloudflare Registrar) | — |
| `<SERVER_IP>` | your server's public IPv4 | `203.0.113.10` |
| `<you>` | your SSH user on the server | `root` or a sudo user |

This runbook builds the API image **on the server** — no GitHub Actions, no registry account
needed to go live. The tag-triggered pipeline is a follow-up (Part G), not a prerequisite.

---

## Part 0 — Preconditions

Confirm all of these before starting. Each is required.

- [ ] A server (Ubuntu/Debian), SSH access, ports **80** and **443** reachable from the internet.
- [ ] The domain's zone is active in your Cloudflare account (automatic with Cloudflare Registrar).
- [ ] This repository pushed to a git host the server can clone from.
- [ ] The repo state passes locally: `dotnet build` clean, `dotnet test` green (145+ tests).
- [ ] An authenticator app (Google Authenticator, Aegis, 1Password…) on your phone — the admin
      account **cannot sign in without TOTP**, by design.
- [ ] Decided: a second machine reachable over SSH for backups (Part F). You can go live without
      it, but do not onboard a real vendor until it exists.

---

## Part A — Cloudflare (dashboard)

### A1. DNS records

**Dashboard → your zone → DNS → Records.** Create (or correct) exactly these:

| # | Type | Name | Content | Proxy status |
|---|---|---|---|---|
| 1 | A | `api` | `<SERVER_IP>` | 🟠 **Proxied** |
| 2 | A | `@` | `<SERVER_IP>` | 🟠 **Proxied** |
| 3 | A | `www` | `<SERVER_IP>` | 🟠 **Proxied** |
| 4 | A | `media` | `<SERVER_IP>` | 🟠 **Proxied** |

Do **not** create `seller`, `admin` (Pages creates those itself in Part D) or a `*` wildcard
(storefronts are Phase 2 — nothing would answer on it yet).

> Proxied (orange) is Cloudflare's CDN/WAF and is free (doc 05 §2.1). If you prefer to stay
> DNS-only for now, set all four to grey **and** skip step A2/A3, deleting the four
> `import origin_tls` lines from `deploy/Caddyfile` instead — Caddy then gets public certificates
> itself. Do not mix: a grey record pointing at a Caddy that serves the Origin CA certificate
> shows visitors an untrusted-certificate error.

### A2. SSL/TLS mode

**SSL/TLS → Overview → Configure**: set the encryption mode to **Full (strict)**.

⚠ Not *Flexible* and not *Full*. Flexible sends logins and cookies to your server as plain HTTP
across the internet while the browser shows a padlock.

### A3. Origin certificate

**SSL/TLS → Origin Server → Create Certificate**:

- Key type: RSA (default) · Hostnames: `example.com` and `*.example.com` · Validity: 15 years.
- Two text boxes appear. Keep this page open — you paste both onto the server in step B3.
  The **private key is shown only once**.

**Expected state after Part A:** four orange-cloud records; mode Full (strict); certificate + key
text available.

---

## Part B — Server: install, configure, first deployment

SSH in: `ssh <you>@<SERVER_IP>`. All commands run on the server unless marked otherwise.

### B1. Docker and tooling

```bash
curl -fsSL https://get.docker.com | sh
sudo apt-get update && sudo apt-get install -y rsync git
sudo usermod -aG docker "$USER"
exit          # then SSH back in, so the docker group applies
```

Verify: `docker compose version` prints `Docker Compose version v2.x`.

### B2. Clone and configure

```bash
sudo mkdir -p /srv/lifestyle /var/backups/lifestyle
sudo chown "$USER" /srv/lifestyle /var/backups/lifestyle
git clone <your-repo-url> /srv/lifestyle
cd /srv/lifestyle

cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
```

Generate the two secrets you are about to need — run each and copy the output:

```bash
openssl rand -base64 32    # → POSTGRES_PASSWORD
openssl rand -base64 48    # → JWT_SIGNING_KEY
```

Edit `deploy/.env` (`nano deploy/.env`). Set **every** row of this table; leave everything else
at its default:

| Variable | Set to |
|---|---|
| `DEPLOYMENT` | `my-prod` |
| `REGISTRY` | `local` *(images are built on this server — Part B4)* |
| `TAG` | `p1` |
| `PLATFORM_COUNTRY` / `PLATFORM_CURRENCY` / `PLATFORM_TIMEZONE` | `MY` / `MYR` / `Asia/Kuala_Lumpur` |
| `ROOT_DOMAIN` | `example.com` |
| `ACME_EMAIL` | an email you read |
| `POSTGRES_PASSWORD` | first generated value |
| `JWT_SIGNING_KEY` | second generated value |
| `STORAGE_PROVIDER` | `local` |
| `MEDIA_PUBLIC_BASE_URL` | `https://media.example.com` |
| `SEED_SUPERADMIN_EMAIL` | your admin email |
| `SEED_SUPERADMIN_PASSWORD` | a strong password, **10+ chars** — you will type it in Part E |
| `SEED_SUPERADMIN_NAME` | your name |
| `BACKUP_RSYNC_TARGET` | leave blank for now; filled in Part F |

### B3. Origin certificate onto the server

```bash
mkdir -p deploy/origin && chmod 700 deploy/origin
nano deploy/origin/cert.pem   # paste the CERTIFICATE box from step A3, save
nano deploy/origin/key.pem    # paste the PRIVATE KEY box, save
chmod 600 deploy/origin/*.pem
```

Verify the two files match (outputs must be identical):

```bash
openssl x509 -noout -modulus -in deploy/origin/cert.pem | openssl md5
openssl rsa  -noout -modulus -in deploy/origin/key.pem  | openssl md5
```

### B4. Build the API image

```bash
docker build -f deploy/api.Dockerfile -t local/lifestyle-api:p1 .
```

First build takes several minutes. **Expected last line:** `naming to docker.io/local/lifestyle-api:p1`.

### B5. Database up, schema in, data seeded

```bash
cd /srv/lifestyle
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d postgres redis
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env ps
```

Wait until `postgres` and `redis` both show **healthy** (re-run `ps`; ~15 s), then:

```bash
# ⚠ Pass ONLY the words `migrate` / `seed` — the image's entrypoint is already the app.
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env run --rm --no-deps api migrate
```

**Expected in output:** `Applying 1 migration(s): ..._InitialSchema` then `Migrations applied.`

```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env run --rm --no-deps api seed
```

**Expected in output:** six `Seeded role …` lines, `Seeded super-admin <your email>`, and
`Seeded 4 attribute sets and 6 categories.`

### B6. Full stack up

```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env up -d
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env ps
```

**Expected:** `api` **healthy**, `caddy` running, `postgres`/`redis` healthy. Then, still on the
server:

```bash
curl -s http://localhost:8080/v1/internal/health 2>/dev/null; \
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env exec api curl -s http://localhost:8080/v1/internal/health
```

**Expected:** the second command prints `Healthy` (the first fails — the API port is
deliberately not published on the host).

### B7. Remove the seed password

The admin account now exists; the password must not sit in a file.

```bash
nano deploy/.env    # blank out SEED_SUPERADMIN_PASSWORD (keep the email if you like)
```

---

## Part C — Verify from the outside

From **your own machine** (not the server):

| # | Command | Expected |
|---|---|---|
| C1 | `curl -s https://api.example.com/v1/internal/health` | `Healthy` |
| C2 | `curl -s -o /dev/null -w '%{http_code}\n' https://api.example.com/v1/internal/tls-check?domain=x.invalid` | `404` — anything else means the certificate gate is open; stop and fix |
| C3 | `curl -s https://www.example.com/` | the "coming soon" HTML |
| C4 | `curl -s https://api.example.com/v1/catalog/categories` | JSON: `Fashion` and `Electronics` trees with 4 leaf categories |
| C5 | `curl -s -o /dev/null -w '%{http_code}\n' https://api.example.com/v1/vendor/products` | `401` |
| C6 | `curl -sI https://api.example.com/v1/internal/health \| grep -i cf-ray` | a `cf-ray:` header — proves traffic goes through Cloudflare |

If C1 hangs: DNS not propagated yet (`dig api.example.com` should return Cloudflare IPs, not
`<SERVER_IP>`), or port 443 blocked at the provider's firewall.

---

## Part D — Seller and Admin SPAs on Cloudflare Pages

### D1. Set the real API URL in the repo (local machine)

Edit **both** files, replacing `example.com` with your domain:

- `web/projects/seller/src/environments/environment.production.ts`
- `web/projects/admin/src/environments/environment.production.ts`

```ts
export const environment = { production: true, apiBaseUrl: 'https://api.example.com' };
```

Commit and push to `master`. (The `_redirects` files for deep-link refreshes are already in the
repo.)

### D2. Create the two Pages projects

**Dashboard → Workers & Pages → Create → Pages → Connect to Git** → pick this repository. You know
this flow from your existing frontends. Exact settings per project:

| Setting | `lifestyle-seller` | `lifestyle-admin` |
|---|---|---|
| Production branch | `master` | `master` |
| Root directory | `web` | `web` |
| Build command | `npx ng build seller --configuration production` | `npx ng build admin --configuration production` |
| Build output directory | `dist/seller/browser` | `dist/admin/browser` |
| Environment variable | `NODE_VERSION` = `24` | `NODE_VERSION` = `24` |

Wait for the first build to go green in the Pages dashboard.

### D3. Custom domains

In each Pages project → **Custom domains → Set up a custom domain**:

- `lifestyle-seller` → `seller.example.com`
- `lifestyle-admin` → `admin.example.com`

Cloudflare adds the DNS records itself (the domain is on its Registrar). Wait for **Active**.

### D4. Verify

| Check | Expected |
|---|---|
| Open `https://seller.example.com` | the Angular shell loads (a mostly empty page is correct — screens are the next work item) |
| Open `https://seller.example.com/anything/deep` and hard-refresh | still the app, not a 404 (`_redirects` working) |
| Browser dev-tools → console | **no CORS errors** once the app calls the API. ⚠ Test on the custom domains only — `*.pages.dev` preview URLs are not in the API's CORS allow-list and cookies are cross-site there, both by design |

---

## Part E — Bootstrap the administrator (TOTP)

There are no admin UI screens yet, so enrolment is three `curl` calls. Use the email/password
from B2. From your own machine:

**E1 — sign in on the buyer surface** (the admin surface refuses until TOTP exists — expected):

```bash
curl -s https://api.example.com/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"<ADMIN_EMAIL>","password":"<ADMIN_PASSWORD>","surface":"buyer"}'
```

Copy the `accessToken` value from the response.

**E2 — begin enrolment:**

```bash
curl -s -X POST https://api.example.com/v1/auth/2fa/begin \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

The response contains `secret` and a `provisioningUri`. In your authenticator app choose
**Enter key manually**, account name = your admin email, key = the `secret` value (or render the
URI as a QR with any offline tool).

**E3 — confirm with the current 6-digit code:**

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://api.example.com/v1/auth/2fa/confirm \
  -H "Authorization: Bearer <ACCESS_TOKEN>" -H 'Content-Type: application/json' \
  -d '{"secret":"<SECRET_FROM_E2>","code":"<6_DIGITS>"}'
```

**Expected:** `204`.

**E4 — prove the gate:**

```bash
# without a code → must be refused:
curl -s https://api.example.com/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<ADMIN_EMAIL>","password":"<ADMIN_PASSWORD>","surface":"admin"}'
# expected: 400 with code identity.totp_required
# (before E3 it was 403 identity.totp_enrolment_required — different code, same gate)

# with the current code → must succeed:
curl -s https://api.example.com/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"<ADMIN_EMAIL>","password":"<ADMIN_PASSWORD>","surface":"admin","totpCode":"<6_DIGITS>"}'
# expected: 200 with an accessToken and "surface":"admin"
```

The platform is now administrable. (Optional end-to-end proof: run the vendor journey —
register → apply → upload docs → submit → approve with the admin token → create a product →
moderate it. It mirrors `PhaseOneExitCriterionTests`, which already walks this path on every test
run.)

---

## Part F — Backups (before any real vendor data)

On the **backup machine** (any second box with SSH):

```bash
sudo useradd -m lifestyle-backup
sudo mkdir -p /home/lifestyle-backup/lifestyle
sudo chown lifestyle-backup: /home/lifestyle-backup/lifestyle
```

On the **server**:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/lifestyle-backup -N ""
ssh-copy-id -i ~/.ssh/lifestyle-backup lifestyle-backup@<BACKUP_HOST>
# and add to ~/.ssh/config:
#   Host <BACKUP_HOST>
#     IdentityFile ~/.ssh/lifestyle-backup

nano deploy/.env    # BACKUP_RSYNC_TARGET=lifestyle-backup@<BACKUP_HOST>:/home/lifestyle-backup/lifestyle
```

Run once by hand and check both outcomes:

```bash
./deploy/scripts/backup.sh manual-test     # expected: "Local dump: … " AND "Off-host copy: …"
./deploy/scripts/restore-test.sh           # expected: "PASS — backup is restorable."
```

Then schedule (`crontab -e`):

```cron
0 2 * * * /srv/lifestyle/deploy/scripts/backup.sh        >> /var/log/lifestyle-backup.log  2>&1
0 4 * * 0 /srv/lifestyle/deploy/scripts/restore-test.sh  >> /var/log/lifestyle-restore.log 2>&1
```

Minimal monitoring, same sitting: point a free uptime monitor (e.g. UptimeRobot) at
`https://api.example.com/v1/internal/health`, and add a disk alert:

```cron
0 * * * * df -P / | awk 'NR==2 {gsub("%","",$5); if ($5+0 > 85) print "DISK " $5 "% on lifestyle server"}' | ifne mail -s "lifestyle disk alert" <you@example.com>
```

*(requires `sudo apt-get install -y moreutils mailutils` and working outbound mail — or replace
with any webhook you already use).*

---

## Part G — After go-live

**Releases from now on.** Manual path (matches Part B): on the server —

```bash
cd /srv/lifestyle && git pull
docker build -f deploy/api.Dockerfile -t local/lifestyle-api:p2 .   # bump the tag each release
./deploy/scripts/deploy.sh my-prod p2
```

`deploy.sh` backs up, migrates, and rolls the API health-gated, printing the rollback command if
anything fails. Rollback is `./deploy/scripts/deploy.sh my-prod <previous-tag>` — safe because
migrations are expand-only (doc 05 §8).

**Optional — tag-triggered pipeline** (`.github/workflows/deploy.yml`): repo on GitHub, add
secrets `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY` and variable `ROOT_DOMAIN`, log the server
into GHCR once (`docker login ghcr.io` with a `read:packages` token) and set
`REGISTRY=ghcr.io/<owner>` in `deploy/.env`. Then `git tag v0.2.0 && git push --tags` does the
whole Part-G manual path for you. SPAs need nothing: Pages already redeploys them on every push.

**Optional hardening once everything is proxied:** restrict 80/443 to Cloudflare's published IP
ranges (doc 05 §3) — after C1–C6 pass, never before.

---

## Part H — Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Browser: Cloudflare error **526** | Origin certificate missing/wrong while mode is Full (strict) | Re-check B3 (the two md5 outputs must match), `docker compose … restart caddy` |
| Browser: Cloudflare error **522/timeout** | Caddy not running, or 443 blocked at the provider firewall | `docker compose … ps`, `docker compose … logs caddy`; open 80+443 |
| `curl https://api…/health` → HTML error page | DNS still propagating or record grey while Caddyfile expects proxied | `dig api.example.com`; align A1 with the Caddyfile mode |
| Migrate/seed starts the web server instead | You passed `dotnet Lifestyle.Api.dll migrate` | Pass **only** `migrate` — the entrypoint is already the app |
| Seed logs `No Seed:SuperAdmin… configured` | `SEED_SUPERADMIN_*` blank in `deploy/.env` at seed time | Fill them, re-run the seed (idempotent), then blank the password |
| API container restarts in a loop | A required env var failed `ValidateOnStart` (e.g. `JWT_SIGNING_KEY` < 32 chars) | `docker compose … logs api` names the exact option |
| SPA login: CORS error in console | Testing on `*.pages.dev`, or `ROOT_DOMAIN` mismatch | Use the custom domains; check `Platform__CorsOrigins` in `docker compose … config` |
| SPA login OK but logged out on reload | Refresh cookie not coming back — cross-**site** context | Same cause as above: SPA and API must both be under `example.com` |
| Admin login refused (403 `totp_enrolment_required` / 400 `totp_required` / 401 `totp_invalid`) | The TOTP gate, in its three states | Part E in full; a time-drifted phone produces `totp_invalid` — enable network time on the phone |
| Image upload 200 but image URL 404 | `media` record missing, or `MEDIA_PUBLIC_BASE_URL` mismatch | A1 row 4; the URL host must equal `media.example.com` in `.env` |
| Real client IPs missing from logs (all one internal IP) | Forwarded headers not reaching the app | Confirm you deployed current `deploy/Caddyfile` (it sets `X-Forwarded-For {client_ip}`) and current API image |

---

## Final go-live sign-off

Every box, no exceptions:

- [ ] C1–C6 all pass
- [ ] D4 all pass on the **custom domains**
- [ ] E4 both results correct (400 `totp_required` without a code, 200 with)
- [ ] `SEED_SUPERADMIN_PASSWORD` is blank in `deploy/.env` (B7)
- [ ] `nmap -p 5432,6379 <SERVER_IP>` from outside: both **closed**
- [ ] `backup.sh` and `restore-test.sh` both PASS and both cron entries installed (Part F)
- [ ] Uptime monitor is watching the health endpoint
- [ ] `deploy/.env` is mode `600` and appears in no commit: `git log --all -- deploy/.env` is empty
