import { Injectable, computed, inject, signal } from '@angular/core';
import { CatalogService, Storefront } from 'data-access';
import { firstValueFrom } from 'rxjs';

/**
 * Which chrome this request renders in (FRD §20.1).
 *
 * Marketplace and the standalone shop sites are **one application, two shells** — they share the
 * product page, so maintaining them as two apps would guarantee drift. But they are genuinely
 * different chrome, not one shell with different colours: platform branding and global search
 * versus vendor branding and shop-scoped nav.
 */
export type ShellKind = 'marketplace' | 'shop';

@Injectable({ providedIn: 'root' })
export class ShellStore {
  private readonly catalog = inject(CatalogService);

  private readonly storefront = signal<Storefront | null>(null);
  private readonly resolved = signal(false);

  readonly shop = this.storefront.asReadonly();
  readonly kind = computed<ShellKind>(() => (this.storefront() ? 'shop' : 'marketplace'));
  readonly isResolved = this.resolved.asReadonly();

  /** The vendor's accent colour, applied as a CSS custom property on the shop shell. */
  readonly accent = computed(() => this.storefront()?.accentColour ?? null);

  /**
   * Asks the API which shop this host belongs to. The client sends no slug — the server derives it
   * from the `Host` header, which is what stops a storefront being pointed at another shop's data.
   *
   * A 404 means the marketplace host (or an unknown host), which is a normal answer, not an error.
   */
  async resolve(): Promise<void> {
    if (this.resolved()) return;

    try {
      this.storefront.set(await firstValueFrom(this.catalog.currentStorefront()));
    } catch {
      this.storefront.set(null);
    } finally {
      this.resolved.set(true);
    }
  }
}
