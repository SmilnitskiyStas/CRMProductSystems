import { api } from "@/lib/api";
import type {
  IotDeviceDto,
  LatestTemperatureDto,
  RegisterDevicePayload,
  TemperatureReadingDto,
  UpdateDevicePayload,
} from "../types";

export const iotApi = {
  getDevices: (storeId?: string) =>
    api.get<IotDeviceDto[]>(`/api/iot/devices${storeId ? `?store_id=${storeId}` : ""}`),
  register: (payload: RegisterDevicePayload) =>
    api.post<IotDeviceDto>("/api/iot/devices", payload),
  update: (id: string, payload: UpdateDevicePayload) =>
    api.put<IotDeviceDto>(`/api/iot/devices/${id}`, payload),
  deactivate: (id: string) => api.delete<void>(`/api/iot/devices/${id}`),
  getReadings: (deviceId: string, hours = 24) =>
    api.get<TemperatureReadingDto[]>(`/api/iot/devices/${deviceId}/readings?hours=${hours}`),
  getLatestTemperatures: (storeId: string) =>
    api.get<LatestTemperatureDto[]>(`/api/iot/temperature?store_id=${storeId}`),
};
