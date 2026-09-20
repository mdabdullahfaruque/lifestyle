import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CatalogService, Category, ImportService, LibraryItem, ProblemDetails } from 'data-access';
import { firstValueFrom } from 'rxjs';
import { downscaleImage, readExifCaptureTime } from 'util';

/** One pile of photos the seller has grouped, plus what they have typed on it. */
interface Pile {
  key: number;
  name: string;
  price: string;
  sku: string;
  stock: string;
  description: string;
  images: LibraryItem[];
}

const BATCH_SIZE = 40;

/**
 * Group photos into products (docs/08 §1.1, images-first).
 *
 * The seller uploads a whole shoot, drags the photos into piles, types a name and price on each
 * pile, and presses one button. No spreadsheet is involved — this is the path for someone who
 * photographs stock before they write anything down, which is most small sellers.
 *
 * Deliberately one screen: uploading, grouping and naming all happen in the same place, because
 * splitting them was what made the spreadsheet flow feel like homework.
 */
@Component({
  selector: 'app-group-photos',
  imports: [FormsModule],
  templateUrl: './group-photos.html',
  styleUrl: './group-photos.scss',
})
export class GroupPhotos {
  private readonly imports = inject(ImportService);
  private readonly catalog = inject(CatalogService);
  private readonly router = inject(Router);

  protected readonly leafCategories = signal<Category[]>([]);
  protected readonly categoryId = signal<string | null>(null);

  /** Photos not yet in any pile. */
  protected readonly loose = signal<LibraryItem[]>([]);
  protected readonly piles = signal<Pile[]>([]);

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly uploading = signal(false);
  protected readonly uploadDone = signal(0);
  protected readonly uploadTotal = signal(0);

  /** Multi-select in the loose tray, so a shoot can be grouped without dragging one at a time. */
  protected readonly selected = signal<Set<string>>(new Set());

  /** What is being dragged — always a list, so one tile and a selection share a code path. */
  private dragging: LibraryItem[] = [];

  private nextKey = 1;

  protected readonly readyCount = computed(
    () =>
      this.piles().filter((p) => p.name.trim() && Number(p.price) > 0 && p.images.length > 0)
        .length,
  );

  protected readonly createLabel = computed(() => {
    const n = this.readyCount();
    return n === 1 ? 'Create 1 product' : `Create ${n} products`;
  });

  constructor() {
    void this.init();
  }

  private async init(): Promise<void> {
    try {
      const tree = await firstValueFrom(this.catalog.categories());
      this.leafCategories.set(flattenLeaves(tree));
      await this.loadLibrary();
    } catch {
      this.error.set('Could not load your images.');
    } finally {
      this.loading.set(false);
    }
  }

  /** Only unused photos — one already on a product should not be silently claimed by another. */
  private async loadLibrary(): Promise<void> {
    const page = await firstValueFrom(this.imports.library({ unusedOnly: true, pageSize: 100 }));
    const placed = new Set(this.piles().flatMap((p) => p.images.map((i) => i.mediaId)));
    this.loose.set((page.items ?? []).filter((i) => !placed.has(i.mediaId)));
  }

  protected onCategoryChange(event: Event): void {
    this.categoryId.set((event.target as HTMLSelectElement).value || null);
  }

  // ── Uploading ──

  protected async onFilesChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;

    this.uploading.set(true);
    this.uploadTotal.set(files.length);
    this.uploadDone.set(0);
    this.error.set(null);

    try {
      for (let start = 0; start < files.length; start += BATCH_SIZE) {
        const batch = files.slice(start, start + BATCH_SIZE);

        // Capture time is read before downscaling, because re-encoding strips EXIF.
        const capturedAt = await Promise.all(batch.map((f) => readExifCaptureTime(f)));
        const prepared = await Promise.all(batch.map((f) => downscaleImage(f)));

        await firstValueFrom(this.imports.uploadToLibrary(prepared, capturedAt));
        this.uploadDone.update((d) => d + batch.length);
      }

      await this.loadLibrary();
      this.notice.set(`Uploaded ${files.length} photo(s). Now drag them into products below.`);
    } catch (err) {
      this.error.set(this.describe(err, 'Some photos could not be uploaded.'));
    } finally {
      this.uploading.set(false);
    }
  }

  // ── Selection ──

  protected toggle(item: LibraryItem): void {
    this.selected.update((set) => {
      const next = new Set(set);
      next.has(item.mediaId) ? next.delete(item.mediaId) : next.add(item.mediaId);
      return next;
    });
  }

  protected isSelected(item: LibraryItem): boolean {
    return this.selected().has(item.mediaId);
  }

  /** The keyboard-and-mouse alternative to dragging, and faster for a whole shoot. */
  protected groupSelected(): void {
    const chosen = this.loose().filter((i) => this.selected().has(i.mediaId));
    if (!chosen.length) return;

    this.addPile(chosen);
    this.selected.set(new Set());
  }

  // ── Piles ──

  protected addPile(images: LibraryItem[] = []): void {
    const ids = new Set(images.map((i) => i.mediaId));
    this.loose.update((l) => l.filter((i) => !ids.has(i.mediaId)));

    this.piles.update((p) => [
      ...p,
      { key: this.nextKey++, name: '', price: '', sku: '', stock: '0', description: '', images },
    ]);
  }

  protected removePile(pile: Pile): void {
    this.piles.update((p) => p.filter((x) => x.key !== pile.key));
    this.loose.update((l) => [...pile.images, ...l]);
  }

  // ── Drag and drop ──

  protected onDragStart(item: LibraryItem): void {
    // Dragging a selected tile carries the whole selection with it.
    this.dragging = this.isSelected(item)
      ? this.loose().filter((i) => this.selected().has(i.mediaId))
      : [item];
  }

  protected onDragStartFromPile(pile: Pile, item: LibraryItem): void {
    this.dragging = [item];
    this.piles.update((ps) =>
      ps.map((p) =>
        p.key === pile.key
          ? { ...p, images: p.images.filter((i) => i.mediaId !== item.mediaId) }
          : p,
      ),
    );
  }

  protected allowDrop(event: DragEvent): void {
    event.preventDefault();
  }

  protected dropOnPile(event: DragEvent, pile: Pile): void {
    event.preventDefault();
    const moving = this.dragging;
    this.dragging = [];
    if (!moving.length) return;

    const ids = new Set(moving.map((i) => i.mediaId));
    this.loose.update((l) => l.filter((i) => !ids.has(i.mediaId)));
    this.selected.set(new Set());

    this.piles.update((ps) =>
      ps.map((p) =>
        p.key === pile.key
          ? { ...p, images: [...p.images.filter((i) => !ids.has(i.mediaId)), ...moving] }
          : { ...p, images: p.images.filter((i) => !ids.has(i.mediaId)) },
      ),
    );
  }

  /** Dropping on the tray takes a photo back out of a product. */
  protected dropOnLoose(event: DragEvent): void {
    event.preventDefault();
    const moving = this.dragging;
    this.dragging = [];
    if (!moving.length) return;

    const ids = new Set(moving.map((i) => i.mediaId));
    this.piles.update((ps) =>
      ps.map((p) => ({ ...p, images: p.images.filter((i) => !ids.has(i.mediaId)) })),
    );
    this.loose.update((l) => [
      ...moving.filter((m) => !l.some((i) => i.mediaId === m.mediaId)),
      ...l,
    ]);
  }

  /** A new pile straight from a drop on the "new product" target. */
  protected dropOnNew(event: DragEvent): void {
    event.preventDefault();
    const moving = this.dragging;
    this.dragging = [];
    if (moving.length) {
      this.selected.set(new Set());
      this.addPile(moving);
    }
  }

  protected thumb(item: LibraryItem): string {
    return item.derivatives['thumb'] ?? item.derivatives['card'] ?? item.url;
  }

  protected isReady(p: Pile): boolean {
    return !!p.name.trim() && Number(p.price) > 0 && p.images.length > 0;
  }

  // ── Create ──

  protected async create(): Promise<void> {
    const category = this.categoryId();
    if (!category) {
      this.error.set('Choose a category first.');
      return;
    }

    const ready = this.piles().filter((p) => this.isReady(p));
    if (!ready.length) {
      this.error.set('Give each product a name and a price above zero.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);

    try {
      const result = await firstValueFrom(
        this.imports.createFromImages(
          category,
          ready.map((p) => ({
            name: p.name.trim(),
            price: Number(p.price),
            sku: p.sku.trim() || null,
            stockQuantity: Number(p.stock) || 0,
            description: p.description.trim() || null,
            mediaIds: p.images.map((i) => i.mediaId),
          })),
        ),
      );

      this.notice.set(`Created ${result.created} draft product(s).`);

      // Only the piles that were sent are cleared; anything half-filled stays for another go.
      const sent = new Set(ready.map((p) => p.key));
      this.piles.update((ps) => ps.filter((p) => !sent.has(p.key)));

      await this.loadLibrary();
    } catch (err) {
      this.error.set(this.describe(err, 'Could not create those products.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected goToProducts(): void {
    void this.router.navigate(['/products']);
  }

  private describe(err: unknown, fallback: string): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
    return problem?.detail ?? fallback;
  }
}

/** Only a leaf category can hold products. */
function flattenLeaves(categories: Category[]): Category[] {
  const out: Category[] = [];
  const walk = (nodes: Category[]) => {
    for (const n of nodes) {
      if (n.isLeaf && n.isActive) out.push(n);
      if (n.children?.length) walk(n.children);
    }
  };
  walk(categories);
  return out;
}
