import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from 'auth';
import { Observable } from 'rxjs';
import { AttributeSet, Category, Paged, Product, ProductListItem, Storefront } from './models';

export interface BrowseQuery {
  categoryId?: string;
  search?: string;
  minPrice?: number;
  maxPrice?: number;
  sort?: 'price_asc' | 'price_desc' | 'name';
  page?: number;
  pageSize?: number;
}

/**
 * The public catalog. Note what is absent: no `vendorId`.
 *
 * On a storefront host the server derives the vendor from the `Host` header and filters to it
 * (FRD §19.4), so a storefront cannot be made to leak another shop's catalogue by tampering with a
 * query parameter. The client must never add one.
 */
@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  browse(query: BrowseQuery = {}): Observable<Paged<ProductListItem>> {
    let params = new HttpParams();

    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }

    return this.http.get<Paged<ProductListItem>>(`${this.baseUrl}/v1/catalog/products`, { params });
  }

  product(vendorId: string, slug: string): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl}/v1/catalog/vendors/${vendorId}/products/${slug}`);
  }

  categories(): Observable<Category[]> {
    return this.http.get<Category[]>(`${this.baseUrl}/v1/catalog/categories`);
  }

  attributeSets(): Observable<AttributeSet[]> {
    return this.http.get<AttributeSet[]>(`${this.baseUrl}/v1/catalog/attribute-sets`);
  }

  /** The shop this request's host belongs to. 404 on the marketplace host. */
  currentStorefront(): Observable<Storefront> {
    return this.http.get<Storefront>(`${this.baseUrl}/v1/catalog/storefronts/current`);
  }

  storefront(slug: string): Observable<Storefront> {
    return this.http.get<Storefront>(`${this.baseUrl}/v1/catalog/storefronts/${slug}`);
  }
}
