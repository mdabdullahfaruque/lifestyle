import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { BulkUploadItem, ImportService, LibraryItem, ProblemDetails } from 'data-access';
import { firstValueFrom } from 'rxjs';
import { downscaleImage, readExifCaptureTime } from 'util';

/** The API accepts at most 40 files per request, so a big drop is sent in batches. */
const BATCH_SIZE = 40;

/** Images fetched per page. The API caps a page at 100. */
const PAGE_SIZE = 60;

/**
 * The vendor's image library (docs/08 §2).
 *
 * Images live here whether or not they are on a product yet, which is what lets a seller upload a
 * shoot first and decide what goes where afterwards — the "bulk upload everything, sort it out
 * later" half of bulk import.
 */
@Component({
  selector: 'app-library',
  imports: [],
  templateUrl: './library.html',
  styleUrl: './library.scss',
})
export class Library {
  private readonly imports = inject(ImportService);

  protected readonly items = signal<LibraryItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly unusedOnly = signal(false);
  protected readonly deleting = signal<string | null>(null);

  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly totalCount = signal(0);

  /** Progress across all batches, so a 400-file upload has a truthful bar rather than 10 bars. */
  protected readonly uploading = signal(false);
  protected readonly uploadTotal = signal(0);
  protected readonly uploadDone = signal(0);
  protected readonly failures = signal<BulkUploadItem[]>([]);

  protected readonly progress = computed(() => {
    const total = this.uploadTotal();
    return total === 0 ? 0 : Math.round((this.uploadDone() / total) * 100);
  });

  protected readonly usedCount = computed(() => this.items().filter((i) => i.isUsed).length);

  constructor() {
    void this.load();
  }

  protected async toggleUnused(): Promise<void> {
    this.unusedOnly.update((v) => !v);
    await this.load();
  }

  protected async onFilesChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);

    // Clear immediately so choosing the same folder twice still fires a change event.
    input.value = '';

    if (files.length > 0) await this.upload(files);
  }

  /**
   * A ZIP is unpacked on the server, which is what makes folder-per-product work: `LS-1001/main.jpg`
   * arrives already tied to the product code, so a later import matches it without any dragging.
   */
  protected async onZipChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const archive = input.files?.[0];
    input.value = '';

    if (!archive) return;

    this.uploading.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.failures.set([]);
    this.uploadTotal.set(1);
    this.uploadDone.set(0);

    try {
      const result = await firstValueFrom(this.imports.uploadZipToLibrary(archive));

      this.failures.set(result.items.filter((i) => !i.succeeded));
      this.notice.set(
        result.failed === 0
          ? `Unpacked ${result.succeeded} image${result.succeeded === 1 ? '' : 's'} from ${archive.name}.`
          : `Unpacked ${result.succeeded}. ${result.failed} were skipped.`,
      );

      await this.load();
    } catch (err) {
      const problem =
        err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'Could not unpack that archive.');
    } finally {
      this.uploadDone.set(1);
      this.uploading.set(false);
    }
  }

  /**
   * Uploads in batches, reporting per-file failures rather than abandoning the run. A corrupt
   * photo in the middle of a shoot must not cost the seller the other 399.
   */
  private async upload(files: File[]): Promise<void> {
    this.uploading.set(true);
    this.error.set(null);
    this.notice.set(null);
    this.failures.set([]);
    this.uploadTotal.set(files.length);
    this.uploadDone.set(0);

    let succeeded = 0;
    const failed: BulkUploadItem[] = [];

    try {
      for (let start = 0; start < files.length; start += BATCH_SIZE) {
        const batch = files.slice(start, start + BATCH_SIZE);

        // Read the capture time *before* shrinking: re-encoding through a canvas destroys EXIF,
        // and capture time is how unnamed phone photos are grouped into shoots during an import.
        const capturedAt = await Promise.all(batch.map((file) => readExifCaptureTime(file)));

        // Shrunk in the browser: a phone JPEG is 8-12 MB against a 10 MB server cap, and an
        // iPhone's HEIC is a format the API does not accept at all but the browser can decode.
        const prepared = await Promise.all(batch.map((file) => downscaleImage(file)));

        try {
          const result = await firstValueFrom(this.imports.uploadToLibrary(prepared, capturedAt));
          succeeded += result.succeeded;
          failed.push(...result.items.filter((i) => !i.succeeded));
        } catch (err) {
          const problem =
            err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;

          failed.push(
            ...batch.map((file) => ({
              fileName: file.name,
              succeeded: false,
              mediaId: null,
              url: null,
              errorCode: problem?.title ?? 'upload_failed',
              errorMessage: problem?.detail ?? 'That batch could not be uploaded.',
            })),
          );
        }

        this.uploadDone.update((done) => done + batch.length);
      }

      this.failures.set(failed);
      this.notice.set(
        failed.length === 0
          ? `Added ${succeeded} image${succeeded === 1 ? '' : 's'} to your library.`
          : `Added ${succeeded}. ${failed.length} could not be uploaded.`,
      );

      await this.load();
    } finally {
      this.uploading.set(false);
    }
  }

  protected async remove(item: LibraryItem): Promise<void> {
    this.deleting.set(item.mediaId);
    this.error.set(null);
    this.notice.set(null);

    try {
      await firstValueFrom(this.imports.deleteFromLibrary(item.mediaId));
      this.items.update((items) => items.filter((i) => i.mediaId !== item.mediaId));
      this.notice.set(`Deleted ${item.fileName}.`);
    } catch (err) {
      const problem =
        err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
      this.error.set(problem?.detail ?? 'Could not delete that image.');
    } finally {
      this.deleting.set(null);
    }
  }

  protected thumb(item: LibraryItem): string {
    return item.derivatives['thumb'] ?? item.derivatives['card'] ?? item.url;
  }

  protected sizeLabel(bytes: number): string {
    return bytes < 1024 * 1024
      ? `${Math.max(1, Math.round(bytes / 1024))} KB`
      : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  /**
   * Loads one page and appends it. The library is built for sellers with hundreds of photos, so
   * showing only the first hundred with no way to reach the rest would hide most of their work —
   * and make the "not on a product yet" filter quietly lie.
   */
  protected async loadMore(): Promise<void> {
    await this.load(this.page() + 1);
  }

  private async load(page = 1): Promise<void> {
    this.loading.set(true);

    try {
      const result = await firstValueFrom(
        this.imports.library({
          unusedOnly: this.unusedOnly() || undefined,
          page,
          pageSize: PAGE_SIZE,
        }),
      );

      const items = result.items ?? [];

      this.items.update((existing) => (page === 1 ? items : [...existing, ...items]));
      this.page.set(result.page?.number ?? page);
      this.hasMore.set(result.page?.hasNext ?? false);
      this.totalCount.set(result.page?.totalCount ?? items.length);
    } catch {
      this.error.set('Could not load your image library.');
    } finally {
      this.loading.set(false);
    }
  }
}
