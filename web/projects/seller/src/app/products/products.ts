import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemDetails, ProductListItem, VendorService } from 'data-access';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-products',
  imports: [RouterLink],
  templateUrl: './products.html',
  styleUrl: './products.scss',
})
export class Products {
  private readonly vendors = inject(VendorService);

  protected readonly items = signal<ProductListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly working = signal<string | null>(null);
  protected readonly filter = signal<string | null>(null);

  protected readonly filters = [
    { value: null, label: 'All' },
    { value: 'Draft', label: 'Draft' },
    { value: 'PendingReview', label: 'In review' },
    { value: 'Published', label: 'Live' },
    { value: 'Rejected', label: 'Rejected' },
  ];

  constructor() {
    void this.load();
  }

  protected async setFilter(value: string | null): Promise<void> {
    this.filter.set(value);
    await this.load();
  }

  /** Draft → PendingReview. A product is invisible to buyers until a moderator approves it. */
  protected async submitForReview(product: ProductListItem): Promise<void> {
    this.working.set(product.id);
    this.error.set(null);
    this.notice.set(null);

    try {
      await firstValueFrom(this.vendors.submitProduct(product.id));
      this.notice.set(`"${product.name}" sent for review.`);
      await this.load();
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'Could not submit that product.');
    } finally {
      this.working.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const page = await firstValueFrom(
        this.vendors.products({ status: this.filter() ?? undefined, pageSize: 100 }),
      );
      this.items.set(page.items ?? []);
    } catch {
      this.error.set('Could not load your products.');
    } finally {
      this.loading.set(false);
    }
  }
}
