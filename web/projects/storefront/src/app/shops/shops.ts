import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CatalogService, Storefront } from 'data-access';
import { I18nStore } from 'i18n';
import { firstValueFrom } from 'rxjs';

import { useMedia } from '../media';

@Component({
  selector: 'app-shops',
  imports: [RouterLink],
  templateUrl: './shops.html',
  styleUrl: './shops.scss',
})
export class Shops {
  private readonly catalog = inject(CatalogService);
  protected readonly media = useMedia();
  protected readonly i18n = inject(I18nStore);

  protected readonly shops = signal<Storefront[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly skeletons = [0, 1, 2, 3, 4, 5];

  constructor() {
    void this.load();
  }

  /** First letters of the shop name — a stand-in until vendors upload a logo. */
  protected initials(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0])
      .join('')
      .toUpperCase();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const page = await firstValueFrom(this.catalog.storefronts({ pageSize: 60 }));
      this.shops.set(page.items ?? []);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
