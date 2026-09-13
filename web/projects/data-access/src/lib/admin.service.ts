import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from 'auth';
import { Observable } from 'rxjs';
import { AuditEntry, Category, Paged, Product, ProductListItem, Vendor, VendorStatus } from './models';

/** What an administrator fills in to create a shop on a seller's behalf. */
export interface CreateVendorRequest {
  legalName: string;
  displayName: string;
  desiredSlug?: string | null;
  contactEmail: string;
  contactPhone: string;
  registrationNumber?: string | null;
  ownerEmail: string;
  ownerFullName?: string | null;
  ownerPhone?: string | null;
  /** False parks it as a draft instead; the default is to approve on the spot. */
  approve?: boolean;
}

export interface AdminCreatedVendor {
  vendor: Vendor;
  ownerUserId: string;
  ownerEmail: string;
  /** True when this call created the login, in which case the password below is set. */
  ownerAccountCreated: boolean;
  /** Shown once, never retrievable. Null when the owner already had an account. */
  temporaryPassword: string | null;
}

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

  /**
   * Onboard a shop directly, for a seller who will not fill in the application form themselves.
   *
   * Skips the review queue — it lands Approved, with the owner already able to sign in on the
   * seller console. When the owner had no account, the response carries a one-time password that
   * exists nowhere else: show it, and do not fetch it again expecting it to be there.
   */
  createVendor(request: CreateVendorRequest): Observable<AdminCreatedVendor> {
    return this.http.post<AdminCreatedVendor>(`${this.baseUrl}/v1/admin/vendors`, request);
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

  // ── Categories ──

  createCategory(request: {
    name: string;
    parentId?: string | null;
    sortOrder: number;
    attributeSetId?: string | null;
    iconMediaId?: string | null;
  }): Observable<Category> {
    return this.http.post<Category>(`${this.baseUrl}/v1/admin/catalog/categories`, request);
  }

  updateCategory(
    categoryId: string,
    request: {
      name: string;
      sortOrder: number;
      isActive: boolean;
      attributeSetId?: string | null;
      iconMediaId?: string | null;
    },
  ): Observable<Category> {
    return this.http.put<Category>(`${this.baseUrl}/v1/admin/catalog/categories/${categoryId}`, request);
  }

  // ── Audit ──

  /** Append-only record of administrative actions. Read-only by design. */
  auditEntries(
    query: {
      action?: string;
      entityType?: string;
      entityId?: string;
      actorUserId?: string;
      from?: string;
      to?: string;
      page?: number;
      pageSize?: number;
    } = {},
  ): Observable<Paged<AuditEntry>> {
    return this.http.get<Paged<AuditEntry>>(`${this.baseUrl}/v1/admin/audit`, {
      params: this.params(query),
    });
  }

  /** Pulls a published product back off the storefront. Always requires a reason. */
  takedownProduct(productId: string, reason: string): Observable<Product> {
    return this.http.post<Product>(`${this.baseUrl}/v1/admin/catalog/products/${productId}/takedown`, {
      reason,
    });
  }
}
