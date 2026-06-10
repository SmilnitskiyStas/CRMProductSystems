// Matches CatalogProductDto from the backend API
export interface Product {
  id: string;
  barcode: string | null;
  name: string;
  categoryId: string | null;
  categoryName: string | null;
  segmentId: string | null;
  segmentName: string | null;
  unit: string;
  managementType: string;
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

export interface CreateProductPayload {
  name: string;
  barcode?: string;
  categoryId?: string;
  segmentId?: string;
  unit: string;
  managementType: string;
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
}

export interface UpdateProductPayload extends CreateProductPayload {
  isActive: boolean;
}
