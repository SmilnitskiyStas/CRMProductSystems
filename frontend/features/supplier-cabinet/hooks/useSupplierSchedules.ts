"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import type {
  CreateSchedulePayload,
  UpdateSchedulePayload,
  AddShiftPayload,
  UpdateShiftPayload,
} from "@/features/schedules/types";

// Supplier-portal expansion Phase 5 (plan 1-partitioned-book.md D6). Mirrors
// features/schedules/hooks/useSchedules.ts against the supplier cabinet endpoints;
// keys live under the ["supplier", …] namespace so they never collide with the
// retail /schedules cache.
const SUPPLIER_SCHEDULES_KEY = ["supplier", "schedules"] as const;

export function useSupplierSchedules(locationId?: string, weekStart?: string) {
  return useQuery({
    queryKey: [...SUPPLIER_SCHEDULES_KEY, locationId ?? null, weekStart ?? null],
    queryFn: () => supplierCabinetApi.schedules.list(locationId, weekStart),
    placeholderData: (prev) => prev,
    retry: false,
  });
}

export function useSupplierSchedule(id: string | null) {
  return useQuery({
    queryKey: [...SUPPLIER_SCHEDULES_KEY, id],
    queryFn: () => supplierCabinetApi.schedules.getById(id!),
    enabled: !!id,
    retry: false,
  });
}

export function useSupplierMyShifts(from: string, to: string) {
  return useQuery({
    queryKey: ["supplier", "my-shifts", from, to],
    queryFn: () => supplierCabinetApi.schedules.myShifts(from, to),
    enabled: !!from && !!to,
    retry: false,
  });
}

export function useSupplierScheduleStaff() {
  return useQuery({
    queryKey: ["supplier", "schedules", "staff"],
    queryFn: () => supplierCabinetApi.schedules.staff(),
    staleTime: 60_000,
    retry: false,
  });
}

// ── Schedule mutations ────────────────────────────────────────────────────────

export function useCreateSupplierSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateSchedulePayload) => supplierCabinetApi.schedules.create(data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SUPPLIER_SCHEDULES_KEY }),
  });
}

export function useUpdateSupplierSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateSchedulePayload }) =>
      supplierCabinetApi.schedules.update(id, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SUPPLIER_SCHEDULES_KEY }),
  });
}

export function useDeleteSupplierSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => supplierCabinetApi.schedules.remove(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SUPPLIER_SCHEDULES_KEY }),
  });
}

// ── Shift mutations ───────────────────────────────────────────────────────────

export function useAddSupplierShift(scheduleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: AddShiftPayload) => supplierCabinetApi.schedules.addShift(scheduleId, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SUPPLIER_SCHEDULES_KEY }),
  });
}

export function useUpdateSupplierShift(scheduleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ shiftId, data }: { shiftId: string; data: UpdateShiftPayload }) =>
      supplierCabinetApi.schedules.updateShift(scheduleId, shiftId, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SUPPLIER_SCHEDULES_KEY }),
  });
}

export function useDeleteSupplierShift(scheduleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (shiftId: string) => supplierCabinetApi.schedules.deleteShift(scheduleId, shiftId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SUPPLIER_SCHEDULES_KEY }),
  });
}
