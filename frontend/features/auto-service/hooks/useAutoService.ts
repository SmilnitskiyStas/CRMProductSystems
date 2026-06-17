"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { autoServiceApi } from "../api/auto-service-api";
import type {
  WorkOrderStatus,
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

// ─── Query Keys ───────────────────────────────────────────────────────────────

export const AUTO_SERVICE_KEYS = {
  workOrders: (params?: { status?: WorkOrderStatus; vehicleId?: string }) =>
    ["auto-service", "work-orders", params] as const,
  workOrder: (id: string) => ["auto-service", "work-order", id] as const,
  customers: ["auto-service", "customers"] as const,
  vehicles: (customerId?: string) =>
    ["auto-service", "vehicles", customerId] as const,
  serviceCatalog: ["auto-service", "service-catalog"] as const,
};

// ─── Work Orders ─────────────────────────────────────────────────────────────

export function useWorkOrders(params?: {
  status?: WorkOrderStatus;
  vehicleId?: string;
}) {
  return useQuery({
    queryKey: AUTO_SERVICE_KEYS.workOrders(params),
    queryFn: () => autoServiceApi.getWorkOrders(params),
    staleTime: 30_000,
  });
}

export function useWorkOrder(id: string) {
  return useQuery({
    queryKey: AUTO_SERVICE_KEYS.workOrder(id),
    queryFn: () => autoServiceApi.getWorkOrder(id),
    enabled: !!id,
    staleTime: 30_000,
  });
}

export function useCreateWorkOrder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateWorkOrderRequest) =>
      autoServiceApi.createWorkOrder(body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["auto-service", "work-orders"],
      });
    },
  });
}

export function useUpdateWorkOrder(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateWorkOrderRequest) =>
      autoServiceApi.updateWorkOrder(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.workOrder(id),
      });
      queryClient.invalidateQueries({
        queryKey: ["auto-service", "work-orders"],
      });
    },
  });
}

export function useCompleteWorkOrder(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => autoServiceApi.completeWorkOrder(id),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.workOrder(id),
      });
      queryClient.invalidateQueries({
        queryKey: ["auto-service", "work-orders"],
      });
    },
  });
}

export function useAddWorkOrderLine(workOrderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AddWorkOrderLineRequest) =>
      autoServiceApi.addLine(workOrderId, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.workOrder(workOrderId),
      });
      queryClient.invalidateQueries({
        queryKey: ["auto-service", "work-orders"],
      });
    },
  });
}

export function useRemoveWorkOrderLine(workOrderId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (lineId: string) =>
      autoServiceApi.removeLine(workOrderId, lineId),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.workOrder(workOrderId),
      });
      queryClient.invalidateQueries({
        queryKey: ["auto-service", "work-orders"],
      });
    },
  });
}

// ─── Customers ───────────────────────────────────────────────────────────────

export function useCustomers() {
  return useQuery({
    queryKey: AUTO_SERVICE_KEYS.customers,
    queryFn: autoServiceApi.getCustomers,
    staleTime: 30_000,
  });
}

export function useCreateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCustomerRequest) =>
      autoServiceApi.createCustomer(body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.customers,
      });
    },
  });
}

export function useUpdateCustomer(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateCustomerRequest) =>
      autoServiceApi.updateCustomer(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.customers,
      });
    },
  });
}

export function useDeleteCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => autoServiceApi.deleteCustomer(id),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.customers,
      });
    },
  });
}

// ─── Vehicles ────────────────────────────────────────────────────────────────

export function useVehicles(customerId?: string) {
  return useQuery({
    queryKey: AUTO_SERVICE_KEYS.vehicles(customerId),
    queryFn: () => autoServiceApi.getVehicles(customerId),
    staleTime: 30_000,
  });
}

export function useCreateVehicle() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateVehicleRequest) =>
      autoServiceApi.createVehicle(body),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.vehicles(variables.customerId),
      });
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.customers,
      });
    },
  });
}

export function useUpdateVehicle(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateVehicleRequest) =>
      autoServiceApi.updateVehicle(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: ["auto-service", "vehicles"],
      });
    },
  });
}

// ─── Service Catalog ─────────────────────────────────────────────────────────

export function useServiceCatalog() {
  return useQuery({
    queryKey: AUTO_SERVICE_KEYS.serviceCatalog,
    queryFn: autoServiceApi.getServiceCatalog,
    staleTime: 60_000,
  });
}

export function useCreateServiceItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateServiceCatalogItemRequest) =>
      autoServiceApi.createServiceItem(body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.serviceCatalog,
      });
    },
  });
}

export function useUpdateServiceItem(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: UpdateServiceCatalogItemRequest) =>
      autoServiceApi.updateServiceItem(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.serviceCatalog,
      });
    },
  });
}

export function useDeleteServiceItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => autoServiceApi.deleteServiceItem(id),
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: AUTO_SERVICE_KEYS.serviceCatalog,
      });
    },
  });
}
