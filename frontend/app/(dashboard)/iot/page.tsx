"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useStore, useStores } from "@/features/stores/hooks/useStores";
import { DevicesTable } from "@/features/iot/components/DevicesTable";
import { DeviceFormDialog } from "@/features/iot/components/DeviceFormDialog";
import { TemperaturePanel } from "@/features/iot/components/TemperaturePanel";
import {
  useDeactivateDevice,
  useIotDevices,
  useRegisterDevice,
  useUpdateDevice,
} from "@/features/iot/hooks/useIot";
import type { IotDeviceDto } from "@/features/iot/types";

export default function IotPage() {
  const { data: stores } = useStores();
  const [storeId, setStoreId] = useState<string | null>(null);
  const activeStoreId = storeId ?? stores?.[0]?.id ?? null;

  const { data: store } = useStore(activeStoreId);
  const { data: devices, isLoading } = useIotDevices(activeStoreId ?? undefined);

  const registerDevice = useRegisterDevice();
  const updateDevice = useUpdateDevice();
  const deactivateDevice = useDeactivateDevice();

  const [dialog, setDialog] = useState<{ open: boolean; device: IotDeviceDto | null }>({
    open: false,
    device: null,
  });

  function handleSubmit(values: {
    deviceId: string;
    deviceType: IotDeviceDto["deviceType"];
    name: string | null;
    zoneId: string | null;
    mqttTopic: string | null;
    config: string | null;
  }) {
    if (dialog.device) {
      updateDevice.mutate(
        {
          id: dialog.device.id,
          payload: { ...values, isActive: dialog.device.isActive },
        },
        {
          onSuccess: () => {
            toast.success("Пристрій оновлено");
            setDialog({ open: false, device: null });
          },
          onError: (e) => toast.error(e.message),
        }
      );
    } else {
      if (!activeStoreId) return;
      registerDevice.mutate(
        { ...values, storeId: activeStoreId },
        {
          onSuccess: () => {
            toast.success("Пристрій зареєстровано");
            setDialog({ open: false, device: null });
          },
          onError: (e) => toast.error(e.message),
        }
      );
    }
  }

  function handleDeactivate(device: IotDeviceDto) {
    deactivateDevice.mutate(device.id, {
      onSuccess: () => toast.success(`«${device.name ?? device.deviceId}» деактивовано`),
      onError: (e) => toast.error(e.message),
    });
  }

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>IoT пристрої</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            Датчики ваги, температури та камери — статус і показники
          </p>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          {stores && stores.length > 1 && (
            <select
              value={activeStoreId ?? ""}
              onChange={(e) => setStoreId(e.target.value)}
              style={{
                background: "#161B26", border: "1px solid #1F2937", color: "#E8EDF5",
                borderRadius: 8, padding: "8px 12px", fontSize: 13,
              }}
            >
              {stores.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          )}
          <Btn icon={<Plus size={15} />} onClick={() => setDialog({ open: true, device: null })}>
            Додати пристрій
          </Btn>
        </div>
      </div>

      {/* Devices */}
      <DevicesTable
        devices={devices}
        isLoading={isLoading}
        onEdit={(d) => setDialog({ open: true, device: d })}
        onDeactivate={handleDeactivate}
      />

      {/* Temperature monitoring */}
      {activeStoreId && devices && (
        <>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: "8px 0 0 0" }}>
            Температурний моніторинг
          </h2>
          <TemperaturePanel storeId={activeStoreId} devices={devices} />
        </>
      )}

      {dialog.open && (
        <DeviceFormDialog
          device={dialog.device}
          zones={store?.zones ?? []}
          isPending={registerDevice.isPending || updateDevice.isPending}
          onClose={() => setDialog({ open: false, device: null })}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  );
}
