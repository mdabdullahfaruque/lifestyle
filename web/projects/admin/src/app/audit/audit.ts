import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, AuditEntry } from 'data-access';
import { firstValueFrom } from 'rxjs';

/**
 * The append-only record of administrative actions — who approved which vendor, who took which
 * product down, and when.
 *
 * Read-only by design. An audit trail an administrator can edit answers no question worth asking,
 * so there is no delete and no edit here and none on the API either.
 */
@Component({
  selector: 'app-audit',
  imports: [FormsModule, DatePipe],
  templateUrl: './audit.html',
  styleUrl: './audit.scss',
})
export class Audit {
  private readonly admin = inject(AdminService);

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly expanded = signal<string | null>(null);

  protected filters = { action: '', entityType: '', from: '', to: '' };
  private page = 1;
  private readonly pageSize = 50;

  protected readonly hasMore = signal(false);

  constructor() {
    void this.load(false);
  }

  protected async apply(): Promise<void> {
    this.page = 1;
    await this.load(false);
  }

  protected async clear(): Promise<void> {
    this.filters = { action: '', entityType: '', from: '', to: '' };
    this.page = 1;
    await this.load(false);
  }

  protected async more(): Promise<void> {
    this.page += 1;
    await this.load(true);
  }

  protected toggle(id: string): void {
    this.expanded.update((current) => (current === id ? null : id));
  }

  /** Pretty-prints the JSON payload when there is one; falls back to the raw string. */
  protected formatData(data: string | null): string {
    if (!data) return '';
    try {
      return JSON.stringify(JSON.parse(data), null, 2);
    } catch {
      return data;
    }
  }

  private async load(append: boolean): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);

    try {
      const page = await firstValueFrom(
        this.admin.auditEntries({
          action: this.filters.action.trim() || undefined,
          entityType: this.filters.entityType.trim() || undefined,
          // A date input gives a plain date; widen it to cover the whole day in both directions.
          from: this.filters.from ? `${this.filters.from}T00:00:00Z` : undefined,
          to: this.filters.to ? `${this.filters.to}T23:59:59Z` : undefined,
          page: this.page,
          pageSize: this.pageSize,
        }),
      );

      const items = page.items ?? [];
      this.entries.update((current) => (append ? [...current, ...items] : items));
      this.total.set(page.page?.totalCount ?? items.length);
      this.hasMore.set(this.entries().length < this.total());
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
