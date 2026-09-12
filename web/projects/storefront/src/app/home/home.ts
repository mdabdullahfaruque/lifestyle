import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CatalogService, Category, ProductListItem } from 'data-access';
import { I18nStore } from 'i18n';
import { firstValueFrom } from 'rxjs';

import { useMedia } from '../media';
import { formatMoney } from '../price';

type Sort = '' | 'price_asc' | 'price_desc' | 'name';

const PAGE_SIZE = 24;

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly catalog = inject(CatalogService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly media = useMedia();
  protected readonly i18n = inject(I18nStore);

  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly failed = signal(false);
  protected readonly products = signal<ProductListItem[]>([]);
  protected readonly categories = signal<Category[]>([]);
  protected readonly total = signal(0);
  private readonly page = signal(1);

  protected readonly activeCategory = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly sort = signal<Sort>('');

  protected readonly skeletons = [0, 1, 2, 3, 4, 5, 6, 7];

  protected readonly sorts: { value: Sort; key: string }[] = [
    { value: '', key: 'home.sortNewest' },
    { value: 'price_asc', key: 'home.sortPriceAsc' },
    { value: 'price_desc', key: 'home.sortPriceDesc' },
    { value: 'name', key: 'home.sortName' },
  ];

  protected readonly hasMore = computed(() => this.products().length < this.total());

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

  protected readonly activeCategoryName = computed(
    () => {
      const hit = this.filters().find((c) => c.id === this.activeCategory());
      return hit ? this.categoryName(hit) : null;
    },
  );

  protected readonly isFiltered = computed(() => !!this.search().trim() || !!this.activeCategory());

  constructor() {
    // The query string is the source of truth, so a filtered grid survives a reload and can be
    // shared as a link — which is exactly what someone does when they want to show a colleague.
    this.route.queryParamMap.subscribe((params) => {
      this.search.set(params.get('q') ?? '');
      this.activeCategory.set(params.get('category'));
      this.sort.set((params.get('sort') as Sort) ?? '');
      this.page.set(1);
      void this.load(false);
    });
  }

  /**
   * Category labels are platform-owned, so they are translated; the fallback is the API's own name
   * so a category added later shows correctly in English rather than as a raw key.
   */
  protected categoryName(c: Category): string {
    return this.i18n.tOr(`category.${c.slug}`, c.name);
  }

  protected countLabel(): string {
    const n = this.total();
    return n === 1
      ? this.i18n.t('home.itemCountOne')
      : this.i18n.t('home.itemCount', { count: this.i18n.formatNumber(n) });
  }

  protected label(p: ProductListItem): string {
    const bcp = this.i18n.bcp47();
    return p.price.min === p.price.max
      ? formatMoney(p.price.min, p.price.currency, bcp)
      : `${formatMoney(p.price.min, p.price.currency, bcp)}+`;
  }

  /** The struck-through "was", shown only when the API says there is a real saving. */
  protected wasLabel(p: ProductListItem): string | null {
    return p.compareAtPrice ? formatMoney(p.compareAtPrice, p.price.currency, this.i18n.bcp47()) : null;
  }

  /** Rounded down, so a badge can never overstate the discount. */
  protected saving(p: ProductListItem): number | null {
    if (!p.compareAtPrice) return null;
    const was = Number(p.compareAtPrice);
    const now = Number(p.price.min);
    if (!Number.isFinite(was) || was <= now) return null;
    return Math.floor(((was - now) / was) * 100);
  }

  /** Availability bands, never counts — seller-maintained stock drifts, a band does not lie. */
  protected stockBand(p: ProductListItem): { text: string; low: boolean } | null {
    if (p.totalStock <= 0) return { text: this.i18n.t('stock.outOfStock'), low: true };
    if (p.totalStock <= 5) return { text: this.i18n.t('stock.onlyAFewLeft'), low: true };
    return null;
  }

  protected selectCategory(id: string | null): void {
    void this.navigate({ category: id });
  }

  protected changeSort(value: string): void {
    void this.navigate({ sort: value || null });
  }

  protected clearFilters(): void {
    void this.router.navigate([], { queryParams: {} });
  }

  protected async loadMore(): Promise<void> {
    if (this.loadingMore() || !this.hasMore()) return;
    this.page.update((p) => p + 1);
    await this.load(true);
  }

  private navigate(patch: Record<string, string | null>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams: patch,
      queryParamsHandling: 'merge',
    });
  }

  private async load(append: boolean): Promise<void> {
    if (append) this.loadingMore.set(true);
    else this.loading.set(true);
    this.failed.set(false);

    try {
      const [page, cats] = await Promise.all([
        firstValueFrom(
          this.catalog.browse({
            page: this.page(),
            pageSize: PAGE_SIZE,
            categoryId: this.activeCategory() ?? undefined,
            search: this.search().trim() || undefined,
            sort: (this.sort() || undefined) as never,
          }),
        ),
        this.categories().length
          ? Promise.resolve(this.categories())
          : firstValueFrom(this.catalog.categories()),
      ]);

      const items = page.items ?? [];
      this.products.update((current) => (append ? [...current, ...items] : items));
      this.total.set(page.page?.totalCount ?? items.length);
      this.categories.set(cats as Category[]);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
      this.loadingMore.set(false);
    }
  }
}
