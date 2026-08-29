"use client";

import { Pencil, Power } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { DEVICE_TYPE_ICONS, getDeviceTypeLabel, type IotDeviceDto } from "../types";

interface Props {
  devices: IotDeviceDto[] | undefined;
  isLoading: boolean;
  onEdit: (device: IotDeviceDto) => void;
  onDeactivate: (device: IotDeviceDto) => void;
}

export function DevicesTable({ devices, isLoading, onEdit, onDeactivate }: Props) {
  const t = useTranslations("Dashboard.iot.devicesTable");
  const tDeviceTypes = useTranslations("Dashboard.iot.deviceTypes");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>{t("loading")}</div>;
  }
  if (!devices?.length) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>
        {t("empty")}
      </div>
    );
  }

  const columns: TableColumn<IotDeviceDto>[] = [
    {
      key: "device",
      header: t("columnDevice"),
      render: (d) => (
        <>
          <div style={{ fontWeight: 600 }}>{d.name ?? d.deviceId}</div>
          <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>{d.deviceId}</div>
        </>
      ),
    },
    {
      key: "type",
      header: t("columnType"),
      render: (d) => {
        const icon = DEVICE_TYPE_ICONS[d.deviceType] ?? "🗂️";
        const label = getDeviceTypeLabel(tDeviceTypes, d.deviceType);
        return <>{icon} {label}</>;
      },
    },
    {
      key: "zone",
      header: t("columnZone"),
      render: (d) => d.zoneName ?? "—",
    },
    {
      key: "status",
      header: t("columnStatus"),
      render: (d) =>
        !d.isActive ? (
          <StatusBadge color="#6B7280" label={t("statusDeactivated")} />
        ) : d.isOnline ? (
          <StatusBadge color="#22c55e" label={t("statusOnline")} />
        ) : (
          <StatusBadge color="#ef4444" label={t("statusOffline")} />
        ),
    },
    {
      key: "lastSeen",
      header: t("columnLastSeen"),
      cellStyle: { color: "#9CA3AF", fontSize: 12 },
      render: (d) => (d.lastSeenAt ? new Date(d.lastSeenAt).toLocaleString(intlLocale) : t("never")),
    },
    {
      key: "battery",
      header: t("columnBattery"),
      render: (d) => (d.batteryLevel != null ? <Battery level={d.batteryLevel} /> : "—"),
    },
    {
      key: "firmware",
      header: t("columnFirmware"),
      cellStyle: { color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" },
      render: (d) => d.firmwareVersion ?? "—",
    },
    {
      key: "actions",
      header: "",
      cellStyle: { whiteSpace: "nowrap" },
      render: (d) => (
        <div style={{ display: "flex", gap: 6, justifyContent: "center" }}>
          <Btn size="sm" variant="ghost" icon={<Pencil size={12} />} onClick={() => onEdit(d)}>
            {t("editButton")}
          </Btn>
          {d.isActive && (
            <Btn size="sm" variant="danger" icon={<Power size={12} />} onClick={() => onDeactivate(d)}>
              {t("deactivateButton")}
            </Btn>
          )}
        </div>
      ),
    },
  ];

  return (
    <Table
      columns={columns}
      rows={devices}
      rowKey={(d) => d.id}
      rowStyle={(d) => ({ opacity: d.isActive ? 1 : 0.45 })}
    />
  );
}

function StatusBadge({ color, label }: { color: string; label: string }) {
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 6, fontSize: 12, color }}>
      <span style={{ width: 7, height: 7, borderRadius: "50%", background: color }} />
      {label}
    </span>
  );
}

function Battery({ level }: { level: number }) {
  const color = level > 50 ? "#22c55e" : level > 20 ? "#f59e0b" : "#ef4444";
  return (
    <span style={{ color, fontSize: 12, fontFamily: "monospace", fontWeight: 600 }}>{level}%</span>
  );
}
