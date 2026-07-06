import { api } from "@/lib/api";
import { downloadFile } from "@/lib/download";
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
} from "../types";
import type { UserDto } from "@/features/users/types";

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

  /** GET .../contract — download PDF */
  downloadAgreementContract: (id: string, contractNumber: string | null) =>
    downloadFile(
      `${BASE}/cooperation-requests/${id}/contract`,
      `Договір_${contractNumber ?? id}.pdf`
    ),

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
};
