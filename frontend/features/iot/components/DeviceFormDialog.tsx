"use client";

import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { StoreZoneDto } from "@/features/stores/types";
import { DEVICE_TYPE_META, type DeviceType, type IotDeviceDto } from "../types";

const deviceSchema = z.object({
  deviceId: z.string().min(1, "Обов'язкове поле").max(100),
  deviceType: z.enum(["weight_sensor", "camera", "temp_sensor", "barcode_reader"]),
  name: z.string().max(255).optional(),
  zoneId: z.string().optional(),
  mqttTopic: z.string().max(255).optional(),
  config: z
    .string()
    .optional()
    .refine((v) => {
      if (!v?.trim()) return true;
      try {
        JSON.parse(v);
        return true;
      } catch {
        return false;
      }
    }, "Невалідний JSON"),
});

type FormValues = z.infer<typeof deviceSchema>;

interface Props {
  device: IotDeviceDto | null; // null = register new
  zones: StoreZoneDto[];
  isPending: boolean;
  onClose: () => void;
  onSubmit: (values: {
    deviceId: string;
    deviceType: DeviceType;
    name: string | null;
    zoneId: string | null;
    mqttTopic: string | null;
    config: string | null;
  }) => void;
}

const inputStyle: React.CSSProperties = {
  width: "100%", background: "#111827", border: "1px solid #1F2937",
  borderRadius: 8, color: "#E8EDF5", fontSize: 13, padding: "8px 12px",
  outline: "none", boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 5,
};

const errStyle: React.CSSProperties = { color: "#F87171", fontSize: 11, marginTop: 3 };

export function DeviceFormDialog({ device, zones, isPending, onClose, onSubmit }: Props) {
  const isEditing = device !== null;

  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(deviceSchema),
    defaultValues: device
      ? {
          deviceId: device.deviceId,
          deviceType: device.deviceType,
          name: device.name ?? "",
          zoneId: device.zoneId ?? "",
          mqttTopic: device.mqttTopic ?? "",
          config: device.config ?? "",
        }
      : { deviceId: "", deviceType: "temp_sensor", name: "", zoneId: "", mqttTopic: "", config: "" },
  });

  function submit(v: FormValues) {
    onSubmit({
      deviceId: v.deviceId.trim(),
      deviceType: v.deviceType,
      name: v.name?.trim() || null,
      zoneId: v.zoneId || null,
      mqttTopic: v.mqttTopic?.trim() || null,
      config: v.config?.trim() || null,
    });
  }

  return (
    <Modal title={isEditing ? "Редагувати пристрій" : "Додати IoT пристрій"} onClose={onClose}>
      <form onSubmit={handleSubmit(submit)} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <div>
          <label style={labelStyle}>Фізичний ID пристрою *</label>
          <input
            {...register("deviceId")}
            placeholder="shelf-A1-3"
            disabled={isEditing}
            style={{ ...inputStyle, opacity: isEditing ? 0.6 : 1 }}
          />
          {errors.deviceId && <div style={errStyle}>{errors.deviceId.message}</div>}
        </div>

        <div>
          <label style={labelStyle}>Тип пристрою *</label>
          <select {...register("deviceType")} style={inputStyle}>
            {(Object.keys(DEVICE_TYPE_META) as DeviceType[]).map((t) => (
              <option key={t} value={t}>
                {DEVICE_TYPE_META[t].icon} {DEVICE_TYPE_META[t].label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>Назва</label>
          <input {...register("name")} placeholder="Холодильник молочний №1" style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle}>Зона</label>
          <select {...register("zoneId")} style={inputStyle}>
            <option value="">— без зони —</option>
            {zones.filter((z) => z.isActive).map((z) => (
              <option key={z.id} value={z.id}>{z.name}</option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>MQTT topic</label>
          <input {...register("mqttTopic")} placeholder="shelfguard/…" style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle}>Конфігурація (JSON)</label>
          <textarea
            {...register("config")}
            rows={3}
            placeholder={'{"profile": "fridge"} або {"product_id": "…", "unit_weight_grams": 245}'}
            style={{ ...inputStyle, fontFamily: "monospace", resize: "vertical" }}
          />
          {errors.config && <div style={errStyle}>{errors.config.message}</div>}
        </div>

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 6 }}>
          <Btn variant="ghost" onClick={onClose}>Скасувати</Btn>
          <Btn type="submit" disabled={isPending}>
            {isPending ? "Збереження…" : isEditing ? "Зберегти" : "Додати"}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
