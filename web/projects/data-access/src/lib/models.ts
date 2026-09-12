/**
 * Hand-written for Phase 0/1 so the apps compile and the shapes are reviewable.
 *
 * These are replaced by a client generated from the API's OpenAPI document
 * (`ng-openapi-gen`), committed and diffed in CI — a backend change that breaks the client then
 * fails the build instead of surfacing at runtime (docs/04 §8). Until that step lands, treat this
 * file as the contract and keep it in step with the API by hand.
 */

/** Money is a decimal string on the wire, never a float (FRD §19.1). */
export interface MoneyRange {
  min: string;
  max: string;
  currency: string;
}

export interface PageMeta {
  number: number;
  size: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface Paged<T> {
  items: T[];
  page: PageMeta;
}

export type ProductStatus = 'Draft' | 'PendingReview' | 'Published' | 'Rejected' | 'Unpublished';

export interface ProductListItem {
  id: string;
  vendorId: string;
  name: string;
  slug: string;
  status: ProductStatus;
  price: MoneyRange;
  totalStock: number;
  primaryImageMediaId: string | null;
  publishedAt: string | null;
  /**
   * Highest "was" price across active variants, when it beats the selling price. Null when
   * nothing is discounted — so a card can badge a saving without fetching every product.
   */
  compareAtPrice: string | null;
}

export interface Variant {
  id: string;
  sku: string;
  options: Record<string, string>;
  price: string;
  compareAtPrice: string | null;
  currency: string;
  stockQuantity: number;
  isActive: boolean;
  weightGrams: number | null;
  barCode: string | null;
}

export interface ProductImage {
  mediaId: string;
  altText: string | null;
  position: number;
}

/** The seller, as a buyer sees them on a product page. Public product endpoint only. */
export interface ProductShop {
  id: string;
  displayName: string;
  slug: string;
  whatsAppNumber: string | null;
  accentColour: string | null;
  logoMediaId: string | null;
}

export interface Product {
  id: string;
  vendorId: string;
  categoryId: string;
  name: string;
  slug: string;
  description: string | null;
  shortDescription: string | null;
  brand: string | null;
  status: ProductStatus;
  moderationNote: string | null;
  submittedAt: string | null;
  publishedAt: string | null;
  price: MoneyRange;
  totalStock: number;
  variants: Variant[];
  images: ProductImage[];
  attributes: Record<string, string>;
  /** Null on the vendor's own views — there the caller already knows which shop they are. */
  shop: ProductShop | null;
}

export interface Category {
  id: string;
  name: string;
  slug: string;
  parentId: string | null;
  depth: number;
  sortOrder: number;
  isLeaf: boolean;
  isActive: boolean;
  attributeSetId: string | null;
  iconMediaId: string | null;
  children: Category[];
}

export type AttributeDataType = 'Text' | 'Select' | 'Number' | 'Boolean';

export interface ProductAttribute {
  id: string;
  name: string;
  code: string;
  dataType: AttributeDataType;
  isRequired: boolean;
  /** True when this attribute's values multiply out into SKUs (size, colour). */
  isVariantAxis: boolean;
  isFilterable: boolean;
  sortOrder: number;
  unit: string | null;
  allowedValues: string[];
}

export interface AttributeSet {
  id: string;
  name: string;
  code: string;
  description: string | null;
  attributes: ProductAttribute[];
}

export type VendorStatus = 'Draft' | 'PendingReview' | 'Approved' | 'Rejected' | 'Suspended';

export interface Storefront {
  id: string;
  displayName: string;
  slug: string;
  about: string | null;
  logoMediaId: string | null;
  bannerMediaId: string | null;
  accentColour: string | null;
  whatsAppNumber: string | null;
  customDomain: string | null;
}

export interface Vendor extends Storefront {
  legalName: string;
  contactEmail: string;
  contactPhone: string;
  registrationNumber: string | null;
  status: VendorStatus;
  statusReason: string | null;
  submittedAt: string | null;
  approvedAt: string | null;
  customDomainStatus: string;
  documents: VendorDocument[];
  staff: VendorStaff[];
}

export interface VendorDocument {
  id: string;
  kind: string;
  mediaId: string;
  fileName: string;
  uploadedAt: string;
}

export interface VendorStaff {
  userId: string;
  role: string;
  joinedAt: string;
}

export interface MediaAsset {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  url: string;
  derivatives: Record<string, string>;
}

/** RFC 9457 problem+json, as the API returns it (docs/04 §3.5). */
export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail?: string;
  /** Stable machine-readable code. Branch on this, never on `detail`. */
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}
