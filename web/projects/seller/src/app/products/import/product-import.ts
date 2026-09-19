import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  CatalogService,
  Category,
  ImageAssignment,
  ImportImage,
  ImportJob,
  ImportProduct,
  ImportService,
  ProblemDetails,
} from 'data-access';
import { firstValueFrom } from 'rxjs';

type Step = 'choose' | 'review' | 'done';

/**
 * Bulk product import (docs/08).
 *
 * One screen for both ways a seller arrives: files already named after their product codes, and a
 * folder of `IMG_0042.jpg`. The matcher does what it can on the server and everything it could not
 * place lands in "Unplaced images" for a drag. Nothing is written to the catalogue until Import is
 * pressed, and everything it writes is a draft.
 */
@Component({
  selector: 'app-product-import',
  imports: [RouterLink],
  templateUrl: './product-import.html',
  styleUrl: './product-import.scss',
})
export class ProductImport {
  private readonly catalog = inject(CatalogService);
  private readonly imports = inject(ImportService);
  private readonly router = inject(Router);

  protected readonly step = signal<Step>('choose');
  protected readonly leafCategories = signal<Category[]>([]);
  protected readonly categoryId = signal<string | null>(null);
  protected readonly job = signal<ImportJob | null>(null);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  /**
   * What is currently being dragged. Always a list, because a whole burst can be dragged at once
   * and a single tile is just a burst of one — the drop handler then needs no special case.
   */
  protected readonly dragging = signal<ImportImage[] | null>(null);

  protected readonly products = computed(() => this.job()?.products ?? []);
  protected readonly looseImages = computed(() => this.job()?.looseImages ?? []);

  /**
   * The unplaced tray, grouped into the bursts the server clustered by capture time. Photos with
   * no EXIF fall into a final "ungrouped" bucket rather than being hidden.
   */
  protected readonly looseGroups = computed(() => {
    const groups = new Map<number, ImportImage[]>();
    const ungrouped: ImportImage[] = [];

    for (const image of this.looseImages()) {
      if (image.clusterKey === null) {
        ungrouped.push(image);
        continue;
      }

      const existing = groups.get(image.clusterKey);
      if (existing) existing.push(image);
      else groups.set(image.clusterKey, [image]);
    }

    const shoots = [...groups.entries()]
      .sort((a, b) => a[0] - b[0])
      .map(([key, images]) => ({ key, images, isShoot: true }));

    return ungrouped.length > 0
      ? [...shoots, { key: -1, images: ungrouped, isShoot: false }]
      : shoots;
  });

  protected readonly needsConfirmation = computed(() =>
    this.products().filter((p) => p.affectsLiveProduct && !p.liveUpdateConfirmed),
  );

  protected readonly canCommit = computed(() => {
    const current = this.job();
    return !!current && current.status === 'NeedsReview' && current.importableRows > 0;
  });

  constructor() {
    void this.loadCategories();
  }

  // ── Step 1: pick a category and get its sheet ──

  protected onCategoryChange(event: Event): void {
    this.categoryId.set((event.target as HTMLSelectElement).value || null);
  }

  /**
   * Downloaded through HttpClient rather than a plain link so the request carries the bearer
   * token — the endpoint is authorised, and an anchor would send no Authorization header.
   */
  protected async downloadTemplate(format: 'xlsx' | 'csv'): Promise<void> {
    const category = this.categoryId();
    if (!category) return;

    this.busy.set(true);
    this.error.set(null);

    try {
      const blob = await firstValueFrom(this.imports.downloadTemplate(category, format));
      const name = this.leafCategories().find((c) => c.id === category)?.slug ?? 'products';
      this.save(blob, `lifestyle-import-${name}.${format}`);
    } catch {
      this.error.set('Could not download the template.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async onSheetChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    const category = this.categoryId();
    if (!file || !category) return;

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    try {
      const started = await firstValueFrom(this.imports.start(category, file));
      this.job.set(started);
      this.step.set('review');
    } catch (err) {
      this.error.set(this.describe(err, 'Could not read that sheet.'));
    } finally {
      this.busy.set(false);
    }
  }

  // ── Step 2: the review grid ──

  protected onDragStart(image: ImportImage): void {
    this.dragging.set([image]);
  }

  /** Drags a whole capture-time burst, which is the point of clustering them at all. */
  protected onDragStartGroup(images: ImportImage[]): void {
    this.dragging.set(images);
  }

  protected onDragEnd(): void {
    this.dragging.set(null);
  }

  protected allowDrop(event: DragEvent): void {
    // Without this the browser refuses the drop entirely.
    event.preventDefault();
  }

  protected async dropOnProduct(event: DragEvent, product: ImportProduct): Promise<void> {
    event.preventDefault();

    const images = this.dragging();
    this.dragging.set(null);

    const moving = (images ?? []).filter((i) => i.productCode !== product.productCode);
    if (moving.length === 0) return;

    // Appended after what the product already has, keeping the burst's own order.
    await this.assign(moving, product.productCode, product.images.length);
  }

  /** Dropping on the unplaced tray detaches images the matcher placed wrongly. */
  protected async dropOnLoose(event: DragEvent): Promise<void> {
    event.preventDefault();

    const images = this.dragging();
    this.dragging.set(null);

    const moving = (images ?? []).filter((i) => i.productCode !== null);
    if (moving.length === 0) return;

    await this.assign(moving, null, 0);
  }

  private async assign(images: ImportImage[], productCode: string | null, firstPosition: number) {
    const changes: ImageAssignment[] = images.map((image, index) => ({
      imageId: image.id,
      productCode,
      position: firstPosition + index,
    }));

    await this.revise({ images: changes });
  }

  protected async toggleRow(rowId: string, skip: boolean): Promise<void> {
    await this.revise({ rows: [{ rowId, skip }] });
  }

  protected async confirmLive(product: ImportProduct): Promise<void> {
    const rowIds = product.rows.map((r) => r.id);
    await this.revise({ confirmLiveUpdates: rowIds });
  }

  private async revise(changes: Parameters<ImportService['revise']>[1]): Promise<void> {
    const current = this.job();
    if (!current) return;

    this.busy.set(true);
    this.error.set(null);

    try {
      this.job.set(await firstValueFrom(this.imports.revise(current.id, changes)));
    } catch (err) {
      this.error.set(this.describe(err, 'Could not apply that change.'));
    } finally {
      this.busy.set(false);
    }
  }

  // ── Step 3: commit ──

  protected async commit(): Promise<void> {
    const current = this.job();
    if (!current) return;

    this.busy.set(true);
    this.error.set(null);

    try {
      const committed = await firstValueFrom(this.imports.commit(current.id));
      this.job.set(committed);
      this.step.set('done');
    } catch (err) {
      this.error.set(this.describe(err, 'Could not complete the import.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    const current = this.job();

    if (current && current.status === 'NeedsReview') {
      try {
        await firstValueFrom(this.imports.cancel(current.id));
      } catch {
        // Cancelling is a courtesy — the job expires on its own after 72 hours either way.
      }
    }

    this.job.set(null);
    this.step.set('choose');
  }

  protected async downloadErrors(): Promise<void> {
    const current = this.job();
    if (!current) return;

    this.busy.set(true);

    try {
      const blob = await firstValueFrom(this.imports.downloadErrors(current.id));
      this.save(blob, `import-errors-${current.id}.xlsx`);
    } catch {
      this.error.set('Could not download the error sheet.');
    } finally {
      this.busy.set(false);
    }
  }

  protected goToProducts(): void {
    void this.router.navigate(['/products']);
  }

  protected errorRows(product: ImportProduct) {
    return product.rows.filter((r) => r.outcome === 'Error');
  }

  private save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }

  private describe(err: unknown, fallback: string): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
    return problem?.detail ?? fallback;
  }

  private async loadCategories(): Promise<void> {
    try {
      const tree = await firstValueFrom(this.catalog.categories());
      this.leafCategories.set(flattenLeaves(tree));
    } catch {
      this.error.set('Could not load categories.');
    } finally {
      this.loading.set(false);
    }
  }
}

/** Only a leaf category can hold products, so only leaves are offered. */
function flattenLeaves(categories: Category[]): Category[] {
  const out: Category[] = [];

  const walk = (nodes: Category[]) => {
    for (const node of nodes) {
      if (node.isLeaf && node.isActive) out.push(node);
      if (node.children?.length) walk(node.children);
    }
  };

  walk(categories);
  return out;
}
