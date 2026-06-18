import { api } from "@/lib/api";
import type {
  WorkScheduleDto,
  WorkScheduleDetailDto,
  ScheduleShiftDto,
  CreateSchedulePayload,
  UpdateSchedulePayload,
  AddShiftPayload,
  UpdateShiftPayload,
} from "../types";

export const schedulesApi = {
  getAll(locationId?: string, weekStart?: string): Promise<WorkScheduleDto[]> {
    const params = new URLSearchParams();
    if (locationId) params.set("locationId", locationId);
    if (weekStart) params.set("weekStart", weekStart);
    const qs = params.toString();
    return api.get<WorkScheduleDto[]>(`/api/schedules${qs ? `?${qs}` : ""}`);
  },

  getById(id: string): Promise<WorkScheduleDetailDto> {
    return api.get<WorkScheduleDetailDto>(`/api/schedules/${id}`);
  },

  create(data: CreateSchedulePayload): Promise<WorkScheduleDto> {
    return api.post<WorkScheduleDto>("/api/schedules", data);
  },

  update(id: string, data: UpdateSchedulePayload): Promise<WorkScheduleDto> {
    return api.put<WorkScheduleDto>(`/api/schedules/${id}`, data);
  },

  delete(id: string): Promise<void> {
    return api.delete<void>(`/api/schedules/${id}`);
  },

  addShift(scheduleId: string, data: AddShiftPayload): Promise<ScheduleShiftDto> {
    return api.post<ScheduleShiftDto>(`/api/schedules/${scheduleId}/shifts`, data);
  },

  updateShift(scheduleId: string, shiftId: string, data: UpdateShiftPayload): Promise<ScheduleShiftDto> {
    return api.put<ScheduleShiftDto>(`/api/schedules/${scheduleId}/shifts/${shiftId}`, data);
  },

  deleteShift(scheduleId: string, shiftId: string): Promise<void> {
    return api.delete<void>(`/api/schedules/${scheduleId}/shifts/${shiftId}`);
  },

  getMyShifts(from: string, to: string): Promise<ScheduleShiftDto[]> {
    const params = new URLSearchParams({ from, to });
    return api.get<ScheduleShiftDto[]>(`/api/schedules/my-shifts?${params.toString()}`);
  },
};
