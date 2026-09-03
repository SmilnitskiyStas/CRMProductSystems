/**
 * One hit from the category typeahead — `GET /api/categories/search?q=&limit=`
 * (supplier-portal expansion #8, Phase 6e). `parentName` disambiguates same-named leaves;
 * `itemCount` is the caller tenant's own catalog items in that category (0 for a pure supplier
 * tenant — harmless). Matches backend `CategorySearchResultDto`.
 */
export interface CategorySearchResult {
  id: string;
  name: string;
  parentName: string | null;
  itemCount: number;
}

export interface CatalogProductDto {
  id: string;
  barcode: string | null;
  name: string;
  categoryId: string | null;
  categoryName: string | null;
  segmentId: string | null;
  segmentName: string | null;
  unit: string;
  managementType: "MTS" | "MTO" | "NA" | "NM";
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
}
