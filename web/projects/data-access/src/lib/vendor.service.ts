import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from 'auth';
import { Observable } from 'rxjs';
import { MediaAsset, Paged, Product, ProductListItem, Vendor } from './models';

export interface VariantInput {
  sku: string;
  options: Record<string, string>;
  price: number;
  compareAtPrice?: number | null;
  stockQuantity: number;
}

export interface CreateProductRequest {
  categoryId: string;
  name: string;
  description?: string | null;
  shortDescription?: string | null;
  brand?: string | null;
  variants: VariantInput[];
  imageMediaIds?: string[];
  attributes?: Record<string, string>;
}

/**
 * Vendor Admin. Every endpoint is scoped to the vendor in the caller's token — the client never
 * passes a vendor id, and could not widen its scope by doing so.
 */
@Injectable({ providedIn: 'root' })
export class VendorService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  // ── Onboarding: runs on an ordinary signed-in token, before any seller role exists ──

  apply(request: {
    legalName: string;
    displayName: string;
    desiredSlug?: string | null;
    contactEmail: string;
    contactPhone: string;
    registrationNumber?: string | null;
  }): Observable<Vendor> {
    return this.http.post<Vendor>(`${this.baseUrl}/v1/vendor-applications`, request);
  }

  myApplication(): Observable<Vendor> {
    return this.http.get<Vendor>(`${this.baseUrl}/v1/vendor-applications/mine`);
  }

  attachDocument(kind: string, mediaId: string, fileName: string): Observable<Vendor> {
    return this.http.post<Vendor>(`${this.baseUrl}/v1/vendor-applications/documents`, {
      kind,
      mediaId,
      fileName,
    });
  }

  submitApplication(): Observable<Vendor> {
    return this.http.post<Vendor>(`${this.baseUrl}/v1/vendor-applications/submit`, {});
  }

  // ── Post-approval ──

  profile(): Observable<Vendor> {
    return this.http.get<Vendor>(`${this.baseUrl}/v1/vendor/profile`);
  }

  updateStorefront(request: {
    displayName: string;
    about?: string | null;
    logoMediaId?: string | null;
    bannerMediaId?: string | null;
    accentColour?: string | null;
    whatsAppNumber?: string | null;
  }): Observable<Vendor> {
    return this.http.put<Vendor>(`${this.baseUrl}/v1/vendor/storefront`, request);
  }

  products(query: { status?: string; search?: string; page?: number; pageSize?: number } = {}) {
    let params = new HttpParams();

    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }

    return this.http.get<Paged<ProductListItem>>(`${this.baseUrl}/v1/vendor/products`, { params });
  }

  product(productId: string): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl}/v1/vendor/products/${productId}`);
  }

  createProduct(request: CreateProductRequest): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/v1/vendor/products`, request);
  }

  submitProduct(productId: string): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/v1/vendor/products/${productId}/submit`, {});
  }

  /**
   * Stock moves as deltas, not absolutes, so two concurrent adjustments both apply rather than
   * the second silently overwriting the first.
   */
  adjustStock(productId: string, adjustments: { variantId: string; delta: number }[]): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/v1/vendor/products/${productId}/stock`, {
      adjustments,
    });
  }

  /**
   * KYC documents MUST be uploaded with isPrivate=true: private files are excluded from the
   * public media host and readable only through the authorised endpoint. Product images stay
   * public so the CDN can cache them.
   */
  upload(file: File, isPrivate = false): Observable<MediaAsset> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (isPrivate) form.append('private', 'true');

    return this.http.post<MediaAsset>(`${this.baseUrl}/v1/media`, form);
  }
}
