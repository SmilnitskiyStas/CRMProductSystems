import { apiClient } from '@/lib/api-client';
import type { ScheduleShift, WorkSchedule, WorkScheduleDetail } from './types';

export async function getMyShifts(from: string, to: string): Promise<ScheduleShift[]> {
  const { data } = await apiClient.get<ScheduleShift[]>('/schedules/my-shifts', {
    params: { from, to },
  });
  return data;
}

export async function getSchedules(locationId?: string, weekStart?: string): Promise<WorkSchedule[]> {
  const { data } = await apiClient.get<WorkSchedule[]>('/schedules', {
    params: {
      locationId: locationId || undefined,
      weekStart: weekStart || undefined,
    },
  });
  return data;
}

export async function getSchedule(id: string): Promise<WorkScheduleDetail> {
  const { data } = await apiClient.get<WorkScheduleDetail>(`/schedules/${id}`);
  return data;
}
