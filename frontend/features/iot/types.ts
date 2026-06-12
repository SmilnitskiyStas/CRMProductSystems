export type DeviceType = "weight_sensor" | "camera" | "temp_sensor" | "barcode_reader";

export interface IotDeviceDto {
  id: string;
  storeId: string;
  storeName: string | null;
  zoneId: string | null;
  zoneName: string | null;
  deviceType: DeviceType;
  deviceId: string;
  name: string | null;
  mqttTopic: string | null;
  config: string | null;
  isActive: boolean;
  isOnline: boolean;
  lastSeenAt: string | null;
  batteryLevel: number | null;
  firmwareVersion: string | null;
  createdAt: string;
}

export interface RegisterDevicePayload {
  storeId: string;
  zoneId: string | null;
  deviceType: DeviceType;
  deviceId: string;
  name: string | null;
  mqttTopic: string | null;
  config: string | null;
}

export interface UpdateDevicePayload {
  zoneId: string | null;
  deviceType: DeviceType;
  name: string | null;
  mqttTopic: string | null;
  config: string | null;
  isActive: boolean;
}

export interface TemperatureReadingDto {
  id: string;
  deviceId: string;
  temperature: number;
  humidity: number | null;
  isAlert: boolean;
  recordedAt: string;
}

export interface LatestTemperatureDto {
  deviceId: string;
  deviceName: string | null;
  zoneId: string | null;
  temperature: number;
  humidity: number | null;
  isAlert: boolean;
  recordedAt: string;
}

export const DEVICE_TYPE_META: Record<DeviceType, { label: string; icon: string }> = {
  weight_sensor: { label: "Датчик ваги", icon: "⚖️" },
  temp_sensor: { label: "Датчик температури", icon: "🌡️" },
  camera: { label: "CV камера", icon: "📷" },
  barcode_reader: { label: "Сканер штрихкодів", icon: "📟" },
};
