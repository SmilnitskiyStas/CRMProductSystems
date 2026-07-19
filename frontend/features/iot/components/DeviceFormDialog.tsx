"use client";

import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { StoreZoneDto } from "@/features/stores/types";
import { DEVICE_TYPES, getDeviceTypeLabel, DEVICE_TYPE_ICONS, type DeviceType, type IotDeviceDto } from "../types";

// Zod .min(1, message) / .refine(..., message) need translated messages, so the schema is
// built once per render inside the component (see `useMemo(() => buildSchema(t), [t])`
// below) — mirrors `buildSchema(t)` in features/legal-entities/components/LegalEntityFormDialog.tsx
// (i18n Block 8b).
function buildSchema(t: ReturnType<typeof useTranslations>) {
  return z.object({
    deviceId: z.string().min(1, t("validationRequired")).max(100),
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
      }, t("validationInvalidJson")),
  });
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>;

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
  const t = useTranslations("Dashboard.iot.deviceForm");
  const tDeviceTypes = useTranslations("Dashboard.iot.deviceTypes");
  const isEditing = device !== null;

  const schema = useMemo(() => buildSchema(t), [t]);

  const { register, handleSubmit, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
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
    <Modal title={isEditing ? t("editTitle") : t("createTitle")} onClose={onClose}>
      <form onSubmit={handleSubmit(submit)} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <div>
          <label style={labelStyle}>{t("deviceIdLabel")}</label>
          <input
            {...register("deviceId")}
            placeholder={t("deviceIdPlaceholder")}
            disabled={isEditing}
            style={{ ...inputStyle, opacity: isEditing ? 0.6 : 1 }}
          />
          {errors.deviceId && <div style={errStyle}>{errors.deviceId.message}</div>}
        </div>

        <div>
          <label style={labelStyle}>{t("deviceTypeLabel")}</label>
          <select {...register("deviceType")} style={inputStyle}>
            {DEVICE_TYPES.map((deviceType) => (
              <option key={deviceType} value={deviceType}>
                {DEVICE_TYPE_ICONS[deviceType]} {getDeviceTypeLabel(tDeviceTypes, deviceType)}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("nameLabel")}</label>
          <input {...register("name")} placeholder={t("namePlaceholder")} style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle}>{t("zoneLabel")}</label>
          <select {...register("zoneId")} style={inputStyle}>
            <option value="">{t("noZoneOption")}</option>
            {zones.filter((z) => z.isActive).map((z) => (
              <option key={z.id} value={z.id}>{z.name}</option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("mqttTopicLabel")}</label>
          <input {...register("mqttTopic")} placeholder={t("mqttTopicPlaceholder")} style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle}>{t("configLabel")}</label>
          <textarea
            {...register("config")}
            rows={3}
            placeholder={t("configPlaceholder")}
            style={{ ...inputStyle, fontFamily: "monospace", resize: "vertical" }}
          />
          {errors.config && <div style={errStyle}>{errors.config.message}</div>}
        </div>

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 6 }}>
          <Btn variant="ghost" onClick={onClose}>{t("cancelButton")}</Btn>
          <Btn type="submit" disabled={isPending}>
            {isPending ? t("savingButton") : isEditing ? t("saveButton") : t("addButton")}
          </Btn>
        </div>
      </form>
    </Modal>
  );
}
