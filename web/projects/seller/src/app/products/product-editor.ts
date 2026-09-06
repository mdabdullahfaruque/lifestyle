import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  AttributeSet,
  CatalogService,
  Category,
  ProblemDetails,
  ProductAttribute,
  VendorService,
} from 'data-access';
import { firstValueFrom } from 'rxjs';

interface VariantRow {
  sku: string;
  options: Record<string, string>;
  price: number | null;
  compareAtPrice: number | null;
  stockQuantity: number | null;
}

@Component({
  selector: 'app-product-editor',
  imports: [FormsModule, RouterLink],
  templateUrl: './product-editor.html',
  styleUrl: './product-editor.scss',
})
export class ProductEditor {
  private readonly catalog = inject(CatalogService);
  private readonly vendors = inject(VendorService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly categories = signal<Category[]>([]);
  protected readonly attributeSets = signal<AttributeSet[]>([]);

  protected form = {
    categoryId: '',
    name: '',
    brand: '',
    shortDescription: '',
    description: '',
  };

  /** Non-axis attributes, keyed by attribute code. */
  protected attributes: Record<string, string> = {};
  protected readonly variants = signal<VariantRow[]>([]);
  protected readonly imageIds = signal<string[]>([]);
  protected readonly uploading = signal(false);

  /** Leaf categories only — a product hangs off a leaf, which is what carries the attribute set. */
  protected readonly leaves = computed(() => {
    const out: Category[] = [];
    const walk = (c: Category, trail: string[]) => {
      const path = [...trail, c.name];
      if (c.isLeaf) out.push({ ...c, name: path.join(' › ') });
      c.children?.forEach((child) => walk(child, path));
    };
    this.categories().forEach((c) => walk(c, []));
    return out;
  });

  protected readonly attributeSet = computed<AttributeSet | null>(() => {
    const category = this.leaves().find((c) => c.id === this.form.categoryId);
    if (!category?.attributeSetId) return null;
    return this.attributeSets().find((s) => s.id === category.attributeSetId) ?? null;
  });

  /** The axes that multiply out into SKUs — size, colour and so on. */
  protected readonly axes = computed<ProductAttribute[]>(
    () => this.attributeSet()?.attributes.filter((a) => a.isVariantAxis) ?? [],
  );

  /** Everything else: material, occasion, gender… asked once for the whole product. */
  protected readonly productAttributes = computed<ProductAttribute[]>(
    () => this.attributeSet()?.attributes.filter((a) => !a.isVariantAxis) ?? [],
  );

  protected readonly canSave = computed(() => {
    if (!this.form.categoryId || !this.form.name.trim() || !this.form.description.trim()) return false;
    if (!this.variants().length) return false;

    return this.variants().every(
      (v) =>
        v.price !== null && v.price > 0 &&
        v.stockQuantity !== null && v.stockQuantity >= 0 &&
        this.axes().every((a) => v.options[a.code]),
    );
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const [cats, sets] = await Promise.all([
        firstValueFrom(this.catalog.categories()),
        firstValueFrom(this.catalog.attributeSets()),
      ]);
      this.categories.set(cats);
      this.attributeSets.set(sets);
    } catch {
      this.error.set('Could not load categories. Please refresh.');
    } finally {
      this.loading.set(false);
    }
  }

  /** Changing category invalidates every attribute and variant — they belong to the old set. */
  protected onCategoryChange(): void {
    this.attributes = {};
    this.variants.set([]);
    this.addVariant();
  }

  protected addVariant(): void {
    const index = this.variants().length;
    const options: Record<string, string> = {};
    for (const axis of this.axes()) options[axis.code] = '';

    this.variants.update((rows) => [
      ...rows,
      { sku: this.suggestSku(index), options, price: null, compareAtPrice: null, stockQuantity: 0 },
    ]);
  }

  protected removeVariant(index: number): void {
    this.variants.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected setOption(index: number, code: string, value: string): void {
    this.variants.update((rows) =>
      rows.map((row, i) => (i === index ? { ...row, options: { ...row.options, [code]: value } } : row)),
    );
  }

  /** A readable default the seller can overwrite; the API only requires it to be unique. */
  private suggestSku(index: number): string {
    const base = this.form.name
      .trim()
      .toUpperCase()
      .replace(/[^A-Z0-9]+/g, '-')
      .replace(/^-|-$/g, '')
      .slice(0, 12);
    return `${base || 'SKU'}-${index + 1}`;
  }

  protected async uploadImage(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploading.set(true);
    this.error.set(null);

    try {
      // Public, unlike KYC documents: product images are meant to be served and CDN-cached.
      const media = await firstValueFrom(this.vendors.upload(file, false));
      this.imageIds.update((ids) => [...ids, media.id]);
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.uploading.set(false);
      input.value = '';
    }
  }

  protected removeImage(id: string): void {
    this.imageIds.update((ids) => ids.filter((x) => x !== id));
  }

  protected async save(submitForReview: boolean): Promise<void> {
    if (this.busy() || !this.canSave()) return;
    this.busy.set(true);
    this.error.set(null);

    try {
      const created = await firstValueFrom(
        this.vendors.createProduct({
          categoryId: this.form.categoryId,
          name: this.form.name.trim(),
          description: this.form.description.trim(),
          shortDescription: this.form.shortDescription.trim() || null,
          brand: this.form.brand.trim() || null,
          variants: this.variants().map((v) => ({
            sku: v.sku.trim(),
            options: v.options,
            price: v.price!,
            compareAtPrice: v.compareAtPrice,
            stockQuantity: v.stockQuantity!,
          })),
          imageMediaIds: this.imageIds(),
          // Blank optional attributes must not be sent — the API validates against allowed values.
          attributes: Object.fromEntries(Object.entries(this.attributes).filter(([, v]) => v)),
        }),
      );

      if (submitForReview) await firstValueFrom(this.vendors.submitProduct(created.id));
      await this.router.navigateByUrl('/products');
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(false);
    }
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
    const code = problem?.code ?? '';

    // catalog.axis_missing.<axis> — the category needs a value on every axis, on every variant.
    if (code.startsWith('catalog.axis_missing.')) {
      return `Every variant needs a ${code.split('.').pop()} value.`;
    }
    if (code === 'catalog.duplicate_sku') return 'Two variants share a SKU. Each must be unique.';
    if (code === 'catalog.attribute_invalid') return problem?.detail ?? 'One of the product details is not an accepted value.';
    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');

    return problem?.detail ?? 'Could not save this product. Please check the fields and try again.';
  }
}
