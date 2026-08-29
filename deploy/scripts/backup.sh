#!/usr/bin/env bash
#
# Database backup, written straight off the host.
#
#   ./deploy/scripts/backup.sh            # scheduled run
#   ./deploy/scripts/backup.sh pre-deploy # labelled, taken before a migration
#
# The point of this script is the OFF-HOST copy. A dump sitting on the same NVMe as the database
# it came from protects against exactly one failure mode (someone dropping a table) and none of
# the ones that actually lose a company (disk failure, host loss, ransomware). Doc 03 §10.1 lists
# this as the single most important gap to close before the platform carries money.

set -Eeuo pipefail

LABEL="${1:-scheduled}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="${ROOT}/deploy/.env"

# shellcheck disable=SC1090
set -a; source "${ENV_FILE}"; set +a

COMPOSE=(docker compose -f "${ROOT}/deploy/docker-compose.prod.yml" --env-file "${ENV_FILE}")
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
NAME="${DEPLOYMENT}-${STAMP}-${LABEL}.dump"
LOCAL_DIR="/var/backups/lifestyle/${DEPLOYMENT}"

mkdir -p "${LOCAL_DIR}"

# Custom format (-Fc): compressed, and restorable selectively with pg_restore.
"${COMPOSE[@]}" exec -T postgres \
    pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -Fc --no-owner --no-acl \
    > "${LOCAL_DIR}/${NAME}"

SIZE=$(stat -c%s "${LOCAL_DIR}/${NAME}")
[[ "${SIZE}" -gt 4096 ]] || { echo "Dump is implausibly small (${SIZE} bytes) — treating as failed." >&2; exit 1; }

echo "Local dump: ${LOCAL_DIR}/${NAME} ($(numfmt --to=iec "${SIZE}"))"

# ── Off-host copy — the entire point of this script ─────────────────────────
if [[ -n "${BACKUP_RSYNC_TARGET:-}" ]]; then
    # Any second machine with SSH. Use a dedicated key restricted to this path, and prefer an
    # append-only arrangement on the receiving side so a compromised host cannot erase history.
    rsync -a --chmod=F600 -e "ssh -o BatchMode=yes" \
        "${LOCAL_DIR}/${NAME}" "${BACKUP_RSYNC_TARGET}/"
    echo "Off-host copy: ${BACKUP_RSYNC_TARGET}/${NAME}"
elif [[ -n "${BACKUP_S3_BUCKET:-}" ]]; then
    AWS_ACCESS_KEY_ID="${BACKUP_ACCESS_KEY}" \
    AWS_SECRET_ACCESS_KEY="${BACKUP_SECRET_KEY}" \
    aws s3 cp "${LOCAL_DIR}/${NAME}" "s3://${BACKUP_S3_BUCKET}/${DEPLOYMENT}/${NAME}" \
        --endpoint-url "${BACKUP_S3_ENDPOINT}" --only-show-errors
    echo "Off-host copy: s3://${BACKUP_S3_BUCKET}/${DEPLOYMENT}/${NAME}"
else
    echo "WARNING: no off-host target configured (BACKUP_RSYNC_TARGET / BACKUP_S3_BUCKET)." >&2
    echo "         This dump exists only on this host — losing the host loses the data." >&2
fi

# Local retention only — the bucket's own lifecycle policy governs the off-host copies, so a
# compromised host cannot delete history.
find "${LOCAL_DIR}" -name '*.dump' -mtime "+${BACKUP_RETENTION_DAYS:-30}" -delete
