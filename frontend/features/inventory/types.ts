// Matches ItemDto from the backend API
export interface Product {
  id: string;
  barcodes: string[];          // було: barcode: string | null
  name: string;
  categoryId: string | null;
  categoryName: string | null;
  segmentId: string | null;
  segmentName: string | null;
  unit: string;
  managementType: string;
  itemType: string;
  minStock: number;
  maxStock: number;
  safetyBuffer: number;
  storageTempMin: number | null;
  storageTempMax: number | null;
  shelfLifeDays: number | null;
  defaultSupplierId: string | null;
  defaultSupplierName: string | null;
  vatRate: number;
  pricePurchase: number | null;
  priceRetail: number | null;
  imageUrl: string | null;
  isActive: boolean;
  createdAt: string;
  manufacturer: string | null;    // НОВЕ
  countryOrigin: string | null;   // НОВЕ
  perishabilityClass: string;     // НОВЕ
  // Slice 3 — promo highlight on the catalog table. null unless the item has a running or
  // near-future promo (populated only by the paged catalog list).
  promoState: "active" | "upcoming" | null;
  promoStartsAt: string | null;
  promoDiscountPercent: number | null;
}

export interface BarcodeProductLookup {
  name: string;
  barcodes: string[];
  brand: string | null;
  manufacturer: string | null;
  countryOrigin: string | null;
  imageUrl: string | null;
  shelfLifeDays: number | null;
  unit: string | null;
}

export interface CreateProductPayload {
  name: string;
  barcodes?: string[];           // було: barcode?: string
  categoryId?: string;
  segmentId?: string;
  unit: string;
  managementType: string;
  itemType?: string;
  minStock: number;
  maxStock: number;
  safetyBuffer: number;
  storageTempMin?: number;
  storageTempMax?: number;
  shelfLifeDays?: number;
  defaultSupplierId?: string;
  vatRate: number;
  pricePurchase?: number;
  priceRetail?: number;
  imageUrl?: string;
  manufacturer?: string;         // НОВЕ
  countryOrigin?: string;        // НОВЕ
  perishabilityClass?: string;   // НОВЕ
}

export interface UpdateProductPayload extends CreateProductPayload {
  isActive: boolean;
}

/** Item-attribute defaults the product form pre-fills when this category is picked (Slice 2). */
export interface CategoryDefaults {
  vatRate: number | null;
  perishabilityClass: string | null;
  managementType: string | null;
  itemType: string | null;
  shelfLifeDays: number | null;
}

export interface CategoryDto {
  id: string;
  name: string;
  parentId: string | null;
  /** null when the category defines no defaults. */
  defaults?: CategoryDefaults | null;
}

// Opaque sort keys passed straight through to GET /api/items's `sortBy` query param — must
// match the backend contract's sort-key strings exactly (categories/sortBy backend task, run
// in parallel with this frontend task — confirm against its task log under `.claude/logs/tasks/`
// if the strings ever drift).
export type ProductSortBy =
  | "name"
  | "barcode"
  | "category"
  | "purchaseprice"
  | "retailprice"
  | "minstock"
  | "maxstock";
