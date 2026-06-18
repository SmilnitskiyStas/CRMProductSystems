import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { schedulesApi } from "../api/schedules";
import type {
  CreateSchedulePayload,
  UpdateSchedulePayload,
  AddShiftPayload,
  UpdateShiftPayload,
} from "../types";

const SCHEDULES_KEY = ["schedules"] as const;

export function useSchedules(locationId?: string, weekStart?: string) {
  return useQuery({
    queryKey: [...SCHEDULES_KEY, locationId, weekStart],
    queryFn: () => schedulesApi.getAll(locationId, weekStart),
    placeholderData: (prev) => prev,
  });
}

export function useSchedule(id: string | null) {
  return useQuery({
    queryKey: [...SCHEDULES_KEY, id],
    queryFn: () => schedulesApi.getById(id!),
    enabled: !!id,
  });
}

export function useMyShifts(from: string, to: string) {
  return useQuery({
    queryKey: ["my-shifts", from, to],
    queryFn: () => schedulesApi.getMyShifts(from, to),
    enabled: !!from && !!to,
  });
}

// ── Schedule mutations ────────────────────────────────────────────────────────

export function useCreateSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateSchedulePayload) => schedulesApi.create(data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY }),
  });
}

export function useUpdateSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateSchedulePayload }) =>
      schedulesApi.update(id, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY }),
  });
}

export function useDeleteSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => schedulesApi.delete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SCHEDULES_KEY }),
  });
}

// ── Shift mutations ───────────────────────────────────────────────────────────

export function useAddShift(scheduleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: AddShiftPayload) => schedulesApi.addShift(scheduleId, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...SCHEDULES_KEY, scheduleId] }),
  });
}

export function useUpdateShift(scheduleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ shiftId, data }: { shiftId: string; data: UpdateShiftPayload }) =>
      schedulesApi.updateShift(scheduleId, shiftId, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...SCHEDULES_KEY, scheduleId] }),
  });
}

export function useDeleteShift(scheduleId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (shiftId: string) => schedulesApi.deleteShift(scheduleId, shiftId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...SCHEDULES_KEY, scheduleId] }),
  });
}
