"use client";

// Client-side cooperation hooks (TASK-318): agreements, marketplace orders,
// support tickets. Server state — React Query only (frontend-structure.md).

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { marketplaceApi } from "../api/marketplace-api";
import type {
  CreateCooperationRequestBody,
  ChooseSigningMethodRequest,
  CreateMarketplaceOrderRequest,
  CreateSupportTicketRequest,
} from "../types";

// ─── Query keys ───────────────────────────────────────────────────────────────

export const COOPERATION_KEYS = {
  myAgreements: ["marketplace", "cooperation"] as const,
  myOrders: ["marketplace", "my-orders"] as const,
  myTickets: ["marketplace", "my-support-tickets"] as const,
  ticket: (id: string) => ["marketplace", "support-ticket", id] as const,
  orderReceipt: (orderId: string) => ["marketplace", "order-receipt", orderId] as const,
};

// ─── Agreements ───────────────────────────────────────────────────────────────

/** Мої угоди про співпрацю (новіші перші). `enabled=false` для provider/supplier viewer. */
export function useMyCooperation(enabled = true) {
  return useQuery({
    queryKey: COOPERATION_KEYS.myAgreements,
    queryFn: marketplaceApi.getMyCooperation,
    enabled,
    staleTime: 15_000,
    retry: false,
  });
}

export function useCreateCooperationRequest(supplierId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCooperationRequestBody) =>
      marketplaceApi.createCooperationRequest(supplierId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.myAgreements });
    },
  });
}

/** Клієнт обирає спосіб підписання (physical/vchasno) для угоди awaiting_signature (TASK-319/320). */
export function useChooseSigningMethod(agreementId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ChooseSigningMethodRequest) =>
      marketplaceApi.chooseSigningMethod(agreementId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.myAgreements });
    },
  });
}

// ─── Orders ───────────────────────────────────────────────────────────────────

export function useMyMarketplaceOrders(enabled = true) {
  return useQuery({
    queryKey: COOPERATION_KEYS.myOrders,
    queryFn: marketplaceApi.getMyOrders,
    enabled,
    staleTime: 15_000,
    retry: false,
  });
}

export function useCreateMarketplaceOrder(supplierId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateMarketplaceOrderRequest) =>
      marketplaceApi.createOrder(supplierId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.myOrders });
    },
  });
}

/** Pre-flight barcode-conflict check (TASK-597) — call at checkout-confirm time, right
 * before createOrder, with the cart's current items. Not a useQuery: it's triggered
 * on-demand, not on mount, and its result is only relevant for the confirm click that
 * requested it. */
export function useCheckOrderConflicts(supplierId: string) {
  return useMutation({
    mutationFn: (items: { supplierItemId: string; qty: number }[]) =>
      marketplaceApi.checkOrderConflicts(supplierId, items),
  });
}

export function useCancelMarketplaceOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ orderId, reason }: { orderId: string; reason: string }) =>
      marketplaceApi.cancelOrder(orderId, reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.myOrders });
    },
  });
}

/** Read-only "what was actually received" (TASK-586). Only meaningful once
 * `order.status === "delivered"` — pass `enabled` accordingly. A 404 means no
 * receiving session exists yet; the caller treats that as "nothing to show". */
export function useMarketplaceOrderReceipt(orderId: string, enabled: boolean) {
  return useQuery({
    queryKey: COOPERATION_KEYS.orderReceipt(orderId),
    queryFn: () => marketplaceApi.getOrderReceipt(orderId),
    enabled: enabled && Boolean(orderId),
    staleTime: 15_000,
    retry: false,
  });
}

// ─── Support tickets ──────────────────────────────────────────────────────────

export function useMySupportTickets(enabled = true) {
  return useQuery({
    queryKey: COOPERATION_KEYS.myTickets,
    queryFn: marketplaceApi.getMySupportTickets,
    enabled,
    staleTime: 15_000,
    retry: false,
  });
}

/** Один тікет з messages; polling поки тред відкритий (як чат). */
export function useSupportTicket(ticketId: string | null) {
  return useQuery({
    queryKey: COOPERATION_KEYS.ticket(ticketId ?? ""),
    queryFn: () => marketplaceApi.getSupportTicket(ticketId!),
    enabled: Boolean(ticketId),
    refetchInterval: 5000,
  });
}

export function useCreateSupportTicket(supplierId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSupportTicketRequest) =>
      marketplaceApi.createSupportTicket(supplierId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.myTickets });
    },
  });
}

export function useSendSupportTicketMessage(ticketId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: string) =>
      marketplaceApi.sendSupportTicketMessage(ticketId!, body),
    onSuccess: () => {
      if (ticketId) {
        queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.ticket(ticketId) });
      }
      queryClient.invalidateQueries({ queryKey: COOPERATION_KEYS.myTickets });
    },
  });
}
