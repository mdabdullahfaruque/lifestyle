import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ProblemDetails, VendorService } from 'data-access';
import { firstValueFrom } from 'rxjs';

import { ShopStore } from '../shop.store';

@Component({
  selector: 'app-settings',
  imports: [FormsModule],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly vendors = inject(VendorService);
  protected readonly shop = inject(ShopStore);

  protected form = {
    displayName: '',
    about: '',
    accentColour: '',
    whatsAppNumber: '',
  };

  protected readonly busy = signal(false);
  protected readonly uploading = signal<'logo' | 'banner' | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  constructor() {
    void this.hydrate();
  }

  private async hydrate(): Promise<void> {
    if (!this.shop.isLoaded()) await this.shop.refresh();
    const v = this.shop.vendor();
    if (!v) return;

    this.form = {
      displayName: v.displayName ?? '',
      about: v.about ?? '',
      accentColour: v.accentColour ?? '',
      whatsAppNumber: v.whatsAppNumber ?? '',
    };
  }

  protected imageUrl(mediaId: string, variant: 'thumb' | 'card' = 'card'): string {
    return `https://media.mylifestylemart.com/${mediaId.slice(0, 2)}/${mediaId}/${variant}.png`;
  }

  /**
   * Logo and banner are public images, unlike KYC documents: they are meant to be served from the
   * media host and cached at the edge.
   */
  protected async uploadBranding(kind: 'logo' | 'banner', event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploading.set(kind);
    this.error.set(null);
    this.notice.set(null);

    try {
      const media = await firstValueFrom(this.vendors.upload(file, false));
      const current = this.shop.vendor();

      const vendor = await firstValueFrom(
        this.vendors.updateStorefront({
          displayName: this.form.displayName.trim() || current?.displayName || '',
          about: this.form.about.trim() || null,
          logoMediaId: kind === 'logo' ? media.id : (current?.logoMediaId ?? null),
          bannerMediaId: kind === 'banner' ? media.id : (current?.bannerMediaId ?? null),
          accentColour: this.form.accentColour.trim() || null,
          whatsAppNumber: this.form.whatsAppNumber.trim() || null,
        }),
      );

      this.shop.set(vendor);
      this.notice.set(kind === 'logo' ? 'Logo updated.' : 'Banner updated.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.uploading.set(null);
      input.value = '';
    }
  }

  protected async removeBranding(kind: 'logo' | 'banner'): Promise<void> {
    const current = this.shop.vendor();
    if (!current || this.busy()) return;

    this.uploading.set(kind);
    try {
      const vendor = await firstValueFrom(
        this.vendors.updateStorefront({
          displayName: this.form.displayName.trim() || current.displayName,
          about: this.form.about.trim() || null,
          logoMediaId: kind === 'logo' ? null : (current.logoMediaId ?? null),
          bannerMediaId: kind === 'banner' ? null : (current.bannerMediaId ?? null),
          accentColour: this.form.accentColour.trim() || null,
          whatsAppNumber: this.form.whatsAppNumber.trim() || null,
        }),
      );
      this.shop.set(vendor);
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.uploading.set(null);
    }
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
    if (problem?.code === 'media.content_type_not_allowed') return 'Use a JPG, PNG or WebP image.';
    if (problem?.code === 'media.too_large') return 'That image is over the 10 MB limit.';
    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
    return problem?.detail ?? 'That change could not be saved.';
  }

  protected async save(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    try {
      const vendor = await firstValueFrom(
        this.vendors.updateStorefront({
          displayName: this.form.displayName.trim(),
          about: this.form.about.trim() || null,
          // Existing media ids are preserved by sending them back unchanged; this screen does not
          // manage logo or banner yet, so pass through what the shop already has.
          // Pass the current ids through: this screen edits text, and omitting them would clear
          // the shop's logo and banner as a side effect of saving a description.
          logoMediaId: this.shop.vendor()?.logoMediaId ?? null,
          bannerMediaId: this.shop.vendor()?.bannerMediaId ?? null,
          accentColour: this.form.accentColour.trim() || null,
          whatsAppNumber: this.form.whatsAppNumber.trim() || null,
        }),
      );
      this.shop.set(vendor);
      this.notice.set('Shop settings saved.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(false);
    }
  }
}
