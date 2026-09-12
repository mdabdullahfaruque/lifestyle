import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  AttributeSet,
  CatalogService,
  Category,
  ProblemDetails,
  Product,
  ProductAttribute,
  VendorService,
} from 'data-access';
import { firstValueFrom } from 'rxjs';

/**
 * Editing an existing product.
 *
 * Split from the create screen rather than sharing one component, because they are genuinely
 * different shapes: creating posts one payload, while editing hits four separate endpoints
 * (basics, variants, stock, images) and each has to be able to fail on its own without
 * discarding the others' work.
 */
@Component({
  selector: 'app-product-edit',
  imports: [FormsModule, RouterLink],
  templateUrl: './product-edit.html',
  styleUrl: './product-edit.scss',
})
export class ProductEdit implements OnInit {
  private readonly vendors = inject(VendorService);
  private readonly catalog = inject(CatalogService);
  private readonly router = inject(Router);

  readonly productId = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly product = signal<Product | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly attributeSets = signal<AttributeSet[]>([]);

  protected form = { name: '', brand: '', shortDescription: '', description: '' };
  protected attributes: Record<string, string> = {};

  /** Pending stock deltas, keyed by variant id. Sent as deltas, never absolutes. */
  protected stockDelta: Record<string, number | null> = {};

  protected readonly attributeSet = computed<AttributeSet | null>(() => {
    const categoryId = this.product()?.categoryId;
    if (!categoryId) return null;

    const find = (list: Category[]): Category | null => {
      for (const c of list) {
        if (c.id === categoryId) return c;
        const hit = c.children?.length ? find(c.children) : null;
        if (hit) return hit;
      }
      return null;
    };

    const category = find(this.categories());
    if (!category?.attributeSetId) return null;
    return this.attributeSets().find((s) => s.id === category.attributeSetId) ?? null;
  });

  protected readonly productAttributes = computed<ProductAttribute[]>(
    () => this.attributeSet()?.attributes.filter((a) => !a.isVariantAxis) ?? [],
  );

  protected readonly canEdit = computed(() => {
    const status = this.product()?.status;
    // A product awaiting moderation is frozen: editing underneath a reviewer would mean they
    // approve something other than what they read.
    return status !== 'PendingReview';
  });

  ngOnInit(): void {
    void this.load();
  }

  protected optionSummary(options: Record<string, string>): string {
    return Object.entries(options)
      .map(([k, v]) => `${k.replace(/_/g, ' ')}: ${v}`)
      .join(' · ');
  }

  protected async saveBasics(): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;

    this.busy.set('basics');
    this.error.set(null);
    this.notice.set(null);

    try {
      const updated = await firstValueFrom(
        this.vendors.updateProduct(p.id, {
          categoryId: p.categoryId,
          name: this.form.name.trim(),
          description: this.form.description.trim() || null,
          shortDescription: this.form.shortDescription.trim() || null,
          brand: this.form.brand.trim() || null,
          attributes: Object.fromEntries(Object.entries(this.attributes).filter(([, v]) => v)),
        }),
      );
      this.product.set(updated);
      this.notice.set('Product details saved.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async savePrice(variantId: string, price: number | null, compareAt: number | null, isActive: boolean): Promise<void> {
    const p = this.product();
    if (!p || price === null || this.busy()) return;

    this.busy.set(variantId);
    this.error.set(null);
    this.notice.set(null);

    try {
      const updated = await firstValueFrom(
        this.vendors.updateVariant(p.id, variantId, { price, compareAtPrice: compareAt, isActive }),
      );
      this.product.set(updated);
      this.notice.set('Variant saved.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Stock moves as a delta so two people restocking at once both apply, rather than the second
   * silently overwriting the first with a stale absolute.
   */
  protected async applyStock(): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;

    const adjustments = Object.entries(this.stockDelta)
      .filter(([, delta]) => delta !== null && delta !== 0 && Number.isFinite(delta))
      .map(([variantId, delta]) => ({ variantId, delta: Number(delta) }));

    if (!adjustments.length) {
      this.error.set('Enter how many to add or remove first. A change of zero does nothing.');
      return;
    }

    this.busy.set('stock');
    this.error.set(null);
    this.notice.set(null);

    try {
      const updated = await firstValueFrom(this.vendors.adjustStock(p.id, adjustments));
      this.product.set(updated);
      this.stockDelta = {};
      this.notice.set('Stock updated.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async removeVariant(variantId: string): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;
    if (p.variants.length <= 1) {
      this.error.set('A product needs at least one variant. Delete the product instead.');
      return;
    }

    this.busy.set(variantId);
    this.error.set(null);

    try {
      this.product.set(await firstValueFrom(this.vendors.removeVariant(p.id, variantId)));
      this.notice.set('Variant removed.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async uploadImage(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const p = this.product();
    if (!file || !p) return;

    this.busy.set('image');
    this.error.set(null);

    try {
      const media = await firstValueFrom(this.vendors.upload(file, false));
      const images = [...p.images.map((i) => ({ mediaId: i.mediaId, altText: i.altText })), { mediaId: media.id, altText: null }];
      this.product.set(await firstValueFrom(this.vendors.setProductImages(p.id, images)));
      this.notice.set('Image added.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
      input.value = '';
    }
  }

  protected async removeImage(mediaId: string): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;

    this.busy.set('image');
    try {
      const images = p.images.filter((i) => i.mediaId !== mediaId).map((i) => ({ mediaId: i.mediaId, altText: i.altText }));
      this.product.set(await firstValueFrom(this.vendors.setProductImages(p.id, images)));
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  /** Moves an image one place towards the front; position 0 is what the grid shows. */
  protected async moveImage(mediaId: string, direction: -1 | 1): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;

    const order = p.images.map((i) => ({ mediaId: i.mediaId, altText: i.altText }));
    const from = order.findIndex((i) => i.mediaId === mediaId);
    const to = from + direction;
    if (from < 0 || to < 0 || to >= order.length) return;

    [order[from], order[to]] = [order[to], order[from]];

    this.busy.set('image');
    try {
      this.product.set(await firstValueFrom(this.vendors.setProductImages(p.id, order)));
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async submitForReview(): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;

    this.busy.set('submit');
    this.error.set(null);

    try {
      this.product.set(await firstValueFrom(this.vendors.submitProduct(p.id)));
      this.notice.set('Sent for review.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  /** Takes a live product off the storefront without deleting it. */
  protected async unpublish(): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;

    this.busy.set('unpublish');
    this.error.set(null);

    try {
      this.product.set(await firstValueFrom(this.vendors.unpublishProduct(p.id)));
      this.notice.set('Taken off the storefront. It stays here as a draft.');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async deleteProduct(): Promise<void> {
    const p = this.product();
    if (!p || this.busy()) return;
    if (!confirm(`Delete “${p.name}” permanently? This cannot be undone.`)) return;

    this.busy.set('delete');
    try {
      await firstValueFrom(this.vendors.deleteProduct(p.id));
      await this.router.navigateByUrl('/products');
    } catch (err) {
      this.error.set(this.describe(err));
      this.busy.set(null);
    }
  }

  protected imageUrl(mediaId: string): string {
    return `https://media.mylifestylemart.com/${mediaId.slice(0, 2)}/${mediaId}/thumb.png`;
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [product, cats, sets] = await Promise.all([
        firstValueFrom(this.vendors.product(this.productId())),
        firstValueFrom(this.catalog.categories()),
        firstValueFrom(this.catalog.attributeSets()),
      ]);

      this.product.set(product);
      this.categories.set(cats);
      this.attributeSets.set(sets);

      this.form = {
        name: product.name ?? '',
        brand: product.brand ?? '',
        shortDescription: product.shortDescription ?? '',
        description: product.description ?? '',
      };
      this.attributes = { ...product.attributes };
    } catch {
      this.error.set('Could not load that product.');
    } finally {
      this.loading.set(false);
    }
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
    const code = problem?.code ?? '';

    if (code === 'catalog.product_locked_for_review') {
      return 'This product is with a moderator and cannot be edited until they decide.';
    }
    if (code === 'catalog.insufficient_stock') return 'That would take stock below zero.';
    if (code === 'catalog.duplicate_sku') return 'Another variant already uses that SKU.';
    if (code.startsWith('catalog.axis_missing.')) {
      return `Every variant needs a ${code.split('.').pop()} value.`;
    }
    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');

    return problem?.detail ?? 'That change could not be saved. Please try again.';
  }
}
