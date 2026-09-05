#!/usr/bin/env bash
#
# Deploy one country stack.
#
#   ./deploy/scripts/deploy.sh my-prod v2026.08.24-1
#
# Order matters: migrate first, on the OLD code, then roll the containers. That only works
# because migrations are expand-only (docs/04 §4.4) — the old code must keep running against the
# new schema for the length of the rollout.

set -Eeuo pipefail

DEPLOYMENT="${1:?usage: deploy.sh <deployment> <tag>}"
TAG="${2:?usage: deploy.sh <deployment> <tag>}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${ROOT}/deploy/.env"
COMPOSE=(docker compose -f "${ROOT}/deploy/docker-compose.prod.yml" --env-file "${ENV_FILE}")

log() { printf '\n\033[1;34m▸ %s\033[0m\n' "$*"; }
fail() { printf '\n\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

[[ -f "${ENV_FILE}" ]] || fail "Missing ${ENV_FILE} — copy deploy/.env.example and fill it in."

export TAG DEPLOYMENT

log "Pulling ${TAG}"
# PHASE 2: add `storefront` here when the storefront container joins the stack (docs/05 §9).
# REGISTRY=local means the image was built on this host (docs/06 Part B4) — nothing to pull,
# and `docker compose pull` on a local-only image would abort the whole deploy under -e.
source "${ENV_FILE}" 2>/dev/null || true
if [[ "${REGISTRY:-local}" == "local" ]]; then
    log "REGISTRY=local — skipping pull (image built on this host)"
else
    "${COMPOSE[@]}" pull api
fi

log "Backing up before touching the schema"
"${ROOT}/deploy/scripts/backup.sh" pre-deploy

log "Applying migrations"
# A one-off container on the same network. It runs and exits; the running API is untouched.
# NOTE: the image's ENTRYPOINT is already `dotnet Lifestyle.Api.dll`, so only the argument is
# passed here. Writing `... run api dotnet Lifestyle.Api.dll migrate` would double the entrypoint
# and silently START THE SERVER instead of migrating.
"${COMPOSE[@]}" run --rm --no-deps api migrate \
    || fail "Migration failed. The previous release is still serving traffic; nothing was rolled."

log "Rolling API"
"${COMPOSE[@]}" up -d --no-deps --wait api \
    || fail "API did not become healthy. Roll back with: ./deploy/scripts/deploy.sh ${DEPLOYMENT} <previous-tag>"

# PHASE 2: roll the storefront here, after the API (docs/05 §9).

log "Reloading edge"
# up -d alone does not re-read a bind-mounted Caddyfile; an explicit reload does.
"${COMPOSE[@]}" up -d --no-deps caddy
"${COMPOSE[@]}" exec -T caddy caddy reload --config /etc/caddy/Caddyfile 2>/dev/null \
    || log "caddy reload skipped (container was just created — config already fresh)"

log "Verifying"
"${COMPOSE[@]}" exec -T api curl -fsS http://localhost:8080/v1/internal/health >/dev/null \
    || fail "Health check failed after rollout."

log "Pruning old images"
docker image prune -f --filter "until=168h" >/dev/null

log "Deployed ${DEPLOYMENT} @ ${TAG}"
"${COMPOSE[@]}" ps
