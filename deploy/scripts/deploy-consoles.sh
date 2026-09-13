#!/usr/bin/env bash
#
# Build the seller and admin consoles and publish them from this host's nginx.
#
#   ./deploy/scripts/deploy-consoles.sh             # both
#   ./deploy/scripts/deploy-consoles.sh seller      # one
#
# These belong on Cloudflare Pages (docs/06 step D1). They are here because the Pages token
# available at go-live was refused by both wrangler and the direct-upload API, and waiting for a
# working token was blocking the seller sign-up flow entirely. docs/07 §8 has the revert trigger:
# when Pages works again, point the DNS records back and delete /var/www/lifestyle-{seller,admin}.
#
# Requires: node + npm, nginx, and certbot certificates already issued for the two hostnames.
# Idempotent — publishing over an existing deploy is the normal case.

set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APPS=("${@:-seller admin}")
read -r -a APPS <<< "${APPS[*]}"

log()  { printf '\n\033[1;34m▸ %s\033[0m\n' "$*"; }
fail() { printf '\n\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

command -v npm >/dev/null || fail "npm is not installed on this host."

for app in "${APPS[@]}"; do
    [[ "${app}" == "seller" || "${app}" == "admin" ]] || fail "Unknown console '${app}'."
done

log "Installing web dependencies"
npm --prefix "${ROOT}/web" ci --no-audit --no-fund

for app in "${APPS[@]}"; do
    target="/var/www/lifestyle-${app}"
    host="${app}.mylifestylemart.com"

    log "Building ${app}"
    npm --prefix "${ROOT}/web" run -- ng build "${app}" --configuration production

    src="${ROOT}/web/dist/${app}/browser"
    [[ -d "${src}" ]] || fail "Build produced no ${src}."
    [[ -f "${src}/index.html" ]] || fail "${src} has no index.html — check the build configuration."

    log "Publishing to ${target}"
    sudo mkdir -p "${target}"
    # --delete so a removed bundle does not linger and get served by a stale index; the write is
    # to a directory nginx serves live, and rsync replaces each file atomically.
    sudo rsync -a --delete "${src}/" "${target}/"
    sudo chown -R www-data:www-data "${target}"

    site="/etc/nginx/sites-available/${host}"
    if [[ ! -f "${site}" ]]; then
        log "Installing the nginx site for ${host}"
        sudo cp "${ROOT}/deploy/nginx/${host}.conf" "${site}"
        sudo ln -sf "${site}" "/etc/nginx/sites-enabled/${host}"
    fi
done

# Always test before reloading: this host serves three other products' production traffic, and a
# failed test leaves the running config untouched.
log "Testing nginx"
sudo nginx -t
sudo systemctl reload nginx

for app in "${APPS[@]}"; do
    host="${app}.mylifestylemart.com"
    code="$(curl -sS -o /dev/null -w '%{http_code}' "https://${host}/" || true)"
    printf '  %-34s %s\n' "https://${host}/" "${code}"
    [[ "${code}" == "200" ]] || fail "${host} did not return 200."
done

log "Consoles published"
