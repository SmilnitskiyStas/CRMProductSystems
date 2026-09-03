"use client";

// Supplier-side cooperation hooks (TASK-318): cooperation requests/agreements,
// contract settings + image uploads, marketplace orders, support tickets.
// Server state — React Query only (frontend-structure.md).

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import type {
  CooperationStatus,
  SupportTicketStatus,
} from "@/features/marketplace/types";
import type {
  UpsertContractSettingsRequest,
  UpdateMarketplaceOrderStatusRequest,
  SetOrderDelayReasonRequest,
  ShipOrderRequest,
} from "../types";
import { SUPPLIER_INVENTORY_KEYS } from "./useSupplierInventory";

// ─── Query keys ───────────────────────────────────────────────────────────────

export const CABINET_COOP_KEYS = {
  requests: (status?: CooperationStatus) =>
    ["supplier-cabinet", "cooperation-requests", status ?? "all"] as const,
  allRequests: ["supplier-cabinet", "cooperation-requests"] as const,
  contractSettings: ["supplier-cabinet", "contract-settings"] as const,
  orders: ["supplier-cabinet", "orders"] as const,
  tickets: ["supplier-cabinet", "support-tickets"] as const,
  ticket: (id: string) => ["supplier-cabinet", "support-ticket", id] as const,
};

// ─── Cooperation requests / agreements ────────────────────────────────────────

export function useCooperationRequests(status?: CooperationStatus) {
  return useQuery({
    queryKey: CABINET_COOP_KEYS.requests(status),
    queryFn: () => supplierCabinetApi.getCooperationRequests(status),
    staleTime: 15_000,
    retry: false,
  });
}

/** Спільний invalidate для всіх мутацій над угодами. */
function useAgreementMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.allRequests });
    },
  });
}

export function useApproveCooperationRequest() {
  return useAgreementMutation((id: string) =>
    supplierCabinetApi.approveCooperationRequest(id)
  );
}

export function useRejectCooperationRequest() {
  return useAgreementMutation(({ id, reason }: { id: string; reason: string }) =>
    supplierCabinetApi.rejectCooperationRequest(id, reason)
  );
}

export function useRegenerateContract() {
  return useAgreementMutation((id: string) => supplierCabinetApi.regenerateContract(id));
}

export function useSendToVchasno() {
  return useAgreementMutation((id: string) => supplierCabinetApi.sendToVchasno(id));
}

export function useMarkAgreementSigned() {
  return useAgreementMutation((id: string) => supplierCabinetApi.markAgreementSigned(id));
}

export function useTerminateAgreement() {
  return useAgreementMutation(({ id, reason }: { id: string; reason?: string }) =>
    supplierCabinetApi.terminateAgreement(id, reason)
  );
}

// ─── Contract settings ────────────────────────────────────────────────────────

/** 404 (ще не заповнено) прилітає як ApiError зі status 404 — сторінка трактує його як "порожньо". */
export function useContractSettings() {
  return useQuery({
    queryKey: CABINET_COOP_KEYS.contractSettings,
    queryFn: supplierCabinetApi.getContractSettings,
    staleTime: 30_000,
    retry: false,
  });
}

export function useUpsertContractSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertContractSettingsRequest) =>
      supplierCabinetApi.upsertContractSettings(body),
    onSuccess: (settings) => {
      queryClient.setQueryData(CABINET_COOP_KEYS.contractSettings, settings);
    },
  });
}

export function useUploadContractImage(kind: "signature" | "stamp") {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (file: File) =>
      kind === "signature"
        ? supplierCabinetApi.uploadSignatureImage(file)
        : supplierCabinetApi.uploadStampImage(file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.contractSettings });
    },
  });
}

// ─── Orders ───────────────────────────────────────────────────────────────────

export function useCabinetOrders() {
  return useQuery({
    queryKey: CABINET_COOP_KEYS.orders,
    queryFn: supplierCabinetApi.getOrders,
    staleTime: 15_000,
    retry: false,
  });
}

export function useUpdateCabinetOrderStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateMarketplaceOrderStatusRequest }) =>
      supplierCabinetApi.updateOrderStatus(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.orders });
    },
  });
}

// ── Batch-consuming shipment (supplier-portal expansion Phase 3, plan D4) ──────

/** GET .../ship-suggestion — editable FEFO allocation plan for one warehouse. Enable only
 *  while the ship modal is open with the module on and a warehouse picked. */
export function useShipSuggestion(orderId: string | null, warehouseId: string | null) {
  return useQuery({
    queryKey: ["supplier-cabinet", "ship-suggestion", orderId, warehouseId] as const,
    queryFn: () => supplierCabinetApi.getShipSuggestion(orderId!, warehouseId ?? undefined),
    enabled: Boolean(orderId) && Boolean(warehouseId),
    staleTime: 10_000,
    retry: false,
  });
}

/** POST .../ship — consumes the allocated supplier_stock batches and moves the order to
 *  shipped. Invalidates the cabinet orders list and the supplier stock (batches were drawn
 *  down). result.warnings is surfaced by the caller as non-error toasts. */
export function useShipOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ShipOrderRequest }) =>
      supplierCabinetApi.shipOrder(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.orders });
      queryClient.invalidateQueries({ queryKey: SUPPLIER_INVENTORY_KEYS.stockRoot });
    },
  });
}

/** POST .../delay-reason — тільки для замовлень зі статусом "shipped" (TASK-585). */
export function useSetOrderDelayReason() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: SetOrderDelayReasonRequest }) =>
      supplierCabinetApi.setOrderDelayReason(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.orders });
    },
  });
}

/** POST .../expected-delivery-date — supplier moves a shipped order's expected delivery date
 *  (Phase 4, plan D5). Repeatable while status === "shipped". Invalidates the cabinet orders
 *  list so the new date shows in both the row ETA and the expanded detail. */
export function useSetExpectedDeliveryDate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, expectedDeliveryDate }: { id: string; expectedDeliveryDate: string }) =>
      supplierCabinetApi.setOrderExpectedDeliveryDate(id, expectedDeliveryDate),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.orders });
    },
  });
}

// ─── Support tickets ──────────────────────────────────────────────────────────

export function useCabinetSupportTickets() {
  return useQuery({
    queryKey: CABINET_COOP_KEYS.tickets,
    queryFn: supplierCabinetApi.getSupportTickets,
    staleTime: 15_000,
    retry: false,
  });
}

/** Один тікет з messages; polling поки тред відкритий (як чат). */
export function useCabinetSupportTicket(ticketId: string | null) {
  return useQuery({
    queryKey: CABINET_COOP_KEYS.ticket(ticketId ?? ""),
    queryFn: () => supplierCabinetApi.getSupportTicket(ticketId!),
    enabled: Boolean(ticketId),
    refetchInterval: 5000,
  });
}

export function useAddCabinetTicketMessage(ticketId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => supplierCabinetApi.addSupportTicketMessage(ticketId!, body),
    onSuccess: () => {
      if (ticketId) {
        queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.ticket(ticketId) });
      }
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.tickets });
    },
  });
}

export function useUpdateCabinetTicketStatus(ticketId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (status: SupportTicketStatus) =>
      supplierCabinetApi.updateSupportTicketStatus(ticketId!, status),
    onSuccess: () => {
      if (ticketId) {
        queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.ticket(ticketId) });
      }
      queryClient.invalidateQueries({ queryKey: CABINET_COOP_KEYS.tickets });
    },
  });
}
