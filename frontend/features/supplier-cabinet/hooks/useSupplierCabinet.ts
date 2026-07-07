"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import type {
  CabinetProfileUpdateRequest,
  CabinetAddItemRequest,
  CabinetUpdateItemRequest,
  CabinetInviteStaffRequest,
  CreateSupplierRoleRequest,
  UpdateSupplierRoleRequest,
  CreateSupplierTaskRequest,
  UpdateSupplierTaskRequest,
  UpdateSupplierTaskStatusRequest,
  SupplierTaskFilters,
  SendSupplierChatMessageRequest,
} from "../types";

// ─── Query keys ───────────────────────────────────────────────────────────────

export const CABINET_KEYS = {
  profile: ["supplier-cabinet", "profile"] as const,
  items: ["supplier-cabinet", "items"] as const,
  reviews: (page: number) => ["supplier-cabinet", "reviews", page] as const,
  reviewStats: ["supplier-cabinet", "reviews", "stats"] as const,
  metrics: ["supplier-cabinet", "metrics"] as const,
  staff: ["supplier-cabinet", "staff"] as const,
  roles: ["supplier-cabinet", "roles"] as const,
  tasks: (filters?: SupplierTaskFilters) =>
    ["supplier-cabinet", "tasks", filters ?? {}] as const,
  clients: ["supplier-cabinet", "clients"] as const,
  chatSessions: ["supplier-cabinet", "chat", "sessions"] as const,
  chatMessages: (clientTenantId: string) =>
    ["supplier-cabinet", "chat", "messages", clientTenantId] as const,
};

// ─── Profile ──────────────────────────────────────────────────────────────────

export function useCabinetProfile() {
  return useQuery({
    queryKey: CABINET_KEYS.profile,
    queryFn: supplierCabinetApi.getProfile,
    staleTime: 30_000,
    retry: false,
  });
}

export function useUpdateCabinetProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CabinetProfileUpdateRequest) =>
      supplierCabinetApi.updateProfile(body),
    onSuccess: (profile) => {
      queryClient.setQueryData(CABINET_KEYS.profile, profile);
    },
  });
}

export function useTogglePublish() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: supplierCabinetApi.togglePublish,
    onSuccess: (profile) => {
      queryClient.setQueryData(CABINET_KEYS.profile, profile);
    },
  });
}

// ─── Items ────────────────────────────────────────────────────────────────────

export function useCabinetItems() {
  return useQuery({
    queryKey: CABINET_KEYS.items,
    queryFn: supplierCabinetApi.getItems,
    staleTime: 30_000,
    retry: false,
  });
}

export function useAddCabinetItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CabinetAddItemRequest) => supplierCabinetApi.addItem(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.items });
    },
  });
}

export function useUpdateCabinetItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CabinetUpdateItemRequest }) =>
      supplierCabinetApi.updateItem(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.items });
    },
  });
}

export function useDeleteCabinetItem() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.deleteItem(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.items });
    },
  });
}

// ─── Reviews / metrics ────────────────────────────────────────────────────────

export function useCabinetReviews(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: CABINET_KEYS.reviews(page),
    queryFn: () => supplierCabinetApi.getReviews(page, pageSize),
    staleTime: 30_000,
    retry: false,
  });
}

export function useReplyToReview() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, replyText }: { id: string; replyText: string }) =>
      supplierCabinetApi.replyToReview(id, replyText),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["supplier-cabinet", "reviews"] });
    },
  });
}

export function useCabinetReviewStats() {
  return useQuery({
    queryKey: CABINET_KEYS.reviewStats,
    queryFn: supplierCabinetApi.getReviewStats,
    staleTime: 30_000,
    retry: false,
  });
}

export function useCabinetMetrics() {
  return useQuery({
    queryKey: CABINET_KEYS.metrics,
    queryFn: supplierCabinetApi.getMetrics,
    staleTime: 30_000,
    retry: false,
  });
}

// ─── Staff ────────────────────────────────────────────────────────────────────

export function useCabinetStaff() {
  return useQuery({
    queryKey: CABINET_KEYS.staff,
    queryFn: supplierCabinetApi.getStaff,
    staleTime: 30_000,
    retry: false,
  });
}

export function useInviteCabinetStaff() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CabinetInviteStaffRequest) => supplierCabinetApi.inviteStaff(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.staff });
    },
  });
}

export function useDeactivateCabinetStaff() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.deactivateStaff(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.staff });
    },
  });
}

// ─── Roles ────────────────────────────────────────────────────────────────────

export function useSupplierRoles() {
  return useQuery({
    queryKey: CABINET_KEYS.roles,
    queryFn: supplierCabinetApi.getRoles,
    staleTime: 30_000,
    retry: false,
  });
}

export function useCreateSupplierRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSupplierRoleRequest) => supplierCabinetApi.createRole(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.roles });
    },
  });
}

export function useUpdateSupplierRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSupplierRoleRequest }) =>
      supplierCabinetApi.updateRole(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.roles });
    },
  });
}

export function useDeleteSupplierRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.deleteRole(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.roles });
    },
  });
}

// ─── Tasks ────────────────────────────────────────────────────────────────────

export function useSupplierTasks(filters?: SupplierTaskFilters) {
  return useQuery({
    queryKey: CABINET_KEYS.tasks(filters),
    queryFn: () => supplierCabinetApi.getTasks(filters),
    staleTime: 15_000,
    retry: false,
  });
}

export function useCreateSupplierTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateSupplierTaskRequest) => supplierCabinetApi.createTask(body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["supplier-cabinet", "tasks"] });
    },
  });
}

export function useUpdateSupplierTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSupplierTaskRequest }) =>
      supplierCabinetApi.updateTask(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["supplier-cabinet", "tasks"] });
    },
  });
}

export function useUpdateSupplierTaskStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSupplierTaskStatusRequest }) =>
      supplierCabinetApi.updateTaskStatus(id, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["supplier-cabinet", "tasks"] });
    },
  });
}

// ─── Clients (TASK-314) ─────────────────────────────────────────────────────────

export function useSupplierClients() {
  return useQuery({
    queryKey: CABINET_KEYS.clients,
    queryFn: supplierCabinetApi.getClients,
    staleTime: 30_000,
    retry: false,
  });
}

// ─── Supplier ↔ client chat (TASK-314) ──────────────────────────────────────────

export function useSupplierChatSessions(enabled = true) {
  return useQuery({
    queryKey: CABINET_KEYS.chatSessions,
    queryFn: supplierCabinetApi.getChatSessions,
    enabled,
    refetchInterval: 3000,
  });
}

export function useSupplierChatMessages(clientTenantId: string | null) {
  return useQuery({
    queryKey: CABINET_KEYS.chatMessages(clientTenantId ?? ""),
    queryFn: () => supplierCabinetApi.getChatMessages(clientTenantId!),
    enabled: Boolean(clientTenantId),
    refetchInterval: 3000,
  });
}

export function useSendSupplierChatMessage(clientTenantId: string | null) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SendSupplierChatMessageRequest) =>
      supplierCabinetApi.sendChatMessage(clientTenantId!, body),
    onSuccess: () => {
      if (clientTenantId) {
        queryClient.invalidateQueries({ queryKey: CABINET_KEYS.chatMessages(clientTenantId) });
      }
      queryClient.invalidateQueries({ queryKey: CABINET_KEYS.chatSessions });
    },
  });
}
