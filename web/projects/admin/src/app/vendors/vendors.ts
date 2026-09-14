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
import { describeSlugProblem, looksLikeEmail, slugify } from 'util';

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

  /**
   * Per-field messages, keyed by the input's name.
   *
   * The submit button is deliberately never disabled for being "invalid": a greyed-out button with
   * no explanation is a dead end — you cannot tell which field is wrong, or even that one is.
   * Pressing it always does something, and what it does when something is missing is say so.
   */
  protected readonly fieldErrors = signal<Record<string, string>>({});

  /**
   * What the shop's web address will actually be, shown live under the field.
   *
   * The box takes any text and the address is derived from it, so without this the admin is
   * guessing. Falls back to the shop name, which is what the API does when the box is empty.
   */
  protected addressPreview(): string {
    const slug = slugify(this.form.desiredSlug || this.form.displayName || '');
    return slug ? `${slug}.mylifestylemart.com` : '';
  }

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
      this.fieldErrors.set({});
      this.created.set(null);
      this.opened.set(null);
    }
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

  /**
   * Defaults the shop's contact details to the owner's when they were left blank. A shop that has
   * to be chased for a phone number is worse than one that starts with the owner's.
   */
  protected async create(): Promise<void> {
    if (this.saving()) return;

    this.error.set(null);
    this.notice.set(null);
    this.copied.set(false);

    const problems = Vendors.validate(this.form);
    this.fieldErrors.set(problems);

    if (Object.keys(problems).length) {
      const first = Object.keys(problems)[0];
      this.error.set(
        Object.keys(problems).length === 1
          ? problems[first]
          : `${Object.keys(problems).length} fields need attention — see the messages below.`,
      );
      // Put the cursor where the work is, rather than leaving it to be hunted for.
      document.querySelector<HTMLInputElement>(`form.create [name="${first}"]`)?.focus();
      return;
    }

    this.saving.set(true);
    const form = this.form;
    const request: CreateVendorRequest = {
      legalName: form.legalName.trim() || form.displayName.trim(),
      displayName: form.displayName.trim(),
      // Sent already slugified. The API would derive the same thing from the shop name, but its
      // validator rejects a `desiredSlug` that is not already a slug — so normalising here is what
      // lets an admin type "Uttara Crafts" in this box instead of guessing the format.
      desiredSlug: slugify(form.desiredSlug ?? '') || null,
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
      this.fieldErrors.set({});
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
      // FluentValidation keys these by property name — "DisplayName". Lower-casing the first
      // letter lands each message on the input it is about instead of in one lump at the top.
      const byField: Record<string, string> = {};
      for (const [key, messages] of Object.entries(problem.errors)) {
        byField[key.charAt(0).toLowerCase() + key.slice(1)] = messages.join(' ');
      }
      this.fieldErrors.set(byField);
      this.error.set('Some details were rejected — see the messages below.');
      return;
    }

    // A conflict is about a field too, even though it does not arrive as a validation error.
    switch (problem?.code) {
      case 'identity.email_taken':
      case 'vendors.already_owner':
        this.fieldErrors.set({ ownerEmail: problem.detail ?? 'That owner cannot be used.' });
        break;
      case 'identity.phone_taken':
        this.fieldErrors.set({ ownerPhone: problem.detail ?? 'That phone number is already in use.' });
        break;
      case 'vendors.slug_reserved':
      case 'vendors.slug_invalid':
      case 'vendors.slug_unavailable':
        this.fieldErrors.set({ desiredSlug: problem.detail ?? 'Choose a different shop address.' });
        break;
      default:
        this.fieldErrors.set({});
    }

    this.error.set(problem?.detail ?? fallback);
  }

  /**
   * The same rules the API enforces, checked here so the answer arrives immediately rather than
   * after a round trip. The API is still the authority — this only saves a request.
   */
  private static validate(form: CreateVendorRequest): Record<string, string> {
    const errors: Record<string, string> = {};

    if (!form.displayName.trim()) errors['displayName'] = 'Give the shop a name — buyers see this one.';
    else if (form.displayName.trim().length > 200) errors['displayName'] = 'Shop name is too long (200 characters).';

    if ((form.legalName ?? '').trim().length > 300) errors['legalName'] = 'Legal name is too long (300 characters).';

    // Typed text becomes a web address rather than being rejected for not already being one — see
    // `slugify` in the util lib, which both consoles share so their answers cannot diverge.
    const typedSlug = (form.desiredSlug ?? '').trim();
    if (typedSlug) {
      const problem = describeSlugProblem(typedSlug);
      if (problem) errors['desiredSlug'] = problem;
    }

    const contactEmail = (form.contactEmail ?? '').trim();
    if (contactEmail && !looksLikeEmail(contactEmail)) {
      errors['contactEmail'] = 'That does not look like an email address.';
    }

    if (!form.contactPhone.trim()) errors['contactPhone'] = 'A contact phone is required — it is how buyers reach the shop.';
    else if (form.contactPhone.trim().length > 32) errors['contactPhone'] = 'Phone number is too long (32 characters).';

    if ((form.registrationNumber ?? '').trim().length > 60) {
      errors['registrationNumber'] = 'Registration number is too long (60 characters).';
    }

    const ownerEmail = form.ownerEmail.trim();
    if (!ownerEmail) errors['ownerEmail'] = "The owner's email is required — it is the account they sign in with.";
    else if (!looksLikeEmail(ownerEmail)) errors['ownerEmail'] = 'That does not look like an email address.';

    if ((form.ownerFullName ?? '').trim().length > 200) errors['ownerFullName'] = 'Owner name is too long (200 characters).';
    if ((form.ownerPhone ?? '').trim().length > 32) errors['ownerPhone'] = 'Phone number is too long (32 characters).';

    return errors;
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
