import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ProblemDetails, VendorService } from 'data-access';
import { describeSlugProblem, looksLikeEmail, slugify } from 'util';
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

  /**
   * Per-field messages, keyed by input name. One banner for every problem meant a seller was told
   * "a shop address may only contain lowercase letters…" with no indication of which box that was
   * about — and no way to satisfy it except by guessing the format.
   */
  protected readonly fieldErrors = signal<Record<string, string>>({});

  protected readonly busy = signal(false);
  protected readonly uploading = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly editable = computed(() => ['none', 'draft', 'rejected'].includes(this.shop.stage()));

  protected readonly attached = computed(() => {
    const docs = this.shop.vendor()?.documents ?? [];
    return new Set(docs.map((d) => d.kind));
  });

  /** What the shop's web address will be, shown live under the field. */
  protected addressPreview(): string {
    const slug = slugify(this.form.desiredSlug || this.form.displayName || '');
    return slug ? `${slug}.mylifestylemart.com` : '';
  }

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

  /**
   * A message about a field stops being true the moment someone edits that field. Leaving it up
   * until the next save makes a corrected form look broken — and it is how a stale "required"
   * ends up sitting under a box that is now filled in.
   */
  protected clearFieldError(event: Event): void {
    const name = (event.target as HTMLInputElement | null)?.name;
    if (!name || !this.fieldErrors()[name]) return;

    this.fieldErrors.update((errors) => {
      const { [name]: _removed, ...rest } = errors;
      return rest;
    });

    if (!Object.keys(this.fieldErrors()).length) this.error.set(null);
  }

  protected async saveApplication(): Promise<void> {
    if (this.busy()) return;

    this.error.set(null);
    this.notice.set(null);

    const problems = this.validate();
    this.fieldErrors.set(problems);

    if (Object.keys(problems).length) {
      const first = Object.keys(problems)[0];
      this.error.set(
        Object.keys(problems).length === 1
          ? problems[first]
          : `${Object.keys(problems).length} fields need attention — see the messages below.`,
      );
      document.querySelector<HTMLInputElement>(`[name="${first}"]`)?.focus();
      return;
    }

    this.busy.set(true);

    try {
      const vendor = await firstValueFrom(
        this.vendors.apply({
          legalName: this.form.legalName.trim(),
          displayName: this.form.displayName.trim(),
          // Slugified here, not sent raw: the API's validator rejects a desiredSlug that is not
          // already a slug, so sending what was typed is what produced "may only contain
          // lowercase letters, numbers and hyphens" for anyone who typed their shop's name.
          desiredSlug: slugify(this.form.desiredSlug) || null,
          contactEmail: this.form.contactEmail.trim(),
          contactPhone: this.form.contactPhone.trim(),
          registrationNumber: this.form.registrationNumber.trim() || null,
        }),
      );
      this.fieldErrors.set({});
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

  /** The API's rules, checked here so the answer is immediate and lands on the right field. */
  private validate(): Record<string, string> {
    const errors: Record<string, string> = {};
    const f = this.form;

    if (!f.legalName.trim()) errors['legalName'] = 'Enter the name on your trade licence.';
    else if (f.legalName.trim().length > 300) errors['legalName'] = 'That is too long (300 characters).';

    if (!f.displayName.trim()) errors['displayName'] = 'Give your shop a name — buyers see this one.';
    else if (f.displayName.trim().length > 200) errors['displayName'] = 'That is too long (200 characters).';

    if (f.desiredSlug.trim()) {
      const problem = describeSlugProblem(f.desiredSlug);
      if (problem) errors['desiredSlug'] = problem;
    }

    if (!f.contactEmail.trim()) errors['contactEmail'] = 'An email is required — this is how we reach you about the shop.';
    else if (!looksLikeEmail(f.contactEmail)) errors['contactEmail'] = 'That does not look like an email address.';

    if (!f.contactPhone.trim()) errors['contactPhone'] = 'A phone number is required — buyers order over WhatsApp.';
    else if (f.contactPhone.trim().length > 32) errors['contactPhone'] = 'That is too long (32 characters).';

    if (f.registrationNumber.trim().length > 60) errors['registrationNumber'] = 'That is too long (60 characters).';

    return errors;
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

    // Anything the API rejects by field goes back onto that field rather than into the banner.
    if (problem?.errors) {
      const byField: Record<string, string> = {};
      for (const [key, messages] of Object.entries(problem.errors)) {
        byField[key.charAt(0).toLowerCase() + key.slice(1)] = messages.join(' ');
      }
      this.fieldErrors.set(byField);
      return 'Some details were rejected — see the messages below.';
    }

    switch (problem?.code) {
      case 'vendors.registration_document_missing':
        return 'Upload both documents before submitting.';
      case 'vendors.slug_taken':
      case 'vendors.slug_reserved':
      case 'vendors.slug_unavailable':
      case 'vendors.slug_invalid':
        this.fieldErrors.set({ desiredSlug: problem.detail ?? 'That web address is taken. Try another.' });
        return 'That web address cannot be used — see below.';
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
