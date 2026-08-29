#!/usr/bin/env bash
#
# Restore the latest backup into a throwaway database and check it is actually usable.
#
#   ./deploy/scripts/restore-test.sh
#
# Run this weekly, from cron, and alert if it fails. A backup that has never been restored is not
# a backup — it is an untested assumption, and the moment you discover it was wrong is always the
# worst possible moment (FRD §27).

set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${ROOT}/deploy/.env"

# shellcheck disable=SC1090
set -a; source "${ENV_FILE}"; set +a

COMPOSE=(docker compose -f "${ROOT}/deploy/docker-compose.prod.yml" --env-file "${ENV_FILE}")
LATEST="$(find "/var/backups/lifestyle/${DEPLOYMENT}" -name '*.dump' -printf '%T@ %p\n' \
          | sort -rn | head -1 | cut -d' ' -f2-)"

[[ -n "${LATEST}" ]] || { echo "No backup found to test." >&2; exit 1; }
echo "Testing: ${LATEST}"

VERIFY_DB="restore_check_$(date -u +%s)"
cleanup() {
    "${COMPOSE[@]}" exec -T postgres psql -U "${POSTGRES_USER}" -d postgres \
        -c "DROP DATABASE IF EXISTS ${VERIFY_DB};" >/dev/null 2>&1 || true
}
trap cleanup EXIT

"${COMPOSE[@]}" exec -T postgres psql -U "${POSTGRES_USER}" -d postgres \
    -c "CREATE DATABASE ${VERIFY_DB};" >/dev/null

"${COMPOSE[@]}" exec -T postgres pg_restore -U "${POSTGRES_USER}" -d "${VERIFY_DB}" --no-owner \
    < "${LATEST}" >/dev/null

# Restoring without error is necessary but not sufficient — an empty dump restores cleanly too.
# Assert the schema is really there and carries rows.
TABLES=$("${COMPOSE[@]}" exec -T postgres psql -U "${POSTGRES_USER}" -d "${VERIFY_DB}" -tAc \
    "SELECT count(*) FROM pg_tables WHERE schemaname IN ('identity','vendors','catalog','media','platform');")

ROLES=$("${COMPOSE[@]}" exec -T postgres psql -U "${POSTGRES_USER}" -d "${VERIFY_DB}" -tAc \
    "SELECT count(*) FROM identity.roles;")

echo "Restored ${TABLES} tables, ${ROLES} roles."

[[ "${TABLES}" -ge 19 ]] || { echo "FAIL: expected at least 19 tables, found ${TABLES}." >&2; exit 1; }
[[ "${ROLES}"  -ge 6  ]] || { echo "FAIL: expected the 6 seeded roles, found ${ROLES}." >&2; exit 1; }

echo "PASS — backup is restorable."
