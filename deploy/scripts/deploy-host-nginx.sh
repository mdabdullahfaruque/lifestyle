#!/usr/bin/env bash
#
# Deploy one country stack on a host whose reverse proxy is NOT ours (see
# deploy/docker-compose.host-nginx.yml).
#
#   ./deploy/scripts/deploy-host-nginx.sh bd-prod p1
#
# Identical to deploy.sh — backup, migrate on the old code, then roll the API health-gated —
# except that it never touches Caddy. deploy.sh's `up -d --no-deps caddy` step names the service
# explicitly, and Compose enables a profiled service when you name it, so running deploy.sh on
# this host would start the Caddy container and fight host nginx for ports 80/443.

set -Eeuo pipefail

DEPLOYMENT="${1:?usage: deploy-host-nginx.sh <deployment> <tag>}"
TAG="${2:?usage: deploy-host-nginx.sh <deployment> <tag>}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${ROOT}/deploy/.env"
COMPOSE=(docker compose
    -f "${ROOT}/deploy/docker-compose.prod.yml"
    -f "${ROOT}/deploy/docker-compose.host-nginx.yml"
    --env-file "${ENV_FILE}")

log() { printf '\n\033[1;34m▸ %s\033[0m\n' "$*"; }
fail() { printf '\n\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

[[ -f "${ENV_FILE}" ]] || fail "Missing ${ENV_FILE} — copy deploy/.env.example and fill it in."

export TAG DEPLOYMENT

source "${ENV_FILE}" 2>/dev/null || true
if [[ "${REGISTRY:-local}" == "local" ]]; then
    log "REGISTRY=local — skipping pull (image built on this host)"
else
    log "Pulling ${TAG}"
    "${COMPOSE[@]}" pull api
fi

log "Backing up before touching the schema"
"${ROOT}/deploy/scripts/backup.sh" pre-deploy

log "Applying migrations"
# Only the argument — the image's ENTRYPOINT is already `dotnet Lifestyle.Api.dll`.
"${COMPOSE[@]}" run --rm --no-deps api migrate \
    || fail "Migration failed. The previous release is still serving traffic; nothing was rolled."

log "Rolling API"
"${COMPOSE[@]}" up -d --no-deps --wait api \
    || fail "API did not become healthy. Roll back with: $0 ${DEPLOYMENT} <previous-tag>"

log "Verifying"
"${COMPOSE[@]}" exec -T api curl -fsS http://localhost:8080/v1/internal/health >/dev/null \
    || fail "Health check failed after rollout."

log "Pruning old images"
docker image prune -f --filter "until=168h" >/dev/null

log "Deployed ${DEPLOYMENT} @ ${TAG}"
"${COMPOSE[@]}" ps
