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
          logoMediaId: this.shop.vendor()?.logoMediaId ?? null,
          bannerMediaId: this.shop.vendor()?.bannerMediaId ?? null,
          accentColour: this.form.accentColour.trim() || null,
          whatsAppNumber: this.form.whatsAppNumber.trim() || null,
        }),
      );
      this.shop.set(vendor);
      this.notice.set('Shop settings saved.');
    } catch (err) {
      const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(
        problem?.errors
          ? Object.values(problem.errors).flat().join(' ')
          : (problem?.detail ?? 'Could not save your settings.'),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
