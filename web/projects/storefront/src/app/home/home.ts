import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CatalogService, Category, ProductListItem } from 'data-access';
import { firstValueFrom } from 'rxjs';

import { useMedia } from '../media';
import { formatMoney } from '../price';

type Sort = '' | 'price_asc' | 'price_desc' | 'name';

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

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly products = signal<ProductListItem[]>([]);
  protected readonly categories = signal<Category[]>([]);
  protected readonly total = signal(0);

  protected readonly activeCategory = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly sort = signal<Sort>('');

  protected readonly skeletons = [0, 1, 2, 3, 4, 5, 6, 7];

  protected readonly sorts: { value: Sort; label: string }[] = [
    { value: '', label: 'Newest' },
    { value: 'price_asc', label: 'Price: low to high' },
    { value: 'price_desc', label: 'Price: high to low' },
    { value: 'name', label: 'Name' },
  ];

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
    () => this.filters().find((c) => c.id === this.activeCategory())?.name ?? null,
  );

  /** Anything the grid is narrowed by, so the heading can say so rather than lying "Fresh this week". */
  protected readonly isFiltered = computed(() => !!this.search().trim() || !!this.activeCategory());

  constructor() {
    // The query string is the source of truth, so a filtered grid survives a reload and can be
    // shared as a link — which is exactly what someone does when they want to show a colleague.
    this.route.queryParamMap.subscribe((params) => {
      this.search.set(params.get('q') ?? '');
      this.activeCategory.set(params.get('category'));
      this.sort.set((params.get('sort') as Sort) ?? '');
      void this.load();
    });
  }

  protected label(p: ProductListItem): string {
    return p.price.min === p.price.max
      ? formatMoney(p.price.min, p.price.currency)
      : `${formatMoney(p.price.min, p.price.currency)}+`;
  }

  /** The struck-through "was", shown only when the API says there is a real saving. */
  protected wasLabel(p: ProductListItem): string | null {
    return p.compareAtPrice ? formatMoney(p.compareAtPrice, p.price.currency) : null;
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
    if (p.totalStock <= 0) return { text: 'Out of stock', low: true };
    if (p.totalStock <= 5) return { text: 'Only a few left', low: true };
    return null;
  }

  protected selectCategory(id: string | null): void {
    void this.navigate({ category: id });
  }

  protected applySearch(term: string): void {
    void this.navigate({ q: term.trim() || null, category: null });
  }

  protected changeSort(value: string): void {
    void this.navigate({ sort: value || null });
  }

  protected clearFilters(): void {
    void this.router.navigate([], { queryParams: {} });
  }

  private navigate(patch: Record<string, string | null>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams: patch,
      queryParamsHandling: 'merge',
    });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      const [page, cats] = await Promise.all([
        firstValueFrom(
          this.catalog.browse({
            pageSize: 48,
            categoryId: this.activeCategory() ?? undefined,
            search: this.search().trim() || undefined,
            sort: (this.sort() || undefined) as never,
          }),
        ),
        this.categories().length
          ? Promise.resolve(this.categories())
          : firstValueFrom(this.catalog.categories()),
      ]);
      this.products.set(page.items ?? []);
      this.total.set(page.page?.totalCount ?? page.items?.length ?? 0);
      this.categories.set(cats as Category[]);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
