import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AdminCreatedVendor,
  AdminService,
  CreateVendorRequest,
  ProblemDetails,
  Vendor,
  VendorListItem,
} from 'data-access';
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

  /**
   * The "create a shop" panel.
   *
   * A shop created here is approved on the spot — the administrator is standing in for the review
   * they would otherwise do, so there is no second step and no KYC upload. What comes back once,
   * and only when the owner had no account, is a password; `created` holds the whole response
   * because that password is not stored anywhere it could be fetched from again.
   */
  protected readonly creating = signal(false);
  protected readonly created = signal<AdminCreatedVendor | null>(null);
  protected readonly saving = signal(false);
  protected readonly copied = signal(false);

  protected form: CreateVendorRequest = Vendors.emptyForm();

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

  protected toggleCreate(): void {
    const open = !this.creating();
    this.creating.set(open);
    this.error.set(null);

    if (open) {
      this.form = Vendors.emptyForm();
      this.created.set(null);
      this.opened.set(null);
    }
  }

  /**
   * Defaults the shop's contact details to the owner's when they were left blank. A shop that has
   * to be chased for a phone number is worse than one that starts with the owner's.
   */
  protected async create(): Promise<void> {
    if (this.saving()) return;

    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.copied.set(false);

    const form = this.form;
    const request: CreateVendorRequest = {
      legalName: form.legalName.trim() || form.displayName.trim(),
      displayName: form.displayName.trim(),
      desiredSlug: form.desiredSlug?.trim() || null,
      contactEmail: (form.contactEmail || form.ownerEmail).trim(),
      contactPhone: form.contactPhone.trim(),
      registrationNumber: form.registrationNumber?.trim() || null,
      ownerEmail: form.ownerEmail.trim(),
      ownerFullName: form.ownerFullName?.trim() || null,
      ownerPhone: form.ownerPhone?.trim() || null,
      approve: true,
    };

    try {
      const result = await firstValueFrom(this.admin.createVendor(request));
      this.created.set(result);
      this.creating.set(false);
      this.notice.set(`${result.vendor.displayName} is live at /${result.vendor.slug}.`);

      // Show the new shop rather than leaving the filter on a queue it is not in.
      this.status.set('Approved');
      await this.load();
    } catch (err) {
      this.handle(err, 'The shop could not be created.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async copyPassword(): Promise<void> {
    const password = this.created()?.temporaryPassword;
    if (!password) return;

    try {
      await navigator.clipboard.writeText(password);
      this.copied.set(true);
    } catch {
      // Clipboard access can be refused outright; the password is on screen to be typed either way.
      this.copied.set(false);
    }
  }

  protected dismissCredentials(): void {
    this.created.set(null);
    this.copied.set(false);
  }

  private handle(err: unknown, fallback: string): void {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    if (problem?.errors) {
      this.error.set(Object.values(problem.errors).flat().join(' '));
      return;
    }

    this.error.set(problem?.detail ?? fallback);
  }

  private static emptyForm(): CreateVendorRequest {
    return {
      legalName: '',
      displayName: '',
      desiredSlug: '',
      contactEmail: '',
      contactPhone: '',
      registrationNumber: '',
      ownerEmail: '',
      ownerFullName: '',
      ownerPhone: '',
    };
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
