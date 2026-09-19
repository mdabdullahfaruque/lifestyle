import { Route } from '@angular/router';

import { routes } from './app.routes';

/** The children of the authenticated shell, which is where every console screen lives. */
function shellChildren(): Route[] {
  const shell = routes.find((r) => r.path === '' && r.children);
  return shell?.children ?? [];
}

describe('seller routes', () => {
  it('puts every console screen behind the authenticated shell', () => {
    const children = shellChildren();

    expect(children.length).toBeGreaterThan(0);
    expect(children.some((r) => r.path === 'products')).toBeTrue();
    expect(children.some((r) => r.path === 'settings')).toBeTrue();
  });

  it('offers the image library and bulk import', () => {
    const paths = shellChildren().map((r) => r.path);

    expect(paths).toContain('media');
    expect(paths).toContain('products/import');
  });

  /**
   * Order matters and is easy to break by adding a route in the obvious place. `products/:productId`
   * matches the literal segment "import", so if it came first the bulk import screen would never
   * load — the router would try to open a product whose id is "import" and the seller would get an
   * error page instead of the importer.
   */
  it('matches products/import before the product-id route', () => {
    const paths = shellChildren().map((r) => r.path ?? '');

    const importAt = paths.indexOf('products/import');
    const productAt = paths.indexOf('products/:productId');

    expect(importAt).toBeGreaterThanOrEqual(0);
    expect(productAt).toBeGreaterThanOrEqual(0);
    expect(importAt).toBeLessThan(productAt);
  });

  it('keeps the password-reset entry points reachable without a session', () => {
    const anonymous = routes.filter((r) => !r.canActivate).map((r) => r.path);

    expect(anonymous).toContain('login');
    expect(anonymous).toContain('forgot-password');
    expect(anonymous).toContain('reset-password');
  });
});
