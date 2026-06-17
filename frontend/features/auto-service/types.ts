// ─── Auto Service Feature Types ───────────────────────────────────────────────
// Matches API DTOs from TASK-231 (Auto Service API)

// ─── Work Order ───────────────────────────────────────────────────────────────

export type WorkOrderStatus =
  | "new"
  | "in_progress"
  | "waiting_parts"
  | "done"
  | "invoiced";

export type WorkOrderLineType = "service" | "part";

export interface WorkOrderLineDto {
  id: string;
  workOrderId: string;
  type: WorkOrderLineType;
  name: string;
  /** service catalog id (when type=service) */
  serviceCatalogId: string | null;
  /** inventory item id (when type=part) */
  itemId: string | null;
  qty: number;
  unitPrice: number;
  discount: number;
  totalPrice: number;
}

export interface WorkOrderDto {
  id: string;
  vehicleId: string;
  vehicleBrand: string;
  vehicleModel: string;
  licensePlate: string;
  mechanicUserId: string | null;
  mechanicName: string | null;
  status: WorkOrderStatus;
  notes: string | null;
  totalAmount: number;
  lineCount: number;
  lines: WorkOrderLineDto[];
  createdAt: string;
  completedAt: string | null;
}

export interface WorkOrderSummaryDto {
  id: string;
  vehicleId: string;
  vehicleBrand: string;
  vehicleModel: string;
  licensePlate: string;
  mechanicUserId: string | null;
  mechanicName: string | null;
  status: WorkOrderStatus;
  totalAmount: number;
  lineCount: number;
  createdAt: string;
  completedAt: string | null;
}

// ─── Customer ─────────────────────────────────────────────────────────────────

export interface CustomerDto {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  notes: string | null;
  vehicleCount: number;
  createdAt: string;
}

// ─── Vehicle ──────────────────────────────────────────────────────────────────

export interface VehicleDto {
  id: string;
  customerId: string;
  brand: string;
  model: string;
  year: number | null;
  vin: string | null;
  licensePlate: string;
  mileage: number | null;
  createdAt: string;
}

// ─── Service Catalog ──────────────────────────────────────────────────────────

export interface ServiceCatalogItemDto {
  id: string;
  name: string;
  description: string | null;
  linkedItemId: string | null;
  linkedItemName: string | null;
  defaultPrice: number;
  durationMinutes: number | null;
  isActive: boolean;
  createdAt: string;
}

// ─── Request Bodies ───────────────────────────────────────────────────────────

export interface CreateWorkOrderRequest {
  vehicleId: string;
  mechanicUserId?: string;
  notes?: string;
}

export interface UpdateWorkOrderRequest {
  status?: WorkOrderStatus;
  mechanicUserId?: string;
  notes?: string;
}

export interface AddWorkOrderLineRequest {
  type: WorkOrderLineType;
  name: string;
  serviceCatalogId?: string;
  itemId?: string;
  qty: number;
  unitPrice: number;
  discount?: number;
}

export interface CreateCustomerRequest {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
}

export interface UpdateCustomerRequest {
  name: string;
  phone?: string;
  email?: string;
  notes?: string;
}

export interface CreateVehicleRequest {
  customerId: string;
  brand: string;
  model: string;
  year?: number;
  vin?: string;
  licensePlate: string;
  mileage?: number;
}

export interface UpdateVehicleRequest {
  brand: string;
  model: string;
  year?: number;
  vin?: string;
  licensePlate: string;
  mileage?: number;
}

export interface CreateServiceCatalogItemRequest {
  name: string;
  description?: string;
  linkedItemId?: string;
  defaultPrice: number;
  durationMinutes?: number;
}

export interface UpdateServiceCatalogItemRequest {
  name: string;
  description?: string;
  linkedItemId?: string;
  defaultPrice: number;
  durationMinutes?: number;
  isActive: boolean;
}
