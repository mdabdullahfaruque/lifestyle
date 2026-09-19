import { Route } from '@angular/router';

import { routes } from './app.routes';

function shellChildren(): Route[] {
  const shell = routes.find((r) => r.path === '' && r.children);
  return shell?.children ?? [];
}

describe('admin routes', () => {
  /**
   * Sign-in is the only screen an unauthenticated caller may reach. Everything else is behind the
   * shell's guard — a console route that escapes it would be reachable without a session, and the
   * admin surface is the one where that matters most.
   */
  it('exposes only sign-in without a session', () => {
    const anonymous = routes.filter((r) => !r.canActivate && r.path !== '**').map((r) => r.path);

    expect(anonymous).toEqual(['login']);
  });

  it('guards the shell that holds every console screen', () => {
    const shell = routes.find((r) => r.path === '' && r.children);

    expect(shell).toBeTruthy();
    expect(shell?.canActivate?.length).toBeGreaterThan(0);
  });

  it('routes to the moderation surfaces', () => {
    const paths = shellChildren().map((r) => r.path);

    expect(paths).toContain('vendors');
  });
});
