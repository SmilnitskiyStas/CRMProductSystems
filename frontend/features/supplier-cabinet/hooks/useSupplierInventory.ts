"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import type {
  AddSupplierBatchRequest,
  AdjustSupplierStockRequest,
  AddSupplierReceiptLineRequest,
  CreateSupplierReceiptRequest,
  SupplierStockReceiptStatus,
  UpdateSupplierReceiptRequest,
} from "../types";

// ─── Query keys ───────────────────────────────────────────────────────────────
// Same ["supplier", …] namespace as useSupplierWarehouses; "stock" / "receipts"
// prefixes so a single invalidate covers every warehouse / page / filter.

export const SUPPLIER_INVENTORY_KEYS = {
  stockRoot: ["supplier", "stock"] as const,
  stock: (
    warehouseId: string | null,
    supplierItemId: string | undefined,
    page: number,
  ) => ["supplier", "stock", warehouseId, supplierItemId ?? null, page] as const,
  receiptsRoot: ["supplier", "receipts"] as const,
  receipts: (warehouseId: string | null, status?: SupplierStockReceiptStatus) =>
    ["supplier", "receipts", "list", warehouseId, status ?? null] as const,
  receipt: (id: string | null) => ["supplier", "receipts", "detail", id] as const,
};

// ─── Stock ────────────────────────────────────────────────────────────────────

export function useWarehouseStock(
  warehouseId: string | null,
  params: { supplierItemId?: string; page?: number; pageSize?: number } = {},
) {
  const page = params.page ?? 1;
  return useQuery({
    queryKey: SUPPLIER_INVENTORY_KEYS.stock(warehouseId, params.supplierItemId, page),
    queryFn: () =>
      supplierCabinetApi.getWarehouseStock(warehouseId!, { ...params, page }),
    enabled: Boolean(warehouseId),
    staleTime: 15_000,
    retry: false,
  });
}

export function useAddStockBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      warehouseId,
      body,
    }: {
      warehouseId: string;
      body: AddSupplierBatchRequest;
    }) => supplierCabinetApi.addStockBatch(warehouseId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SUPPLIER_INVENTORY_KEYS.stockRoot });
    },
  });
}

export function useAdjustStockBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      batchId,
      body,
    }: {
      batchId: string;
      body: AdjustSupplierStockRequest;
    }) => supplierCabinetApi.adjustStockBatch(batchId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SUPPLIER_INVENTORY_KEYS.stockRoot });
    },
  });
}

// ─── Receipts ─────────────────────────────────────────────────────────────────

export function useSupplierReceipts(
  warehouseId: string | null,
  params: { status?: SupplierStockReceiptStatus } = {},
) {
  return useQuery({
    queryKey: SUPPLIER_INVENTORY_KEYS.receipts(warehouseId, params.status),
    queryFn: () => supplierCabinetApi.listReceipts(warehouseId!, params),
    enabled: Boolean(warehouseId),
    staleTime: 15_000,
    retry: false,
  });
}

export function useSupplierReceipt(id: string | null) {
  return useQuery({
    queryKey: SUPPLIER_INVENTORY_KEYS.receipt(id),
    queryFn: () => supplierCabinetApi.getReceipt(id!),
    enabled: Boolean(id),
    staleTime: 5_000,
    retry: false,
  });
}

function invalidateReceipts(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: SUPPLIER_INVENTORY_KEYS.receiptsRoot });
}

export function useCreateReceipt() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      warehouseId,
      body,
    }: {
      warehouseId: string;
      body: CreateSupplierReceiptRequest;
    }) => supplierCabinetApi.createReceipt(warehouseId, body),
    onSuccess: () => invalidateReceipts(queryClient),
  });
}

export function useUpdateReceipt() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      body,
    }: {
      id: string;
      body: UpdateSupplierReceiptRequest;
    }) => supplierCabinetApi.updateReceipt(id, body),
    onSuccess: () => invalidateReceipts(queryClient),
  });
}

export function useAddReceiptLine() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      body,
    }: {
      id: string;
      body: AddSupplierReceiptLineRequest;
    }) => supplierCabinetApi.addReceiptLine(id, body),
    onSuccess: () => invalidateReceipts(queryClient),
  });
}

export function useRemoveReceiptLine() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, lineId }: { id: string; lineId: string }) =>
      supplierCabinetApi.removeReceiptLine(id, lineId),
    onSuccess: () => invalidateReceipts(queryClient),
  });
}

export function useFinalizeReceipt() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.finalizeReceipt(id),
    onSuccess: () => {
      invalidateReceipts(queryClient);
      // finalize writes one SupplierStock batch per line.
      queryClient.invalidateQueries({ queryKey: SUPPLIER_INVENTORY_KEYS.stockRoot });
    },
  });
}
