import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, ProblemDetails, Vendor, VendorListItem } from 'data-access';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-vendors',
  imports: [FormsModule, DatePipe],
  templateUrl: './vendors.html',
  styleUrl: './vendors.scss',
})
export class Vendors {
  private readonly admin = inject(AdminService);

  protected readonly items = signal<VendorListItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly working = signal<string | null>(null);

  /** Pending first: this screen exists to clear the queue. */
  protected readonly status = signal<string | null>('PendingReview');

  protected readonly filters = [
    { value: 'PendingReview', label: 'Awaiting review' },
    { value: 'Approved', label: 'Approved' },
    { value: 'Rejected', label: 'Rejected' },
    { value: 'Suspended', label: 'Suspended' },
    { value: null, label: 'All' },
  ];

  /** The vendor whose detail panel is open, with its documents. */
  protected readonly opened = signal<Vendor | null>(null);
  protected readonly openingId = signal<string | null>(null);
  protected rejectionReason = '';

  constructor() {
    void this.load();
  }

  protected async setStatus(value: string | null): Promise<void> {
    this.status.set(value);
    this.opened.set(null);
    await this.load();
  }

  protected async open(vendor: VendorListItem): Promise<void> {
    if (this.opened()?.id === vendor.id) {
      this.opened.set(null);
      return;
    }

    this.openingId.set(vendor.id);
    this.rejectionReason = '';
    try {
      this.opened.set(await firstValueFrom(this.admin.vendor(vendor.id)));
    } catch {
      this.error.set('Could not load that application.');
    } finally {
      this.openingId.set(null);
    }
  }

  /**
   * Suspending pulls the shop and everything it sells off the storefront (Catalog listens for
   * VendorSuspendedEvent). Always needs a reason — the vendor is shown it, and "your shop
   * disappeared" with no explanation is how a marketplace loses sellers.
   */
  protected async setSuspension(vendorId: string, suspend: boolean): Promise<void> {
    if (suspend && !this.rejectionReason.trim()) {
      this.error.set('Give a reason for suspending — the vendor is shown it.');
      return;
    }

    this.working.set(vendorId);
    this.error.set(null);
    this.notice.set(null);

    try {
      const updated = await firstValueFrom(
        this.admin.setSuspension(vendorId, suspend, suspend ? this.rejectionReason.trim() : null),
      );
      this.notice.set(
        suspend
          ? `${updated.displayName} suspended. Their products are no longer visible to buyers.`
          : `${updated.displayName} reinstated.`,
      );
      this.opened.set(null);
      await this.load();
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'That change could not be recorded.');
    } finally {
      this.working.set(null);
    }
  }

  protected async review(vendorId: string, approve: boolean): Promise<void> {
    // A rejection without a reason is useless to the vendor, and the API refuses it anyway.
    if (!approve && !this.rejectionReason.trim()) {
      this.error.set('Give a reason for rejecting — the applicant is shown it.');
      return;
    }

    this.working.set(vendorId);
    this.error.set(null);
    this.notice.set(null);

    try {
      const updated = await firstValueFrom(
        this.admin.reviewVendor(vendorId, approve, approve ? null : this.rejectionReason.trim()),
      );
      this.notice.set(`${updated.displayName} ${approve ? 'approved' : 'rejected'}.`);
      this.opened.set(null);
      await this.load();
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'That review could not be recorded.');
    } finally {
      this.working.set(null);
    }
  }

  /** KYC documents are never on a public URL — this fetches through the authorised endpoint. */
  protected documentUrl(mediaId: string): string {
    return `/v1/media/private/${mediaId}`;
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const page = await firstValueFrom(
        this.admin.vendors({ status: this.status() ?? undefined, pageSize: 100 }),
      );
      this.items.set(page.items ?? []);
    } catch {
      this.error.set('Could not load the vendor queue.');
    } finally {
      this.loading.set(false);
    }
  }
}
