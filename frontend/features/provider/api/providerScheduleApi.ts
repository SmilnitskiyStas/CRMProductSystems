import { api } from "@/lib/api";

export interface ProviderScheduleSlotDto {
  id: string;
  userId: string;
  userFullName: string;
  userRole: string;
  dayOfWeek: number; // 0=Mon..6=Sun
  startTime: string; // "HH:mm"
  endTime: string;   // "HH:mm"
  notes: string | null;
  isActive: boolean;
}

export interface CreateProviderScheduleSlotRequest {
  userId: string;
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  notes?: string;
}

export function getScheduleSlots(userId?: string): Promise<ProviderScheduleSlotDto[]> {
  const qs = userId ? `?userId=${userId}` : "";
  return api.get<ProviderScheduleSlotDto[]>(`/api/provider/schedules${qs}`);
}

export function createScheduleSlot(req: CreateProviderScheduleSlotRequest): Promise<ProviderScheduleSlotDto> {
  return api.post<ProviderScheduleSlotDto>("/api/provider/schedules", req);
}

export function deleteScheduleSlot(id: string): Promise<void> {
  return api.delete<void>(`/api/provider/schedules/${id}`);
}
