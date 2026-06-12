import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { iotApi } from "../api/iot";
import type { RegisterDevicePayload, UpdateDevicePayload } from "../types";

const IOT_KEY = ["iot-devices"] as const;

export function useIotDevices(storeId?: string) {
  return useQuery({
    queryKey: [...IOT_KEY, storeId ?? "all"],
    queryFn: () => iotApi.getDevices(storeId),
    refetchInterval: 60_000, // online/offline status should stay fresh
  });
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: IOT_KEY });
}

export function useRegisterDevice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (payload: RegisterDevicePayload) => iotApi.register(payload),
    onSuccess: invalidate,
  });
}

export function useUpdateDevice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateDevicePayload }) =>
      iotApi.update(id, payload),
    onSuccess: invalidate,
  });
}

export function useDeactivateDevice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: (id: string) => iotApi.deactivate(id),
    onSuccess: invalidate,
  });
}

export function useTemperatureReadings(deviceId: string | null, hours = 24) {
  return useQuery({
    queryKey: ["iot-readings", deviceId, hours],
    queryFn: () => iotApi.getReadings(deviceId!, hours),
    enabled: !!deviceId,
    refetchInterval: 5 * 60_000,
  });
}

export function useLatestTemperatures(storeId: string | null) {
  return useQuery({
    queryKey: ["iot-temperature", storeId],
    queryFn: () => iotApi.getLatestTemperatures(storeId!),
    enabled: !!storeId,
    refetchInterval: 5 * 60_000,
  });
}
