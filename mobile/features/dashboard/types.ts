export interface DashboardStats {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
  needsVerification: number;
  total: number;
}

export interface AiOrderListItem {
  id: string;
  locationId: string;
  locationName: string;
  generatedAt: string;
  orderDate: string;
  status: string;
  itemsCount: number;
  aiModel: string;
  tokensUsed: number | null;
}

export interface RecentMovement {
  id: string;
  movementType: string;
  productId: string;
  productName: string | null;
  fromLocationId: string | null;
  fromLocationName: string | null;
  toLocationId: string | null;
  toLocationName: string | null;
  quantity: number;
  quantityBefore: number | null;
  quantityAfter: number | null;
  unitPrice: number | null;
  totalAmount: number | null;
  referenceId: string | null;
  referenceType: string | null;
  notes: string | null;
  createdAt: string;
}

export interface RecentMovementsPage {
  items: RecentMovement[];
  total: number;
  page: number;
  pageSize: number;
}

export const MOVEMENT_LABELS: Record<string, string> = {
  receipt:     'Прийомка',
  transfer:    'Переміщення',
  write_off:   'Списання',
  adjustment:  'Коригування',
  pos_sale:    'Продаж',
};

export const MOVEMENT_REFERENCE_LABELS: Record<string, string> = {
  receipt: 'Прийомка',
  marketplace_receipt: 'Приймання замовлення',
  transfer: 'Переміщення',
  write_off: 'Списання',
  adjustment: 'Коригування залишку',
  pos_sale: 'Продаж через касу',
  production: 'Виробництво',
  inventory: 'Інвентаризація',
};

export function movementNumber(movement: Pick<RecentMovement, 'id'>): string {
  return movement.id.slice(0, 8).toUpperCase();
}
