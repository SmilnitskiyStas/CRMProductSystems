import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as api from "../api/providerScheduleApi";
import type { CreateProviderScheduleSlotRequest } from "../api/providerScheduleApi";

const SCHEDULE_KEY = ["provider", "schedule"];

export function useProviderSchedule(userId?: string) {
  return useQuery({
    queryKey: [...SCHEDULE_KEY, userId ?? "all"],
    queryFn: () => api.getScheduleSlots(userId),
  });
}

export function useCreateScheduleSlot() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateProviderScheduleSlotRequest) => api.createScheduleSlot(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: SCHEDULE_KEY }),
  });
}

export function useDeleteScheduleSlot() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.deleteScheduleSlot(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: SCHEDULE_KEY }),
  });
}
