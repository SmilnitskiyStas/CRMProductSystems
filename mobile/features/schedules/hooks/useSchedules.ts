import { useQuery } from '@tanstack/react-query';
import { getMyShifts, getSchedules, getSchedule } from '../api';

export function useMyShifts(from: string, to: string, enabled = true) {
  return useQuery({
    queryKey: ['my-shifts', from, to],
    queryFn: () => getMyShifts(from, to),
    enabled: enabled && !!from && !!to,
  });
}

export function useSchedules(locationId?: string, weekStart?: string, enabled = true) {
  return useQuery({
    queryKey: ['schedules', locationId, weekStart],
    queryFn: () => getSchedules(locationId, weekStart),
    enabled,
  });
}

export function useSchedule(id: string) {
  return useQuery({
    queryKey: ['schedule', id],
    queryFn: () => getSchedule(id),
    enabled: !!id,
  });
}
