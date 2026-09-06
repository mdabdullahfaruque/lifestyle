import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService, Category, ProductListItem } from 'data-access';
import { firstValueFrom } from 'rxjs';

import { useMedia } from '../media';
import { formatMoney } from '../price';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly catalog = inject(CatalogService);
  protected readonly media = useMedia();

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly products = signal<ProductListItem[]>([]);
  protected readonly categories = signal<Category[]>([]);
  protected readonly activeCategory = signal<string | null>(null);

  protected readonly skeletons = [0, 1, 2, 3, 4, 5, 6, 7];

  /** Leaf categories only — parents carry no attribute set and are not selectable filters. */
  protected readonly filters = computed(() => {
    const leaves: Category[] = [];
    const walk = (c: Category) => {
      if (c.isLeaf) leaves.push(c);
      c.children?.forEach(walk);
    };
    this.categories().forEach(walk);
    return leaves;
  });

  constructor() {
    void this.load();
  }

  protected label(p: ProductListItem): string {
    return p.price.min === p.price.max
      ? formatMoney(p.price.min, p.price.currency)
      : `${formatMoney(p.price.min, p.price.currency)}+`;
  }

  /** Availability bands, never counts — seller-maintained stock drifts, a band does not lie. */
  protected stockBand(p: ProductListItem): { text: string; low: boolean } | null {
    if (p.totalStock <= 0) return { text: 'Out of stock', low: true };
    if (p.totalStock <= 5) return { text: 'Only a few left', low: true };
    return null;
  }

  protected async selectCategory(id: string | null): Promise<void> {
    if (this.activeCategory() === id) return;
    this.activeCategory.set(id);
    await this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      const [page, cats] = await Promise.all([
        firstValueFrom(this.catalog.browse({ pageSize: 40, categoryId: this.activeCategory() ?? undefined })),
        this.categories().length
          ? Promise.resolve(this.categories())
          : firstValueFrom(this.catalog.categories()),
      ]);
      this.products.set(page.items ?? []);
      this.categories.set(cats as Category[]);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
