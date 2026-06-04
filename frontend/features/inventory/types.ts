export interface Product {
  id: string;
  sku: string;
  name: string;
  description: string | null;
  category: string;
  unit: string;
  costPrice: number;
  salePrice: number;
  stockQuantity: number;
  reorderLevel: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateProductPayload {
  sku: string;
  name: string;
  description?: string;
  category: string;
  unit: string;
  costPrice: number;
  salePrice: number;
  reorderLevel: number;
}

export interface UpdateProductPayload {
  name: string;
  description?: string;
  category: string;
  unit: string;
  costPrice: number;
  salePrice: number;
  reorderLevel: number;
  isActive: boolean;
}
