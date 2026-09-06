import { Injectable, computed, inject, signal } from '@angular/core';
import { Vendor, VendorService } from 'data-access';
import { firstValueFrom } from 'rxjs';

/**
 * The signed-in user's shop, and where they are in onboarding.
 *
 * A seller's console looks completely different depending on this: someone with no application
 * needs the apply form, someone awaiting review needs a status page, and only an approved vendor
 * gets products and settings. Resolving it once here keeps that decision out of every component.
 */
export type ShopStage = 'none' | 'draft' | 'pending' | 'rejected' | 'approved' | 'suspended';

@Injectable({ providedIn: 'root' })
export class ShopStore {
  private readonly vendors = inject(VendorService);

  private readonly vendorSignal = signal<Vendor | null>(null);
  private readonly loaded = signal(false);

  readonly vendor = this.vendorSignal.asReadonly();
  readonly isLoaded = this.loaded.asReadonly();

  readonly stage = computed<ShopStage>(() => {
    const v = this.vendorSignal();
    if (!v) return 'none';
    switch (v.status) {
      case 'Draft': return 'draft';
      case 'PendingReview': return 'pending';
      case 'Rejected': return 'rejected';
      case 'Approved': return 'approved';
      case 'Suspended': return 'suspended';
      default: return 'none';
    }
  });

  readonly canSell = computed(() => this.stage() === 'approved');

  /**
   * Reads the application first: it is the only endpoint that answers for a user who is not yet a
   * vendor. `/v1/vendor/profile` requires a seller-scoped token, which they will not have.
   * A 404 from both simply means "has not applied", which is a normal state, not an error.
   */
  async refresh(): Promise<void> {
    try {
      this.vendorSignal.set(await firstValueFrom(this.vendors.myApplication()));
    } catch {
      try {
        this.vendorSignal.set(await firstValueFrom(this.vendors.profile()));
      } catch {
        this.vendorSignal.set(null);
      }
    } finally {
      this.loaded.set(true);
    }
  }

  set(vendor: Vendor): void {
    this.vendorSignal.set(vendor);
    this.loaded.set(true);
  }

  clear(): void {
    this.vendorSignal.set(null);
    this.loaded.set(false);
  }
}
