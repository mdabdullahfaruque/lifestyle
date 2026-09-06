import { KeyValuePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, ProblemDetails, Product, ProductListItem } from 'data-access';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-moderation',
  imports: [FormsModule, KeyValuePipe],
  templateUrl: './moderation.html',
  styleUrl: './moderation.scss',
})
export class Moderation {
  private readonly admin = inject(AdminService);

  protected readonly items = signal<ProductListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly working = signal<string | null>(null);

  protected readonly status = signal<string | null>('PendingReview');

  protected readonly filters = [
    { value: 'PendingReview', label: 'Awaiting review' },
    { value: 'Published', label: 'Live' },
    { value: 'Rejected', label: 'Rejected' },
    { value: null, label: 'All' },
  ];

  protected readonly opened = signal<Product | null>(null);
  protected readonly openingId = signal<string | null>(null);
  protected note = '';

  constructor() {
    void this.load();
  }

  protected async setStatus(value: string | null): Promise<void> {
    this.status.set(value);
    this.opened.set(null);
    await this.load();
  }

  protected imageUrl(mediaId: string, variant: 'thumb' | 'card' = 'card'): string {
    return `https://media.mylifestylemart.com/${mediaId.slice(0, 2)}/${mediaId}/${variant}.png`;
  }

  protected async open(product: ProductListItem): Promise<void> {
    if (this.opened()?.id === product.id) {
      this.opened.set(null);
      return;
    }

    this.openingId.set(product.id);
    this.note = '';
    try {
      this.opened.set(await firstValueFrom(this.admin.productForModeration(product.id)));
    } catch {
      this.error.set('Could not load that product.');
    } finally {
      this.openingId.set(null);
    }
  }

  protected async moderate(productId: string, approve: boolean): Promise<void> {
    if (!approve && !this.note.trim()) {
      this.error.set('Give a note when rejecting — the vendor is shown it.');
      return;
    }

    this.working.set(productId);
    this.error.set(null);
    this.notice.set(null);

    try {
      const updated = await firstValueFrom(
        this.admin.moderateProduct(productId, approve, approve ? null : this.note.trim()),
      );
      this.notice.set(`"${updated.name}" is now ${updated.status}.`);
      this.opened.set(null);
      await this.load();
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'That decision could not be recorded.');
    } finally {
      this.working.set(null);
    }
  }

  /** Pulls a live product back off the storefront. Always needs a reason on the record. */
  protected async takedown(productId: string): Promise<void> {
    if (!this.note.trim()) {
      this.error.set('A takedown needs a reason.');
      return;
    }

    this.working.set(productId);
    this.error.set(null);

    try {
      const updated = await firstValueFrom(this.admin.takedownProduct(productId, this.note.trim()));
      this.notice.set(`"${updated.name}" taken down.`);
      this.opened.set(null);
      await this.load();
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'Could not take that product down.');
    } finally {
      this.working.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const page = await firstValueFrom(
        this.admin.productsForModeration({ status: this.status() ?? undefined, pageSize: 100 }),
      );
      this.items.set(page.items ?? []);
    } catch {
      this.error.set('Could not load the moderation queue.');
    } finally {
      this.loading.set(false);
    }
  }
}
