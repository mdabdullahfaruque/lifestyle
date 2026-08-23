import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthStore } from './auth.store';

/**
 * Route guards. These are a usability layer, not a security boundary — the server re-checks every
 * permission on the endpoint (docs/04 §3.4). Their job is to avoid routing someone to a screen that
 * would only produce a 403.
 */
async function ensureAuthenticated(returnUrl: string): Promise<true | UrlTree> {
  const auth = inject(AuthStore);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;

  // A hard reload starts with no access token in memory; try the refresh cookie first.
  if (await auth.restore()) return true;

  return router.createUrlTree(['/login'], { queryParams: { returnUrl } });
}

export const authGuard: CanActivateFn = (_route, state) => ensureAuthenticated(state.url);

export const permissionGuard =
  (permission: string): CanActivateFn =>
  async (_route, state) => {
    const auth = inject(AuthStore);
    const router = inject(Router);

    const authenticated = await ensureAuthenticated(state.url);
    if (authenticated !== true) return authenticated;

    return auth.can(permission) ? true : router.createUrlTree(['/forbidden']);
  };
