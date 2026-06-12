"use client";

import { Pencil, Power } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { DEVICE_TYPE_META, type IotDeviceDto } from "../types";

interface Props {
  devices: IotDeviceDto[] | undefined;
  isLoading: boolean;
  onEdit: (device: IotDeviceDto) => void;
  onDeactivate: (device: IotDeviceDto) => void;
}

const th: React.CSSProperties = {
  textAlign: "left", color: "#6B7280", fontSize: 11, fontWeight: 600,
  textTransform: "uppercase", letterSpacing: 0.5, padding: "10px 14px",
  borderBottom: "1px solid #1F2937", whiteSpace: "nowrap",
};

const td: React.CSSProperties = {
  color: "#E8EDF5", fontSize: 13, padding: "12px 14px",
  borderBottom: "1px solid #161B26", verticalAlign: "middle",
};

export function DevicesTable({ devices, isLoading, onEdit, onDeactivate }: Props) {
  if (isLoading) {
    return <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>Завантаження…</div>;
  }
  if (!devices?.length) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: 32 }}>
        Немає зареєстрованих пристроїв — додайте перший
      </div>
    );
  }

  return (
    <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, overflow: "auto" }}>
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={th}>Пристрій</th>
            <th style={th}>Тип</th>
            <th style={th}>Зона</th>
            <th style={th}>Статус</th>
            <th style={th}>Остання активність</th>
            <th style={th}>Батарея</th>
            <th style={th}>Firmware</th>
            <th style={th}></th>
          </tr>
        </thead>
        <tbody>
          {devices.map((d) => {
            const meta = DEVICE_TYPE_META[d.deviceType] ?? { label: d.deviceType, icon: "🗂️" };
            return (
              <tr key={d.id} style={{ opacity: d.isActive ? 1 : 0.45 }}>
                <td style={td}>
                  <div style={{ fontWeight: 600 }}>{d.name ?? d.deviceId}</div>
                  <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>{d.deviceId}</div>
                </td>
                <td style={td}>{meta.icon} {meta.label}</td>
                <td style={td}>{d.zoneName ?? "—"}</td>
                <td style={td}>
                  {!d.isActive ? (
                    <StatusBadge color="#6B7280" label="Деактивовано" />
                  ) : d.isOnline ? (
                    <StatusBadge color="#22c55e" label="Онлайн" />
                  ) : (
                    <StatusBadge color="#ef4444" label="Офлайн" />
                  )}
                </td>
                <td style={{ ...td, color: "#9CA3AF", fontSize: 12 }}>
                  {d.lastSeenAt ? new Date(d.lastSeenAt).toLocaleString("uk-UA") : "ніколи"}
                </td>
                <td style={td}>
                  {d.batteryLevel != null ? <Battery level={d.batteryLevel} /> : "—"}
                </td>
                <td style={{ ...td, color: "#9CA3AF", fontSize: 12, fontFamily: "monospace" }}>
                  {d.firmwareVersion ?? "—"}
                </td>
                <td style={{ ...td, whiteSpace: "nowrap" }}>
                  <div style={{ display: "flex", gap: 6, justifyContent: "flex-end" }}>
                    <Btn size="sm" variant="ghost" icon={<Pencil size={12} />} onClick={() => onEdit(d)}>
                      Змінити
                    </Btn>
                    {d.isActive && (
                      <Btn size="sm" variant="danger" icon={<Power size={12} />} onClick={() => onDeactivate(d)}>
                        Вимкнути
                      </Btn>
                    )}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
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
