import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getWriteOffs,
  getWriteOff,
  createWriteOff,
  approveWriteOff,
  rejectWriteOff,
} from '../api/writeOffApi';
import type { CreateWriteOffPayload } from '../types';

export function useWriteOffs(storeId?: string) {
  return useQuery({
    queryKey: ['write-offs', storeId ?? 'all'],
    queryFn: () => getWriteOffs(storeId),
  });
}

export function useWriteOff(id: string) {
  return useQuery({
    queryKey: ['write-off', id],
    queryFn: () => getWriteOff(id),
    enabled: !!id,
  });
}

export function useCreateWriteOff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateWriteOffPayload) => createWriteOff(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['write-offs'] }),
  });
}

export function useApproveWriteOff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => approveWriteOff(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['write-offs'] }),
  });
}

export function useRejectWriteOff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => rejectWriteOff(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['write-offs'] }),
  });
}
