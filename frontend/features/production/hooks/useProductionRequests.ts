"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { productionApi } from "../api/production-api";
import type {
  ProductionRequestFilters,
  ProductionRequestCreateDto,
  ProductionRequestUpdateDto,
  ProductionRequestReviewDto,
  OrdersFromRequestsDto,
} from "../types";

export const REQUEST_KEYS = {
  all: ["production", "requests"] as const,
  list: (f: ProductionRequestFilters) => ["production", "requests", "list", f] as const,
  detail: (id: string) => ["production", "requests", "detail", id] as const,
  summary: (loc?: string, status?: string) =>
    ["production", "requests", "summary", loc, status] as const,
};

export function useProductionRequests(filters: ProductionRequestFilters) {
  return useQuery({
    queryKey: REQUEST_KEYS.list(filters),
    queryFn: () => productionApi.getRequests(filters),
    staleTime: 15_000,
  });
}

export function useProductionRequest(id: string | null) {
  return useQuery({
    queryKey: REQUEST_KEYS.detail(id ?? ""),
    queryFn: () => productionApi.getRequest(id as string),
    enabled: !!id,
  });
}

/** Production module only — pass enabled=false for tenants without it (endpoint is module-gated). */
export function useProductionRequestSummary(
  enabled: boolean,
  productionLocationId?: string,
  status?: string
) {
  return useQuery({
    queryKey: REQUEST_KEYS.summary(productionLocationId, status),
    queryFn: () => productionApi.getRequestSummary(productionLocationId, status),
    enabled,
    staleTime: 15_000,
  });
}

function useInvalidateRequests() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: REQUEST_KEYS.all });
}

export function useCreateProductionRequest() {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: (body: ProductionRequestCreateDto) => productionApi.createRequest(body),
    onSuccess: invalidate,
  });
}

export function useUpdateProductionRequest(id: string) {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: (body: ProductionRequestUpdateDto) => productionApi.updateRequest(id, body),
    onSuccess: invalidate,
  });
}

export function useSubmitProductionRequest() {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: (id: string) => productionApi.submitRequest(id),
    onSuccess: invalidate,
  });
}

export function useCancelProductionRequest() {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: (id: string) => productionApi.cancelRequest(id),
    onSuccess: invalidate,
  });
}

export function useReviewProductionRequest() {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ProductionRequestReviewDto }) =>
      productionApi.reviewRequest(id, body),
    onSuccess: invalidate,
  });
}

export function useRejectProductionRequest() {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      productionApi.rejectRequest(id, reason),
    onSuccess: invalidate,
  });
}

export function useShipProductionRequest() {
  const invalidate = useInvalidateRequests();
  return useMutation({
    mutationFn: (id: string) => productionApi.shipRequest(id),
    onSuccess: invalidate,
  });
}

export function useCreateOrdersFromRequests() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: OrdersFromRequestsDto) => productionApi.createOrdersFromRequests(body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: REQUEST_KEYS.all });
      qc.invalidateQueries({ queryKey: ["production", "orders"] });
    },
  });
}
