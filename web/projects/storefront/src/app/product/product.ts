import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { CatalogService, Product, ProductShop, Variant } from 'data-access';
import { firstValueFrom } from 'rxjs';

import { useMedia } from '../media';
import { formatMoney } from '../price';

@Component({
  selector: 'app-product',
  imports: [],
  templateUrl: './product.html',
  styleUrl: './product.scss',
})
export class ProductPage implements OnInit {
  private readonly catalog = inject(CatalogService);
  protected readonly media = useMedia();

  /** Bound from the route by withComponentInputBinding(). */
  readonly vendorId = input.required<string>();
  readonly slug = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly product = signal<Product | null>(null);
  protected readonly shop = signal<ProductShop | null>(null);
  protected readonly copied = signal(false);

  /** The buyer's current pick, one value per variant axis. */
  protected readonly picked = signal<Record<string, string>>({});
  protected readonly activeImage = signal(0);

  /** Axis order comes from the variants themselves, so it matches how the category defines them. */
  protected readonly axes = computed(() => {
    const first = this.product()?.variants?.[0];
    return first ? Object.keys(first.options) : [];
  });

  protected readonly valuesByAxis = computed(() => {
    const out: Record<string, string[]> = {};
    for (const axis of this.axes()) {
      const seen = new Set<string>();
      for (const v of this.product()?.variants ?? []) {
        if (v.options[axis]) seen.add(v.options[axis]);
      }
      out[axis] = [...seen];
    }
    return out;
  });

  /** The variant matching every picked axis — null until the buyer has chosen a full combination. */
  protected readonly selected = computed<Variant | null>(() => {
    const p = this.product();
    const picks = this.picked();
    if (!p) return null;
    const axes = this.axes();
    if (axes.some((a) => !picks[a])) return null;
    return p.variants.find((v) => axes.every((a) => v.options[a] === picks[a])) ?? null;
  });

  protected readonly price = computed(() => {
    const p = this.product();
    if (!p) return null;
    const v = this.selected();

    const currency = v?.currency ?? p.price.currency;
    const now = v ? v.price : p.price.min;
    const was = v?.compareAtPrice ?? null;

    const nowNum = Number(now);
    const wasNum = was === null ? null : Number(was);
    const hasSaving = wasNum !== null && Number.isFinite(wasNum) && wasNum > nowNum;

    return {
      now: !v && p.price.min !== p.price.max ? `${formatMoney(now, currency)}+` : formatMoney(now, currency),
      was: hasSaving ? formatMoney(String(wasNum), currency) : null,
      // Rounded down so the badge can never overstate the discount.
      saving: hasSaving ? Math.floor(((wasNum - nowNum) / wasNum) * 100) : null,
    };
  });

  /** Availability band, never an exact count (design note: seller-maintained stock drifts). */
  protected readonly stock = computed(() => {
    const v = this.selected();
    const qty = v ? v.stockQuantity : (this.product()?.totalStock ?? 0);
    if (qty <= 0) return { text: 'Out of stock', low: true, sellable: false };
    if (qty <= 5) return { text: 'Only a few left', low: true, sellable: true };
    return { text: 'In stock', low: false, sellable: true };
  });

  /**
   * A short human-quotable code the buyer and seller can both refer to. Derived from the product
   * id so it is stable, and shown on the page because Messenger cannot pre-fill a message.
   */
  protected readonly reference = computed(() => {
    const id = this.product()?.id ?? '';
    return `LS-${id.replace(/-/g, '').slice(-6).toUpperCase()}`;
  });

  /**
   * v1 ordering: no cart, no checkout. The CTA opens WhatsApp with the order pre-filled — product,
   * chosen options, price and the reference code (Plan §6.2).
   */
  protected readonly whatsAppLink = computed(() => {
    const p = this.product();
    if (!p) return null;
    const number = this.shop()?.whatsAppNumber?.replace(/[^\d]/g, '');
    if (!number) return null;

    const picks = Object.entries(this.picked())
      .map(([axis, value]) => `${this.axisLabel(axis)}: ${value}`)
      .join(', ');

    const message = [
      `Hi ${this.shop()?.displayName ?? ''}, I would like to order:`,
      '',
      p.name,
      picks || null,
      this.price()?.now ? `Price: ${this.price()!.now}` : null,
      `Ref: ${this.reference()}`,
    ]
      .filter((line) => line !== null)
      .join('\n');

    return `https://wa.me/${number}?text=${encodeURIComponent(message)}`;
  });

  protected readonly specs = computed(() => {
    const attrs = this.product()?.attributes ?? {};
    return Object.entries(attrs).map(([code, value]) => ({ label: this.axisLabel(code), value }));
  });

  /**
   * Loads on init, not in the constructor: required inputs are not set until after construction,
   * so reading vendorId()/slug() there throws NG0950 — which the load's own catch turned into a
   * silent "Product not found" on a product that exists.
   */
  ngOnInit(): void {
    void this.load();
  }

  protected axisLabel(code: string): string {
    return code
      .replace(/_/g, ' ')
      .replace(/\beu\b/i, '(EU)')
      .replace(/^./, (c) => c.toUpperCase());
  }

  protected pick(axis: string, value: string): void {
    this.picked.update((current) => ({ ...current, [axis]: value }));
  }

  /** Whether choosing this value still leaves a real variant available. */
  protected isAvailable(axis: string, value: string): boolean {
    const others = Object.entries(this.picked()).filter(([a]) => a !== axis);
    return (this.product()?.variants ?? []).some(
      (v) => v.options[axis] === value && others.every(([a, val]) => v.options[a] === val) && v.stockQuantity > 0,
    );
  }

  protected async copyReference(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.reference());
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1800);
    } catch {
      // Clipboard is unavailable (insecure context, or permission refused). The code is on the
      // page in full, so the buyer can still read it out — nothing to recover from.
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const p = await firstValueFrom(this.catalog.product(this.vendorId(), this.slug()));
      this.product.set(p);
      // The shop rides along with the product, so there is no second request to fail.
      this.shop.set(p.shop ?? null);

      // Preselect the first in-stock variant so price and availability are never ambiguous.
      const first = p.variants.find((v) => v.stockQuantity > 0) ?? p.variants[0];
      if (first) this.picked.set({ ...first.options });
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
