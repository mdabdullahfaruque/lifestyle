import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService, ProductListItem, Storefront } from 'data-access';
import { I18nStore } from 'i18n';
import { firstValueFrom } from 'rxjs';

import { useMedia } from '../media';
import { formatMoney } from '../price';

@Component({
  selector: 'app-shop',
  imports: [RouterLink],
  templateUrl: './shop.html',
  styleUrl: './shop.scss',
})
export class ShopPage implements OnInit {
  private readonly catalog = inject(CatalogService);
  protected readonly media = useMedia();
  protected readonly i18n = inject(I18nStore);

  readonly slug = input.required<string>();

  protected readonly shop = signal<Storefront | null>(null);
  protected readonly products = signal<ProductListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly skeletons = [0, 1, 2, 3];

  /** The vendor's own colour, applied to this page only — the design's shop-within-marketplace rule. */
  protected readonly accent = computed(() => this.shop()?.accentColour || null);

  /**
   * Loads on init, not in the constructor: a required input is not set until after construction,
   * so reading `slug()` there throws NG0950 — which the load's own catch turned into a silent
   * "Shop not found" on a shop that exists.
   */
  ngOnInit(): void {
    void this.load();
  }

  protected initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]).join('').toUpperCase();
  }

  protected label(p: ProductListItem): string {
    const bcp = this.i18n.bcp47();
    return p.price.min === p.price.max
      ? formatMoney(p.price.min, p.price.currency, bcp)
      : `${formatMoney(p.price.min, p.price.currency, bcp)}+`;
  }

  protected wasLabel(p: ProductListItem): string | null {
    return p.compareAtPrice ? formatMoney(p.compareAtPrice, p.price.currency, this.i18n.bcp47()) : null;
  }

  /** "1 product" is a different string from "{count} products" in both languages. */
  protected productCountLabel(): string {
    const n = this.products().length;
    return n === 1
      ? this.i18n.t('shop.productCountOne')
      : this.i18n.t('shop.productCount', { count: this.i18n.formatNumber(n) });
  }

  protected saving(p: ProductListItem): number | null {
    if (!p.compareAtPrice) return null;
    const was = Number(p.compareAtPrice);
    const now = Number(p.price.min);
    if (!Number.isFinite(was) || was <= now) return null;
    return Math.floor(((was - now) / was) * 100);
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [shop, page] = await Promise.all([
        firstValueFrom(this.catalog.storefront(this.slug())),
        firstValueFrom(this.catalog.shopProducts(this.slug(), { pageSize: 60 })),
      ]);
      this.shop.set(shop);
      this.products.set(page.items ?? []);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
