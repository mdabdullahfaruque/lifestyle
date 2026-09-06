import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductListItem, VendorService } from 'data-access';
import { firstValueFrom } from 'rxjs';

import { ShopStore } from '../shop.store';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly vendors = inject(VendorService);
  protected readonly shop = inject(ShopStore);

  protected readonly products = signal<ProductListItem[]>([]);
  protected readonly loading = signal(false);

  protected readonly counts = computed(() => {
    const all = this.products();
    return {
      total: all.length,
      published: all.filter((p) => p.status === 'Published').length,
      pending: all.filter((p) => p.status === 'PendingReview').length,
      draft: all.filter((p) => p.status === 'Draft').length,
      lowStock: all.filter((p) => p.status === 'Published' && p.totalStock > 0 && p.totalStock <= 5).length,
      outOfStock: all.filter((p) => p.status === 'Published' && p.totalStock <= 0).length,
    };
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    if (!this.shop.isLoaded()) await this.shop.refresh();
    if (!this.shop.canSell()) return;

    this.loading.set(true);
    try {
      const page = await firstValueFrom(this.vendors.products({ pageSize: 100 }));
      this.products.set(page.items ?? []);
    } catch {
      this.products.set([]);
    } finally {
      this.loading.set(false);
    }
  }
}
