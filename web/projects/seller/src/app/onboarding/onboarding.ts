import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ProblemDetails, VendorService } from 'data-access';
import { firstValueFrom } from 'rxjs';

import { ShopStore } from '../shop.store';

/** The two documents the API requires before an application can be submitted. */
const REQUIRED_DOCS = [
  { kind: 'BusinessRegistration', label: 'Business registration', hint: 'Trade licence or incorporation certificate.' },
  { kind: 'OwnerIdentity', label: 'Owner identity', hint: 'NID or passport of the shop owner.' },
] as const;

@Component({
  selector: 'app-onboarding',
  imports: [FormsModule],
  templateUrl: './onboarding.html',
  styleUrl: './onboarding.scss',
})
export class Onboarding {
  private readonly vendors = inject(VendorService);
  protected readonly shop = inject(ShopStore);

  protected readonly requiredDocs = REQUIRED_DOCS;

  protected form = {
    legalName: '',
    displayName: '',
    desiredSlug: '',
    contactEmail: '',
    contactPhone: '',
    registrationNumber: '',
  };

  protected readonly busy = signal(false);
  protected readonly uploading = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly editable = computed(() => ['none', 'draft', 'rejected'].includes(this.shop.stage()));

  protected readonly attached = computed(() => {
    const docs = this.shop.vendor()?.documents ?? [];
    return new Set(docs.map((d) => d.kind));
  });

  protected readonly canSubmit = computed(
    () => this.shop.vendor() !== null && REQUIRED_DOCS.every((d) => this.attached().has(d.kind)),
  );

  constructor() {
    void this.hydrate();
  }

  private async hydrate(): Promise<void> {
    if (!this.shop.isLoaded()) await this.shop.refresh();
    const v = this.shop.vendor();
    if (!v) return;

    this.form = {
      legalName: v.legalName ?? '',
      displayName: v.displayName ?? '',
      desiredSlug: v.slug ?? '',
      contactEmail: v.contactEmail ?? '',
      contactPhone: v.contactPhone ?? '',
      registrationNumber: v.registrationNumber ?? '',
    };
  }

  protected async saveApplication(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    try {
      const vendor = await firstValueFrom(
        this.vendors.apply({
          legalName: this.form.legalName.trim(),
          displayName: this.form.displayName.trim(),
          desiredSlug: this.form.desiredSlug.trim() || null,
          contactEmail: this.form.contactEmail.trim(),
          contactPhone: this.form.contactPhone.trim(),
          registrationNumber: this.form.registrationNumber.trim() || null,
        }),
      );
      this.shop.set(vendor);
      this.notice.set('Application saved. Now upload both documents, then submit it for review.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * KYC documents upload with `private: true`. That is not a detail: private files are excluded
   * from the public media host and readable only through the authorised endpoint. An identity
   * document on a public, CDN-cached URL is a breach.
   */
  protected async uploadDocument(kind: string, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploading.set(kind);
    this.error.set(null);

    try {
      const media = await firstValueFrom(this.vendors.upload(file, true));
      const vendor = await firstValueFrom(this.vendors.attachDocument(kind, media.id, file.name));
      this.shop.set(vendor);
      this.notice.set(`${kind === 'OwnerIdentity' ? 'Identity document' : 'Business registration'} uploaded.`);
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.uploading.set(null);
      input.value = '';
    }
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      const vendor = await firstValueFrom(this.vendors.submitApplication());
      this.shop.set(vendor);
      this.notice.set('Submitted. A reviewer will look at your application shortly.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(false);
    }
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    switch (problem?.code) {
      case 'vendors.registration_document_missing':
        return 'Upload both documents before submitting.';
      case 'vendors.slug_taken':
        return 'That shop address is already taken. Try another.';
      case 'media.content_type_not_allowed':
        return 'That file type is not accepted. Use a JPG, PNG, WebP or PDF.';
      case 'media.too_large':
        return 'That file is over the 10 MB limit.';
      default:
        if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
        return problem?.detail ?? 'Something went wrong. Please try again.';
    }
  }
}
