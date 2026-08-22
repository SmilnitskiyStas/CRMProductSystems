import { api } from "@/lib/api";
import { viewFile } from "@/lib/download";
import type {
  SupplierListItemDto,
  SupplierProfileDto,
  SupplierItemDto,
  SupplierReviewDto,
  PublicSupplierReviewDto,
  PaginatedResponse,
  MarketplaceSearchRequest,
  CreateReviewRequest,
  SupplierProfileUpdateRequest,
  SupplierPlan,
  AddSupplierItemRequest,
  SupplierItemCategoryDto,
  SupplierChatMessageDto,
  SendSupplierChatMessageRequest,
  CooperationAgreementDto,
  CreateCooperationRequestBody,
  ChooseSigningMethodRequest,
  MarketplaceOrderDto,
  CreateMarketplaceOrderRequest,
  MarketplaceOrderReceiptDto,
  SupplierSupportTicketDto,
  SupportTicketMessageDto,
  CreateSupportTicketRequest,
  BarcodeConflict,
} from "../types";

export const marketplaceApi = {
  /** GET /api/marketplace/suppliers — paginated listing with optional filters */
  getSuppliers: (params: {
    page?: number;
    pageSize?: number;
    region?: string;
    category?: string;
    plan?: "all" | SupplierPlan;
  }) => {
    const qs = new URLSearchParams();
    qs.set("page", String(params.page ?? 1));
    qs.set("pageSize", String(params.pageSize ?? 20));
    if (params.region) qs.set("region", params.region);
    if (params.category) qs.set("category", params.category);
    if (params.plan && params.plan !== "all") qs.set("plan", params.plan);
    return api.get<PaginatedResponse<SupplierListItemDto>>(
      `/api/marketplace/suppliers?${qs.toString()}`
    );
  },

  /** GET /api/marketplace/suppliers/{id} — supplier profile */
  getSupplier: (id: string) =>
    api.get<SupplierProfileDto>(`/api/marketplace/suppliers/${id}`),

  /** GET /api/marketplace/suppliers/{id}/items — supplier catalog */
  getSupplierItems: (supplierId: string) =>
    api.get<SupplierItemDto[]>(`/api/marketplace/suppliers/${supplierId}/items`),

  /** GET /api/marketplace/suppliers/{id}/reviews — public, paginated (v4.1, TASK-285) */
  getSupplierReviews: (supplierId: string, page = 1, pageSize = 20) =>
    api.get<PaginatedResponse<PublicSupplierReviewDto>>(
      `/api/marketplace/suppliers/${supplierId}/reviews?page=${page}&pageSize=${pageSize}`
    ),

  /** POST /api/marketplace/search */
  search: (body: MarketplaceSearchRequest) =>
    api.post<SupplierListItemDto[]>("/api/marketplace/search", body),

  /** POST /api/marketplace/suppliers/{id}/reviews */
  createReview: (supplierId: string, body: CreateReviewRequest) =>
    api.post<SupplierReviewDto>(
      `/api/marketplace/suppliers/${supplierId}/reviews`,
      body
    ),

  /** GET /api/settings/supplier-profile */
  getMyProfile: () =>
    api.get<SupplierProfileUpdateRequest>("/api/settings/supplier-profile"),

  /** PUT /api/settings/supplier-profile */
  updateMyProfile: (body: SupplierProfileUpdateRequest) =>
    api.put<SupplierProfileDto>("/api/settings/supplier-profile", body),

  // ── Admin / platform endpoints (TASK-275) ─────────────────────────────────

  /** POST /api/admin/marketplace/suppliers/{id}/items */
  adminAddSupplierItem: (supplierId: string, body: AddSupplierItemRequest) =>
    api.post<SupplierItemDto>(
      `/api/admin/marketplace/suppliers/${supplierId}/items`,
      body
    ),

  /** DELETE /api/admin/marketplace/suppliers/{id}/items/{itemId} */
  adminDeleteSupplierItem: (supplierId: string, itemId: string) =>
    api.delete<void>(
      `/api/admin/marketplace/suppliers/${supplierId}/items/${itemId}`
    ),

  /** GET /api/marketplace/item-categories — static category/field registry (ADR-017). */
  getItemCategories: () =>
    api.get<SupplierItemCategoryDto[]>("/api/marketplace/item-categories"),

  /** GET /api/marketplace/suppliers/{id}/chat/messages — supplierId is the public supplier id. */
  getSupplierChatMessages: (supplierId: string) =>
    api.get<SupplierChatMessageDto[]>(`/api/marketplace/suppliers/${supplierId}/chat/messages`),

  /** POST /api/marketplace/suppliers/{id}/chat/messages */
  sendSupplierChatMessage: (supplierId: string, body: SendSupplierChatMessageRequest) =>
    api.post<SupplierChatMessageDto>(`/api/marketplace/suppliers/${supplierId}/chat/messages`, body),

  // ── Cooperation agreements (TASK-318) ──────────────────────────────────────

  /** POST /api/marketplace/suppliers/{id}/cooperation-requests — 409 = уже є жива угода */
  createCooperationRequest: (supplierId: string, body: CreateCooperationRequestBody) =>
    api.post<CooperationAgreementDto>(
      `/api/marketplace/suppliers/${supplierId}/cooperation-requests`,
      body
    ),

  /** GET /api/marketplace/cooperation — мої угоди, новіші перші */
  getMyCooperation: () =>
    api.get<CooperationAgreementDto[]>("/api/marketplace/cooperation"),

  /** GET /api/marketplace/cooperation/{agreementId}/contract — view PDF in a new tab */
  downloadAgreementContract: (agreementId: string) =>
    viewFile(`/api/marketplace/cooperation/${agreementId}/contract`),

  /** POST /api/marketplace/cooperation/{agreementId}/signing-method — client chooses physical/vchasno (TASK-319/320) */
  chooseSigningMethod: (agreementId: string, body: ChooseSigningMethodRequest) =>
    api.post<CooperationAgreementDto>(
      `/api/marketplace/cooperation/${agreementId}/signing-method`,
      body
    ),

  // ── Marketplace orders (TASK-318) ──────────────────────────────────────────

  /** POST /api/marketplace/suppliers/{id}/orders — 403 без активного договору, 400 якщо
   * лишилась нерозвʼязана barcode-конфліктна лінія (catalogAction "auto"/omitted) — див.
   * checkOrderConflicts, TASK-597 */
  createOrder: (supplierId: string, body: CreateMarketplaceOrderRequest) =>
    api.post<MarketplaceOrderDto>(`/api/marketplace/suppliers/${supplierId}/orders`, body),

  /** POST /api/marketplace/suppliers/{id}/orders/conflicts — pre-flight check before
   * createOrder (TASK-597): does any ordered supplier item's barcode already belong to a
   * DIFFERENT existing item in the client's own catalog? Empty array = no conflicts, safe
   * to submit as-is. */
  checkOrderConflicts: (supplierId: string, items: { supplierItemId: string; qty: number }[]) =>
    api.post<BarcodeConflict[]>(
      `/api/marketplace/suppliers/${supplierId}/orders/conflicts`,
      { items }
    ),

  /** GET /api/marketplace/my-orders */
  getMyOrders: () => api.get<MarketplaceOrderDto[]>("/api/marketplace/my-orders"),

  /** POST /api/marketplace/orders/{orderId}/cancel — лише зі статусу new */
  cancelOrder: (orderId: string, reason: string) =>
    api.post<MarketplaceOrderDto>(`/api/marketplace/orders/${orderId}/cancel`, { reason }),

  /** GET /api/marketplace/orders/{orderId}/receipt — read-only "what was actually received"
   * (TASK-586, ADR-033). 404 = no receiving session started yet (order isn't delivered, or
   * some edge case) — treat as "nothing to show", not an error. */
  getOrderReceipt: (orderId: string) =>
    api.get<MarketplaceOrderReceiptDto>(`/api/marketplace/orders/${orderId}/receipt`),

  // ── Support tickets (TASK-318, без угоди — відкрито всім клієнтам) ──────────

  /** POST /api/marketplace/suppliers/{id}/support-tickets */
  createSupportTicket: (supplierId: string, body: CreateSupportTicketRequest) =>
    api.post<SupplierSupportTicketDto>(
      `/api/marketplace/suppliers/${supplierId}/support-tickets`,
      body
    ),

  /** GET /api/marketplace/my-support-tickets — messages = null */
  getMySupportTickets: () =>
    api.get<SupplierSupportTicketDto[]>("/api/marketplace/my-support-tickets"),

  /** GET /api/marketplace/support-tickets/{ticketId} — з messages (старіші перші) */
  getSupportTicket: (ticketId: string) =>
    api.get<SupplierSupportTicketDto>(`/api/marketplace/support-tickets/${ticketId}`),

  /** POST /api/marketplace/support-tickets/{ticketId}/messages */
  sendSupportTicketMessage: (ticketId: string, body: string) =>
    api.post<SupportTicketMessageDto>(
      `/api/marketplace/support-tickets/${ticketId}/messages`,
      { body }
    ),
};
