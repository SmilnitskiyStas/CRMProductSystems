import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { changeConsumerPhone, getConsumerProfile, getConsumerProfileHistory, updateConsumerProfile } from './api';

export const useConsumerProfile = (enabled = true) => useQuery({ queryKey: ['consumer-profile'], queryFn: getConsumerProfile, enabled });
export const useConsumerProfileHistory = (enabled = true) => useQuery({ queryKey: ['consumer-profile', 'history'], queryFn: getConsumerProfileHistory, enabled });
export function useUpdateConsumerProfile() {
  const qc = useQueryClient();
  return useMutation({ mutationFn: updateConsumerProfile, onSuccess: (data) => {
    qc.setQueryData(['consumer-profile'], data); void qc.invalidateQueries({ queryKey: ['consumer-profile', 'history'] });
  } });
}
export function useChangeConsumerPhone() {
  const qc = useQueryClient();
  return useMutation({ mutationFn: changeConsumerPhone, onSuccess: (data) => {
    qc.setQueryData(['consumer-profile'], data); void qc.invalidateQueries({ queryKey: ['consumer-profile', 'history'] });
  } });
}
