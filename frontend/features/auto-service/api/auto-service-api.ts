import { api } from "@/lib/api";
import type {
  WorkOrderDto,
  WorkOrderSummaryDto,
  WorkOrderStatus,
  WorkOrderLineDto,
  CustomerDto,
  VehicleDto,
  ServiceCatalogItemDto,
  CreateWorkOrderRequest,
  UpdateWorkOrderRequest,
  AddWorkOrderLineRequest,
  CreateCustomerRequest,
  UpdateCustomerRequest,
  CreateVehicleRequest,
  UpdateVehicleRequest,
  CreateServiceCatalogItemRequest,
  UpdateServiceCatalogItemRequest,
} from "../types";

export const autoServiceApi = {
  // ── Work Orders ──────────────────────────────────────────────────────────────

  /** GET /api/auto-service/work-orders?status=&vehicle_id= */
  getWorkOrders: (params?: { status?: WorkOrderStatus; vehicleId?: string }) => {
    const qs = new URLSearchParams();
    if (params?.status) qs.set("status", params.status);
    if (params?.vehicleId) qs.set("vehicle_id", params.vehicleId);
    const q = qs.toString();
    return api.get<WorkOrderSummaryDto[]>(
      `/api/auto-service/work-orders${q ? `?${q}` : ""}`
    );
  },

  /** GET /api/auto-service/work-orders/{id} */
  getWorkOrder: (id: string) =>
    api.get<WorkOrderDto>(`/api/auto-service/work-orders/${id}`),

  /** POST /api/auto-service/work-orders */
  createWorkOrder: (body: CreateWorkOrderRequest) =>
    api.post<WorkOrderDto>("/api/auto-service/work-orders", body),

  /** PUT /api/auto-service/work-orders/{id} */
  updateWorkOrder: (id: string, body: UpdateWorkOrderRequest) =>
    api.put<WorkOrderDto>(`/api/auto-service/work-orders/${id}`, body),

  /** POST /api/auto-service/work-orders/{id}/complete */
  completeWorkOrder: (id: string) =>
    api.post<WorkOrderDto>(`/api/auto-service/work-orders/${id}/complete`),

  /** POST /api/auto-service/work-orders/{id}/lines */
  addLine: (workOrderId: string, body: AddWorkOrderLineRequest) =>
    api.post<WorkOrderLineDto>(
      `/api/auto-service/work-orders/${workOrderId}/lines`,
      body
    ),

  /** DELETE /api/auto-service/work-orders/{id}/lines/{lineId} */
  removeLine: (workOrderId: string, lineId: string) =>
    api.delete<void>(
      `/api/auto-service/work-orders/${workOrderId}/lines/${lineId}`
    ),

  // ── Customers ────────────────────────────────────────────────────────────────

  /** GET /api/auto-service/customers */
  getCustomers: () =>
    api.get<CustomerDto[]>("/api/auto-service/customers"),

  /** POST /api/auto-service/customers */
  createCustomer: (body: CreateCustomerRequest) =>
    api.post<CustomerDto>("/api/auto-service/customers", body),

  /** PUT /api/auto-service/customers/{id} */
  updateCustomer: (id: string, body: UpdateCustomerRequest) =>
    api.put<CustomerDto>(`/api/auto-service/customers/${id}`, body),

  /** DELETE /api/auto-service/customers/{id} */
  deleteCustomer: (id: string) =>
    api.delete<void>(`/api/auto-service/customers/${id}`),

  // ── Vehicles ─────────────────────────────────────────────────────────────────

  /** GET /api/auto-service/vehicles?customer_id= */
  getVehicles: (customerId?: string) => {
    const qs = customerId ? `?customer_id=${customerId}` : "";
    return api.get<VehicleDto[]>(`/api/auto-service/vehicles${qs}`);
  },

  /** POST /api/auto-service/vehicles */
  createVehicle: (body: CreateVehicleRequest) =>
    api.post<VehicleDto>("/api/auto-service/vehicles", body),

  /** PUT /api/auto-service/vehicles/{id} */
  updateVehicle: (id: string, body: UpdateVehicleRequest) =>
    api.put<VehicleDto>(`/api/auto-service/vehicles/${id}`, body),

  // ── Service Catalog ───────────────────────────────────────────────────────────

  /** GET /api/auto-service/service-catalog */
  getServiceCatalog: () =>
    api.get<ServiceCatalogItemDto[]>("/api/auto-service/service-catalog"),

  /** POST /api/auto-service/service-catalog */
  createServiceItem: (body: CreateServiceCatalogItemRequest) =>
    api.post<ServiceCatalogItemDto>("/api/auto-service/service-catalog", body),

  /** PUT /api/auto-service/service-catalog/{id} */
  updateServiceItem: (id: string, body: UpdateServiceCatalogItemRequest) =>
    api.put<ServiceCatalogItemDto>(
      `/api/auto-service/service-catalog/${id}`,
      body
    ),

  /** DELETE /api/auto-service/service-catalog/{id} */
  deleteServiceItem: (id: string) =>
    api.delete<void>(`/api/auto-service/service-catalog/${id}`),
};
