import { api } from "@/lib/api";
import { viewFile } from "@/lib/download";
import type {
  CooperationAgreementDto,
  CooperationStatus,
  MarketplaceOrderDto,
  SupplierSupportTicketDto,
  SupportTicketMessageDto,
  SupportTicketStatus,
} from "@/features/marketplace/types";
import type {
  CabinetProfile,
  CabinetProfileUpdateRequest,
  CabinetItem,
  CabinetAddItemRequest,
  CabinetUpdateItemRequest,
  CabinetReview,
  CabinetReplyToReviewRequest,
  SupplierReviewStats,
  CabinetMetrics,
  PagedResult,
  CabinetInviteStaffRequest,
  SupplierRoleDto,
  CreateSupplierRoleRequest,
  UpdateSupplierRoleRequest,
  SupplierTaskDto,
  CreateSupplierTaskRequest,
  UpdateSupplierTaskRequest,
  UpdateSupplierTaskStatusRequest,
  SupplierTaskFilters,
  SupplierClientDto,
  SupplierChatSessionDto,
  SupplierChatMessageDto,
  SendSupplierChatMessageRequest,
  SupplierContractSettingsDto,
  UpsertContractSettingsRequest,
  UpdateMarketplaceOrderStatusRequest,
  SetOrderDelayReasonRequest,
  SupplierWarehouse,
  CreateSupplierWarehouseRequest,
  UpdateSupplierWarehouseRequest,
  SupplierStock,
  SupplierStockReceipt,
  AddSupplierBatchRequest,
  AdjustSupplierStockRequest,
  CreateSupplierReceiptRequest,
  UpdateSupplierReceiptRequest,
  AddSupplierReceiptLineRequest,
  SupplierStockReceiptStatus,
} from "../types";
import type { UserDto } from "@/features/users/types";
import type { PagedResult as ApiPagedResult } from "@/lib/api-types";

const BASE = "/api/supplier-cabinet";

export const supplierCabinetApi = {
  /** GET /api/supplier-cabinet/profile */
  getProfile: () => api.get<CabinetProfile>(`${BASE}/profile`),

  /** PUT /api/supplier-cabinet/profile */
  updateProfile: (body: CabinetProfileUpdateRequest) =>
    api.put<CabinetProfile>(`${BASE}/profile`, body),

  /** POST /api/supplier-cabinet/profile/publish — toggles IsPublic */
  togglePublish: () => api.post<CabinetProfile>(`${BASE}/profile/publish`),

  /** GET /api/supplier-cabinet/items */
  getItems: () => api.get<CabinetItem[]>(`${BASE}/items`),

  /** POST /api/supplier-cabinet/items */
  addItem: (body: CabinetAddItemRequest) =>
    api.post<CabinetItem>(`${BASE}/items`, body),

  /** PUT /api/supplier-cabinet/items/{id} */
  updateItem: (id: string, body: CabinetUpdateItemRequest) =>
    api.put<CabinetItem>(`${BASE}/items/${id}`, body),

  /** DELETE /api/supplier-cabinet/items/{id} */
  deleteItem: (id: string) => api.delete<void>(`${BASE}/items/${id}`),

  /** GET /api/supplier-cabinet/reviews */
  getReviews: (page = 1, pageSize = 20) =>
    api.get<PagedResult<CabinetReview>>(
      `${BASE}/reviews?page=${page}&pageSize=${pageSize}`
    ),

  /** PUT /api/supplier-cabinet/reviews/{id}/reply */
  replyToReview: (id: string, replyText: string) =>
    api.put<CabinetReview>(`${BASE}/reviews/${id}/reply`, {
      replyText,
    } satisfies CabinetReplyToReviewRequest),

  /** GET /api/supplier-cabinet/reviews/stats */
  getReviewStats: () => api.get<SupplierReviewStats>(`${BASE}/reviews/stats`),

  /** GET /api/supplier-cabinet/metrics */
  getMetrics: () => api.get<CabinetMetrics>(`${BASE}/metrics`),

  /** GET /api/supplier-cabinet/staff */
  getStaff: () => api.get<UserDto[]>(`${BASE}/staff`),

  /** POST /api/supplier-cabinet/staff */
  inviteStaff: (body: CabinetInviteStaffRequest) =>
    api.post<UserDto>(`${BASE}/staff`, body),

  /** DELETE /api/supplier-cabinet/staff/{id} */
  deactivateStaff: (id: string) => api.delete<void>(`${BASE}/staff/${id}`),

  /** GET /api/supplier-cabinet/roles */
  getRoles: () => api.get<SupplierRoleDto[]>(`${BASE}/roles`),

  /** POST /api/supplier-cabinet/roles */
  createRole: (body: CreateSupplierRoleRequest) =>
    api.post<SupplierRoleDto>(`${BASE}/roles`, body),

  /** PUT /api/supplier-cabinet/roles/{id} */
  updateRole: (id: string, body: UpdateSupplierRoleRequest) =>
    api.put<SupplierRoleDto>(`${BASE}/roles/${id}`, body),

  /** DELETE /api/supplier-cabinet/roles/{id} */
  deleteRole: (id: string) => api.delete<void>(`${BASE}/roles/${id}`),

  /** GET /api/supplier-cabinet/tasks */
  getTasks: (filters?: SupplierTaskFilters) => {
    const qs = new URLSearchParams();
    if (filters?.assignedToMe) qs.set("assignedToMe", "true");
    if (filters?.clientTenantId) qs.set("clientTenantId", filters.clientTenantId);
    if (filters?.status) qs.set("status", filters.status);
    const query = qs.toString();
    return api.get<SupplierTaskDto[]>(`${BASE}/tasks${query ? `?${query}` : ""}`);
  },

  /** POST /api/supplier-cabinet/tasks */
  createTask: (body: CreateSupplierTaskRequest) =>
    api.post<SupplierTaskDto>(`${BASE}/tasks`, body),

  /** PUT /api/supplier-cabinet/tasks/{id} */
  updateTask: (id: string, body: UpdateSupplierTaskRequest) =>
    api.put<SupplierTaskDto>(`${BASE}/tasks/${id}`, body),

  /** PUT /api/supplier-cabinet/tasks/{id}/status */
  updateTaskStatus: (id: string, body: UpdateSupplierTaskStatusRequest) =>
    api.put<SupplierTaskDto>(`${BASE}/tasks/${id}/status`, body),

  /** GET /api/supplier-cabinet/clients */
  getClients: () => api.get<SupplierClientDto[]>(`${BASE}/clients`),

  /** GET /api/supplier-cabinet/chat/sessions */
  getChatSessions: () => api.get<SupplierChatSessionDto[]>(`${BASE}/chat/sessions`),

  /** GET /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages */
  getChatMessages: (clientTenantId: string) =>
    api.get<SupplierChatMessageDto[]>(`${BASE}/chat/sessions/${clientTenantId}/messages`),

  /** POST /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages */
  sendChatMessage: (clientTenantId: string, body: SendSupplierChatMessageRequest) =>
    api.post<SupplierChatMessageDto>(`${BASE}/chat/sessions/${clientTenantId}/messages`, body),

  // ── Cooperation requests / agreements (TASK-318) ────────────────────────────

  /** GET /api/supplier-cabinet/cooperation-requests?status= — новіші перші */
  getCooperationRequests: (status?: CooperationStatus) =>
    api.get<CooperationAgreementDto[]>(
      `${BASE}/cooperation-requests${status ? `?status=${status}` : ""}`
    ),

  /** POST .../approve — генерує договір, 400 якщо реквізити не заповнені */
  approveCooperationRequest: (id: string) =>
    api.post<CooperationAgreementDto>(`${BASE}/cooperation-requests/${id}/approve`),

  /** POST .../reject */
  rejectCooperationRequest: (id: string, reason: string) =>
    api.post<CooperationAgreementDto>(`${BASE}/cooperation-requests/${id}/reject`, { reason }),

  /** POST .../regenerate-contract — лише awaiting_signature */
  regenerateContract: (id: string) =>
    api.post<CooperationAgreementDto>(`${BASE}/cooperation-requests/${id}/regenerate-contract`),

  /** POST .../send-to-vchasno — 400 «Інтеграцію Вчасно не налаштовано.» */
  sendToVchasno: (id: string) =>
    api.post<CooperationAgreementDto>(`${BASE}/cooperation-requests/${id}/send-to-vchasno`),

  /** POST .../mark-signed — awaiting_signature → active */
  markAgreementSigned: (id: string) =>
    api.post<CooperationAgreementDto>(`${BASE}/cooperation-requests/${id}/mark-signed`),

  /** POST .../terminate — active → terminated (reason optional) */
  terminateAgreement: (id: string, reason?: string) =>
    api.post<CooperationAgreementDto>(`${BASE}/cooperation-requests/${id}/terminate`, {
      reason: reason || undefined,
    }),

  /** GET .../contract — view PDF in a new tab */
  downloadAgreementContract: (id: string) =>
    viewFile(`${BASE}/cooperation-requests/${id}/contract`),

  // ── Contract settings (requisites, TASK-318) ────────────────────────────────

  /** GET /api/supplier-cabinet/contract-settings — 404 поки не заповнено */
  getContractSettings: () =>
    api.get<SupplierContractSettingsDto>(`${BASE}/contract-settings`),

  /** PUT /api/supplier-cabinet/contract-settings */
  upsertContractSettings: (body: UpsertContractSettingsRequest) =>
    api.put<SupplierContractSettingsDto>(`${BASE}/contract-settings`, body),

  /** POST .../signature-image — multipart, png/jpg ≤2MB (потрібні збережені реквізити) */
  uploadSignatureImage: (file: File) => {
    const form = new FormData();
    form.append("file", file);
    return api.postForm<{ imageUrl: string }>(`${BASE}/contract-settings/signature-image`, form);
  },

  /** POST .../stamp-image — multipart, png/jpg ≤2MB */
  uploadStampImage: (file: File) => {
    const form = new FormData();
    form.append("file", file);
    return api.postForm<{ imageUrl: string }>(`${BASE}/contract-settings/stamp-image`, form);
  },

  // ── Marketplace orders (TASK-318) ───────────────────────────────────────────

  /** GET /api/supplier-cabinet/orders — новіші перші */
  getOrders: () => api.get<MarketplaceOrderDto[]>(`${BASE}/orders`),

  /** POST /api/supplier-cabinet/orders/{id}/status — дозволені переходи:
   * new→confirmed|cancelled, confirmed→shipped|cancelled, shipped→delivered */
  updateOrderStatus: (id: string, body: UpdateMarketplaceOrderStatusRequest) =>
    api.post<MarketplaceOrderDto>(`${BASE}/orders/${id}/status`, body),

  /** POST /api/supplier-cabinet/orders/{id}/delay-reason — тільки для status "shipped" */
  setOrderDelayReason: (id: string, body: SetOrderDelayReasonRequest) =>
    api.post<MarketplaceOrderDto>(`${BASE}/orders/${id}/delay-reason`, body),

  // ── Support tickets (TASK-318) ──────────────────────────────────────────────

  /** GET /api/supplier-cabinet/support-tickets — messages = null */
  getSupportTickets: () =>
    api.get<SupplierSupportTicketDto[]>(`${BASE}/support-tickets`),

  /** GET /api/supplier-cabinet/support-tickets/{id} — з messages (старіші перші) */
  getSupportTicket: (id: string) =>
    api.get<SupplierSupportTicketDto>(`${BASE}/support-tickets/${id}`),

  /** POST /api/supplier-cabinet/support-tickets/{id}/messages */
  addSupportTicketMessage: (id: string, body: string) =>
    api.post<SupportTicketMessageDto>(`${BASE}/support-tickets/${id}/messages`, { body }),

  /** POST /api/supplier-cabinet/support-tickets/{id}/status */
  updateSupportTicketStatus: (id: string, status: SupportTicketStatus) =>
    api.post<SupplierSupportTicketDto>(`${BASE}/support-tickets/${id}/status`, { status }),

  // ── Warehouses (supplier-portal expansion) ─────────────────────────────────

  /** GET /api/supplier-cabinet/warehouses */
  getWarehouses: () => api.get<SupplierWarehouse[]>(`${BASE}/warehouses`),

  /** POST /api/supplier-cabinet/warehouses */
  createWarehouse: (body: CreateSupplierWarehouseRequest) =>
    api.post<SupplierWarehouse>(`${BASE}/warehouses`, body),

  /** PUT /api/supplier-cabinet/warehouses/{id} */
  updateWarehouse: (id: string, body: UpdateSupplierWarehouseRequest) =>
    api.put<SupplierWarehouse>(`${BASE}/warehouses/${id}`, body),

  /** POST /api/supplier-cabinet/warehouses/{id}/deactivate */
  deactivateWarehouse: (id: string) =>
    api.post<void>(`${BASE}/warehouses/${id}/deactivate`),

  // ── Warehouse batch inventory (supplier-portal expansion Phase 2) ──────────

  /** GET /api/supplier-cabinet/warehouses/{warehouseId}/stock — paged, FEFO-ordered. */
  getWarehouseStock: (
    warehouseId: string,
    params: { supplierItemId?: string; page?: number; pageSize?: number } = {},
  ) => {
    const qs = new URLSearchParams();
    if (params.supplierItemId) qs.set("supplierItemId", params.supplierItemId);
    if (params.page) qs.set("page", String(params.page));
    if (params.pageSize) qs.set("pageSize", String(params.pageSize));
    const query = qs.toString();
    return api.get<ApiPagedResult<SupplierStock>>(
      `${BASE}/warehouses/${warehouseId}/stock${query ? `?${query}` : ""}`,
    );
  },

  /** POST /api/supplier-cabinet/warehouses/{warehouseId}/stock — add one batch. */
  addStockBatch: (warehouseId: string, body: AddSupplierBatchRequest) =>
    api.post<SupplierStock>(`${BASE}/warehouses/${warehouseId}/stock`, body),

  /** POST /api/supplier-cabinet/stock/{batchId}/adjust — stock-take / manual write-off. */
  adjustStockBatch: (batchId: string, body: AdjustSupplierStockRequest) =>
    api.post<SupplierStock>(`${BASE}/stock/${batchId}/adjust`, body),

  // ── Manual "what actually arrived" receiving ──────────────────────────────

  /** GET /api/supplier-cabinet/warehouses/{warehouseId}/receipts — bare array, newest first. */
  listReceipts: (
    warehouseId: string,
    params: { status?: SupplierStockReceiptStatus } = {},
  ) =>
    api.get<SupplierStockReceipt[]>(
      `${BASE}/warehouses/${warehouseId}/receipts${params.status ? `?status=${params.status}` : ""}`,
    ),

  /** POST /api/supplier-cabinet/warehouses/{warehouseId}/receipts — create a draft. */
  createReceipt: (warehouseId: string, body: CreateSupplierReceiptRequest) =>
    api.post<SupplierStockReceipt>(`${BASE}/warehouses/${warehouseId}/receipts`, body),

  /** GET /api/supplier-cabinet/receipts/{id} */
  getReceipt: (id: string) =>
    api.get<SupplierStockReceipt>(`${BASE}/receipts/${id}`),

  /** PUT /api/supplier-cabinet/receipts/{id} — draft header (warehouse / reference / notes). */
  updateReceipt: (id: string, body: UpdateSupplierReceiptRequest) =>
    api.put<SupplierStockReceipt>(`${BASE}/receipts/${id}`, body),

  /** POST /api/supplier-cabinet/receipts/{id}/lines — one row per (item, expiry, batch). */
  addReceiptLine: (id: string, body: AddSupplierReceiptLineRequest) =>
    api.post<SupplierStockReceipt>(`${BASE}/receipts/${id}/lines`, body),

  /** DELETE /api/supplier-cabinet/receipts/{id}/lines/{lineId} */
  removeReceiptLine: (id: string, lineId: string) =>
    api.delete<SupplierStockReceipt>(`${BASE}/receipts/${id}/lines/${lineId}`),

  /** POST /api/supplier-cabinet/receipts/{id}/finalize — 400 { error } names lines missing expiry. */
  finalizeReceipt: (id: string) =>
    api.post<SupplierStockReceipt>(`${BASE}/receipts/${id}/finalize`),
};
