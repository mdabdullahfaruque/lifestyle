import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from 'auth';
import { Observable } from 'rxjs';
import { BulkUploadResult, ImportJob, LibraryItem, Paged } from './models';

export interface ImageAssignment {
  imageId: string;
  productCode: string | null;
  position: number;
}

export interface RowDecision {
  rowId: string;
  skip: boolean;
}

/**
 * The vendor's image library and bulk product import (docs/08).
 *
 * Every endpoint is scoped to the vendor in the caller's token — the client never passes a vendor
 * id and could not widen its scope by doing so.
 */
@Injectable({ providedIn: 'root' })
export class ImportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  // ── Image library ──

  library(
    query: { unusedOnly?: boolean; search?: string; page?: number; pageSize?: number } = {},
  ): Observable<Paged<LibraryItem>> {
    let params = new HttpParams();
    if (query.unusedOnly !== undefined) params = params.set('unusedOnly', query.unusedOnly);
    if (query.search) params = params.set('search', query.search);
    if (query.page) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);

    return this.http.get<Paged<LibraryItem>>(`${this.baseUrl}/v1/vendor/media`, { params });
  }

  /**
   * Uploads up to 40 images in one request. One bad file does not fail the batch — the result
   * reports each file separately.
   */
  uploadToLibrary(files: File[]): Observable<BulkUploadResult> {
    const form = new FormData();
    for (const file of files) form.append('files', file, file.name);

    return this.http.post<BulkUploadResult>(`${this.baseUrl}/v1/vendor/media/bulk`, form);
  }

  deleteFromLibrary(mediaId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/v1/vendor/media/${mediaId}`);
  }

  // ── Import ──

  /** The per-category sheet, with that category's attribute columns and dropdowns already in it. */
  templateUrl(categoryId: string, format: 'xlsx' | 'csv' = 'xlsx'): string {
    return `${this.baseUrl}/v1/vendor/products/import/template?categoryId=${categoryId}&format=${format}`;
  }

  downloadTemplate(categoryId: string, format: 'xlsx' | 'csv' = 'xlsx'): Observable<Blob> {
    return this.http.get(this.templateUrl(categoryId, format), { responseType: 'blob' });
  }

  /** Stages a sheet for review. Writes nothing to the catalogue. */
  start(categoryId: string, sheet: File): Observable<ImportJob> {
    const form = new FormData();
    form.append('sheet', sheet, sheet.name);

    const params = new HttpParams().set('categoryId', categoryId);

    return this.http.post<ImportJob>(`${this.baseUrl}/v1/vendor/products/import`, form, { params });
  }

  job(jobId: string): Observable<ImportJob> {
    return this.http.get<ImportJob>(`${this.baseUrl}/v1/vendor/products/import/${jobId}`);
  }

  /** Applies the review grid's corrections. Still writes nothing to the catalogue. */
  revise(
    jobId: string,
    changes: {
      images?: ImageAssignment[];
      rows?: RowDecision[];
      confirmLiveUpdates?: string[];
    },
  ): Observable<ImportJob> {
    return this.http.patch<ImportJob>(
      `${this.baseUrl}/v1/vendor/products/import/${jobId}`,
      changes,
    );
  }

  commit(jobId: string): Observable<ImportJob> {
    return this.http.post<ImportJob>(
      `${this.baseUrl}/v1/vendor/products/import/${jobId}/commit`,
      {},
    );
  }

  cancel(jobId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/v1/vendor/products/import/${jobId}`);
  }

  errorsUrl(jobId: string): string {
    return `${this.baseUrl}/v1/vendor/products/import/${jobId}/errors`;
  }

  downloadErrors(jobId: string): Observable<Blob> {
    return this.http.get(this.errorsUrl(jobId), { responseType: 'blob' });
  }
}
