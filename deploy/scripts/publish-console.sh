#!/usr/bin/env bash
#
# Publish a pre-built console bundle from this host's nginx. Runs ON the server.
#
#   # from the workstation:
#   cd web && npx ng build seller
#   tar -C web/dist/seller/browser -czf seller.tgz .
#   scp seller.tgz deploy@<host>:/tmp/
#   ssh deploy@<host> 'cd ~/lifestyle-bd-prod && ./deploy/scripts/publish-console.sh seller /tmp/seller.tgz'
#
# The bundle is built on the workstation, not here, deliberately: this VPS runs five other
# production stacks in 12 GB, and an Angular build peaks well past what is comfortable to spend on
# a static site. There is no node on the host and there should not need to be.
#
# These consoles belong on Cloudflare Pages — docs/07 §9 says why they are not, and what has to be
# true before they move back.

set -Eeuo pipefail

APP="${1:?usage: publish-console.sh <seller|admin> <bundle.tgz>}"
BUNDLE="${2:?usage: publish-console.sh <seller|admin> <bundle.tgz>}"

[[ "${APP}" == "seller" || "${APP}" == "admin" ]] || { echo "Unknown console '${APP}'." >&2; exit 1; }
[[ -f "${BUNDLE}" ]] || { echo "No such bundle: ${BUNDLE}" >&2; exit 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HOST="${APP}.mylifestylemart.com"
TARGET="/var/www/lifestyle-${APP}"

log()  { printf '\n\033[1;34m▸ %s\033[0m\n' "$*"; }
fail() { printf '\n\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

# Unpack beside the live directory and swap, so a half-extracted bundle is never served.
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/lifestyle-${APP}.XXXXXX")"
trap 'rm -rf "${STAGE}"' EXIT

log "Unpacking ${BUNDLE}"
tar -xzf "${BUNDLE}" -C "${STAGE}"
[[ -f "${STAGE}/index.html" ]] || fail "That bundle has no index.html at its root — tar from inside dist/<app>/browser."

log "Publishing to ${TARGET}"
sudo mkdir -p "${TARGET}"
sudo rsync -a --delete "${STAGE}/" "${TARGET}/"
sudo chown -R www-data:www-data "${TARGET}"

SITE="/etc/nginx/sites-available/${HOST}"
if [[ ! -f "${SITE}" ]]; then
    log "Installing the nginx site for ${HOST}"
    sudo cp "${ROOT}/deploy/nginx/${HOST}.conf" "${SITE}"
    sudo ln -sf "${SITE}" "/etc/nginx/sites-enabled/${HOST}"
fi

# Always test before reloading: this host serves four other products' production traffic, and a
# failed test leaves the running config untouched.
log "Testing nginx"
sudo nginx -t
sudo systemctl reload nginx

code="$(curl -sS -o /dev/null -w '%{http_code}' "https://${HOST}/" || true)"
printf '\n  %-34s %s\n' "https://${HOST}/" "${code}"
[[ "${code}" == "200" ]] || fail "${HOST} did not return 200."

log "${APP} console published"
