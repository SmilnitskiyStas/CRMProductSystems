export interface MarketplaceOrderItem {
  id: string;
  itemName: string;
  unit: string | null;
  qty: number;
}

export interface MarketplaceOrder {
  id: string;
  orderNumber: string;
  supplierName: string;
  status: 'new' | 'confirmed' | 'shipped' | 'delivered' | 'cancelled';
  totalAmount: number;
  shippedAt: string | null;
  estimatedDeliveryDays: number | null;
  destinationStoreId: string | null;
  items: MarketplaceOrderItem[];
}

export interface MarketplaceOrderReceiptItem {
  id: string;
  marketplaceOrderItemId: string;
  productId: string | null;
  itemNameSnapshot: string;
  productName: string | null;
  quantityOrdered: number;
  quantityReceived: number | null;
  expiryDate: string | null;
  batchNumber: string | null;
  discrepancyNotes: string | null;
  isResolved: boolean;
}

export interface MarketplaceOrderReceipt {
  id: string;
  marketplaceOrderId: string;
  destinationStoreId: string;
  destinationStoreName: string;
  status: 'draft' | 'received';
  receivedAt: string | null;
  createdAt: string;
  updatedAt: string;
  items: MarketplaceOrderReceiptItem[];
}

export interface CatalogProduct {
  id: string;
  name: string;
  barcode?: string | null;
}

export interface UpdateReceiptItemRequest {
  productId: string;
  quantityReceived: number;
  expiryDate: string;
  batchNumber?: string | null;
  discrepancyNotes?: string | null;
}
