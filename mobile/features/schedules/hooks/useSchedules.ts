import { useQuery } from '@tanstack/react-query';
import { getMyShifts, getSchedules, getSchedule } from '../api';

export function useMyShifts(from: string, to: string) {
  return useQuery({
    queryKey: ['my-shifts', from, to],
    queryFn: () => getMyShifts(from, to),
    enabled: !!from && !!to,
  });
}

export function useSchedules(locationId?: string, weekStart?: string) {
  return useQuery({
    queryKey: ['schedules', locationId, weekStart],
    queryFn: () => getSchedules(locationId, weekStart),
  });
}

export function useSchedule(id: string) {
  return useQuery({
    queryKey: ['schedule', id],
    queryFn: () => getSchedule(id),
    enabled: !!id,
  });
}
