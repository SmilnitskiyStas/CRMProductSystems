export type WorkOrderStatus =
  | 'new'
  | 'in_progress'
  | 'waiting_parts'
  | 'done'
  | 'invoiced';

export type WorkOrderLineType = 'service' | 'part';

export interface AsCustomer {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
  vehicleCount?: number;
}

export interface AsVehicle {
  id: string;
  customerId: string;
  brand: string;
  model: string;
  year: number | null;
  vin: string | null;
  licensePlate: string;
  mileage: number | null;
  notes: string | null;
}

export interface AsServiceCatalogItem {
  id: string;
  name: string;
  description: string | null;
  defaultPrice: number;
  durationHours: number | null;
}

export interface AsWorkOrderLine {
  id: string;
  workOrderId: string;
  type: WorkOrderLineType;
  serviceCatalogId: string | null;
  itemId: string | null;
  name: string;
  qty: number;
  price: number;
  discount: number;
  total: number;
}

export interface AsWorkOrder {
  id: string;
  vehicleId: string;
  vehicleBrand: string;
  vehicleModel: string;
  vehicleLicensePlate: string;
  vehicleVin: string | null;
  customerId: string;
  customerName: string;
  mechanicUserId: string | null;
  mechanicName: string | null;
  status: WorkOrderStatus;
  notes: string | null;
  createdAt: string;
  completedAt: string | null;
  lines: AsWorkOrderLine[];
  totalAmount: number;
}

export interface CreateWorkOrderLinePayload {
  type: WorkOrderLineType;
  serviceCatalogId?: string;
  itemId?: string;
  qty: number;
  price: number;
  discount?: number;
}

export interface CreateCustomerPayload {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
}

export interface SparePartItem {
  id: string;
  name: string;
  barcode: string | null;
  priceRetail: number;
  itemType: string;
}

export const STATUS_LABELS: Record<WorkOrderStatus, string> = {
  new: 'Новий',
  in_progress: 'В роботі',
  waiting_parts: 'Очікує запчастин',
  done: 'Виконано',
  invoiced: 'Виставлено рахунок',
};

export const STATUS_COLORS: Record<WorkOrderStatus, string> = {
  new: 'text-gray-700 bg-gray-100',
  in_progress: 'text-blue-700 bg-blue-100',
  waiting_parts: 'text-yellow-700 bg-yellow-100',
  done: 'text-green-700 bg-green-100',
  invoiced: 'text-purple-700 bg-purple-100',
};

export const COMPLETABLE_STATUSES: WorkOrderStatus[] = [
  'new',
  'in_progress',
  'waiting_parts',
];
