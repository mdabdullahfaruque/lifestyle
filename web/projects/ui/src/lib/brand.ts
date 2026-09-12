import { Component, InjectionToken, computed, inject, input } from '@angular/core';

/**
 * Where the brand assets are served from, and what they are called.
 *
 * Every app provides this once, so changing the logo is changing one file on disk
 * (`web/assets/brand/logo-source.png`) — or, if a deployment needs a different brand entirely, overriding
 * this token in that app's `app.config.ts`. Nothing else in the codebase names a logo file.
 */
export interface BrandAssets {
  /** Stacked lockup: mark over wordmark over tagline. For a login card or a hero. */
  logo: string;
  /**
   * Mark beside wordmark. For slim chrome — a header constrains height, and the stacked lockup
   * at 40px leaves the name about six pixels tall and unreadable.
   */
  horizontal: string;
  /** The mark alone. Used where the lockup would be illegible — a tight header, an avatar. */
  mark: string;
  /** Accessible name. Not decorative: the logo is usually also the link home. */
  name: string;
}

export const BRAND_ASSETS = new InjectionToken<BrandAssets>('lifestyle.brand', {
  providedIn: 'root',
  factory: () => ({
    logo: '/brand/logo.png',
    horizontal: '/brand/logo-horizontal.png',
    mark: '/brand/logo-mark.png',
    name: 'MyLifestyleMart',
  }),
});

/**
 * The brand logo, everywhere.
 *
 * Sized by height rather than width: the lockup and the mark have very different aspect ratios, so
 * constraining the height is what keeps a header the same height whichever variant it shows.
 *
 * `eager` exists because the header logo is above the fold on every page — lazy-loading it is a
 * guaranteed flash of empty space exactly where the brand should be.
 */
@Component({
  selector: 'lib-brand-logo',
  standalone: true,
  template: `
    <img
      [src]="src()"
      [alt]="alt()"
      [style.height.px]="height()"
      [attr.width]="null"
      [attr.loading]="eager() ? null : 'lazy'"
      [attr.fetchpriority]="eager() ? 'high' : null"
      class="brand-logo"
    />
  `,
  styles: `
    :host { display: inline-flex; align-items: center; }
    .brand-logo { width: auto; max-width: 100%; display: block; }
  `,
})
export class BrandLogo {
  private readonly assets = inject(BRAND_ASSETS);

  /**
   * `horizontal` for chrome, `full` for a login card or hero, `mark` where even the name will
   * not fit — a favicon-sized slot or an avatar.
   */
  readonly variant = input<'horizontal' | 'full' | 'mark'>('horizontal');
  readonly height = input(36);
  readonly eager = input(true);

  /**
   * Overrides the accessible name. Default is the brand name — but when the logo sits inside a
   * link that is already labelled, pass an empty string so a screen reader does not read the
   * destination twice.
   */
  readonly label = input<string | null>(null);

  protected readonly src = computed(() => {
    switch (this.variant()) {
      case 'mark':
        return this.assets.mark;
      case 'full':
        return this.assets.logo;
      default:
        return this.assets.horizontal;
    }
  });

  protected readonly alt = computed(() => this.label() ?? this.assets.name);
}
