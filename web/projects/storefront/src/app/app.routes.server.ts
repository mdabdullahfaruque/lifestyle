import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * PHASE 2 (docs/05 §9, docs/07 §3): these routes should be **server-rendered**. Organic search is
 * the storefront's acquisition channel, and WhatsApp link previews do not execute JavaScript — a
 * client-rendered product page loses the preview card on every shared link, which is the v1 order
 * path. That is the whole reason the storefront is SSR rather than a Pages SPA.
 *
 * Until the SSR container is deployed, the build is served as static files by nginx and every
 * route renders in the browser. Prerendering is not a substitute:
 *
 *   - the catalogue is live data, and there is no build-time list of products to prerender from;
 *   - `apiBaseUrl` is '' in production, which is correct for a same-origin browser request but
 *     resolves to the prerender server itself at build time, so the fetch times out.
 *
 * Switching these to `RenderMode.Server` is the whole change when the SSR container goes up.
 */
export const serverRoutes: ServerRoute[] = [
  {
    path: '**',
    renderMode: RenderMode.Client,
  },
];
