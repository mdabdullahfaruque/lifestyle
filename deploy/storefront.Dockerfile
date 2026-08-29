# Angular SSR storefront — a Node server, not a static bundle.
#
# PHASE 2: this image is built and tested but not yet deployed — the storefront is deferred
# (docs/05 §9). Nothing here needs changing when that phase starts; the switch-on steps live in
# the guide.
#
# Build from the repository root:
#   docker build -f deploy/storefront.Dockerfile -t lifestyle-storefront:$TAG .
#
# The seller and admin apps do NOT use this image — they are SPAs on Cloudflare Pages (docs/05 §4).

# ─────────────────────────── build ───────────────────────────
FROM node:24-alpine AS build
WORKDIR /src

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npx ng build storefront --configuration production

# ─────────────────────────── runtime ───────────────────────────
FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=4000

RUN apk add --no-cache curl && addgroup -S ssr && adduser -S ssr -G ssr

# The SSR bundle is self-contained: Angular's build emits every dependency it needs into
# dist/, so no node_modules are installed in the runtime image.
COPY --from=build --chown=ssr:ssr /src/dist/storefront ./dist/storefront

USER ssr
EXPOSE 4000

HEALTHCHECK --interval=15s --timeout=3s --start-period=15s --retries=4 \
    CMD curl -fsS http://localhost:4000/ -o /dev/null || exit 1

CMD ["node", "dist/storefront/server/server.mjs"]
