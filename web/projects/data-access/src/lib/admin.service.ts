import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from 'auth';
import { Observable } from 'rxjs';
import { Paged, Product, ProductListItem, Vendor, VendorStatus } from './models';

/** The row shape the vendor queue returns — narrower than a full Vendor. */
export interface VendorListItem {
  id: string;
  displayName: string;
  legalName: string;
  slug: string;
  status: VendorStatus;
  contactEmail: string;
  contactPhone: string;
  submittedAt: string | null;
  approvedAt: string | null;
  createdAt: string | null;
}

/**
 * Platform administration. Every endpoint here is permission-gated server-side; the client's
 * guards only avoid routing someone to a screen that would 403 (docs/04 §3.4).
 */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private params(query: Record<string, unknown>): HttpParams {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return params;
  }

  // ── Vendor onboarding review ──

  vendors(
    query: { status?: string; search?: string; page?: number; pageSize?: number } = {},
  ): Observable<Paged<VendorListItem>> {
    return this.http.get<Paged<VendorListItem>>(`${this.baseUrl}/v1/admin/vendors`, {
      params: this.params(query),
    });
  }

  vendor(vendorId: string): Observable<Vendor> {
    return this.http.get<Vendor>(`${this.baseUrl}/v1/admin/vendors/${vendorId}`);
  }

  /** Approve or reject an application. A rejection must carry a reason — the vendor is told it. */
  reviewVendor(vendorId: string, approve: boolean, reason?: string | null): Observable<Vendor> {
    return this.http.post<Vendor>(`${this.baseUrl}/v1/admin/vendors/${vendorId}/review`, {
      approve,
      reason: reason ?? null,
    });
  }

  setSuspension(vendorId: string, suspend: boolean, reason?: string | null): Observable<Vendor> {
    return this.http.post<Vendor>(`${this.baseUrl}/v1/admin/vendors/${vendorId}/suspension`, {
      suspend,
      reason: reason ?? null,
    });
  }

  // ── Catalogue moderation ──

  productsForModeration(
    query: { status?: string; vendorId?: string; page?: number; pageSize?: number } = {},
  ): Observable<Paged<ProductListItem>> {
    return this.http.get<Paged<ProductListItem>>(`${this.baseUrl}/v1/admin/catalog/products`, {
      params: this.params(query),
    });
  }

  productForModeration(productId: string): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl}/v1/admin/catalog/products/${productId}`);
  }

  moderateProduct(productId: string, approve: boolean, note?: string | null): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/v1/admin/catalog/products/${productId}/moderate`, {
      approve,
      note: note ?? null,
    });
  }

  /** Pulls a published product back off the storefront. Always requires a reason. */
  takedownProduct(productId: string, reason: string): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/v1/admin/catalog/products/${productId}/takedown`, {
      reason,
    });
  }
}
