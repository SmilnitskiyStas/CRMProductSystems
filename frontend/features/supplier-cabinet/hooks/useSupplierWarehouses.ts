"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import type {
  CreateSupplierWarehouseRequest,
  UpdateSupplierWarehouseRequest,
} from "../types";

// Shared key — Phase 5 (supplier schedules) reuses useSupplierWarehouses() for the
// location options, so keep this hook self-contained and stable.
export const SUPPLIER_WAREHOUSE_KEYS = {
  all: ["supplier", "warehouses"] as const,
};

export function useSupplierWarehouses() {
  return useQuery({
    queryKey: SUPPLIER_WAREHOUSE_KEYS.all,
    queryFn: supplierCabinetApi.getWarehouses,
    staleTime: 30_000,
    retry: false,
  });
}

export function useCreateWarehouse() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSupplierWarehouseRequest) =>
      supplierCabinetApi.createWarehouse(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SUPPLIER_WAREHOUSE_KEYS.all });
    },
  });
}

export function useUpdateWarehouse() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSupplierWarehouseRequest }) =>
      supplierCabinetApi.updateWarehouse(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SUPPLIER_WAREHOUSE_KEYS.all });
    },
  });
}

export function useDeactivateWarehouse() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.deactivateWarehouse(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SUPPLIER_WAREHOUSE_KEYS.all });
    },
  });
}
