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

/**
 * Device-type labels moved to i18n as of i18n Block 10 (TASK-389): `Dashboard.iot.deviceTypes.*`.
 * Icons stay a plain constant since they're not user-facing text; the key-order array and
 * label helper mirror `getEventTypeLabel` (features/notifications/types.ts).
 */
export const DEVICE_TYPES: DeviceType[] = ["weight_sensor", "temp_sensor", "camera", "barcode_reader"];

export const DEVICE_TYPE_ICONS: Record<DeviceType, string> = {
  weight_sensor: "⚖️",
  temp_sensor: "🌡️",
  camera: "📷",
  barcode_reader: "📟",
};

const DEVICE_TYPE_I18N_KEY: Record<DeviceType, string> = {
  weight_sensor: "weightSensor",
  temp_sensor: "tempSensor",
  camera: "camera",
  barcode_reader: "barcodeReader",
};

/** Translated device-type label. `t` must be scoped to `Dashboard.iot.deviceTypes`. */
export function getDeviceTypeLabel(t: (key: string) => string, deviceType: DeviceType): string {
  return t(DEVICE_TYPE_I18N_KEY[deviceType] ?? deviceType);
}
