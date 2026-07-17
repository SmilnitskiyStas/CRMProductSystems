"use client";

import { useTranslations } from "next-intl";
import type { ItemStatus, StoreZone } from "../types";

const STATUS_CONFIG: Record<ItemStatus, { color: string; bg: string; border: string }> = {
  safe: { color: "#22c55e", bg: "#0d2818", border: "#166534" },
  warning: { color: "#f59e0b", bg: "#261c05", border: "#854d0e" },
  critical: { color: "#ef4444", bg: "#2a0a0a", border: "#991b1b" },
  expired: { color: "#6b7280", bg: "#141414", border: "#374151" },
};

const ZONE_TYPE_ICONS: Record<string, string> = {
  refrigerated: "❄️",
  fresh: "🌿",
  dry: "📦",
  frozen: "🧊",
};

interface Props {
  zones: StoreZone[] | undefined;
  isLoading: boolean;
}

export function StoreMap({ zones = [], isLoading }: Props) {
  const t = useTranslations("Dashboard.dashboard.storeMap");
  const tStatus = useTranslations("Dashboard.dashboard.status");
  const tCommon = useTranslations("Common");

  return (
    <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, overflow: "hidden" }}>
      <div
        style={{
          padding: "16px 20px",
          borderBottom: "1px solid #1F2937",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: 0 }}>{t("title")}</h2>
        <div style={{ display: "flex", gap: 12 }}>
          {(["safe", "warning", "critical"] as ItemStatus[]).map((s) => {
            const cfg = STATUS_CONFIG[s];
            return (
              <div key={s} style={{ display: "flex", alignItems: "center", gap: 4 }}>
                <span
                  style={{
                    width: 8,
                    height: 8,
                    borderRadius: 2,
                    background: cfg.color,
                    display: "inline-block",
                  }}
                />
                <span style={{ color: "#6B7280", fontSize: 11 }}>
                  {tStatus(s)}
                </span>
              </div>
            );
          })}
        </div>
      </div>

      <div style={{ padding: 20 }}>
        {isLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "24px 0" }}>
            {tCommon("loading")}
          </div>
        ) : (
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))",
              gap: 12,
            }}
          >
            {zones.map((zone) => {
              const cfg = STATUS_CONFIG[zone.status];
              const icon = ZONE_TYPE_ICONS[zone.type] ?? "🗂️";
              const total = zone.safe + zone.warning + zone.critical;
              return (
                <div
                  key={zone.id}
                  title={`${tStatus("safe")}: ${zone.safe} · ${tStatus("warning")}: ${zone.warning} · ${tStatus("critical")}: ${zone.critical}`}
                  style={{
                    background: cfg.bg,
                    border: `1px solid ${cfg.border}`,
                    borderRadius: 10,
                    padding: "14px 16px",
                    cursor: "default",
                    transition: "transform 0.1s",
                  }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.transform = "translateY(-1px)")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.transform = "none")}
                >
                  <div style={{ fontSize: 20, marginBottom: 8 }}>{icon}</div>
                  <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 4, lineHeight: 1.3 }}>
                    {zone.name}
                  </div>
                  <div
                    style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 4,
                      padding: "2px 8px",
                      borderRadius: 5,
                      background: `${cfg.color}15`,
                      marginBottom: 10,
                    }}
                  >
                    <span
                      style={{
                        width: 5,
                        height: 5,
                        borderRadius: "50%",
                        background: cfg.color,
                        display: "inline-block",
                      }}
                    />
                    <span style={{ color: cfg.color, fontSize: 11, fontWeight: 600 }}>
                      {tStatus(zone.status)}
                    </span>
                  </div>
                  <div style={{ display: "flex", gap: 8, marginTop: 4 }}>
                    <ZoneStat value={zone.safe} color="#22c55e" label="S" />
                    <ZoneStat value={zone.warning} color="#f59e0b" label="W" />
                    <ZoneStat value={zone.critical} color="#ef4444" label="C" />
                  </div>
                  <div style={{ color: "#374151", fontSize: 10, marginTop: 6 }}>{total} {t("positionsSuffix")}</div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

function ZoneStat({ value, color, label }: { value: number; color: string; label: string }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 3 }}>
      <span style={{ color, fontSize: 10, fontWeight: 600 }}>{label}</span>
      <span style={{ color: value > 0 ? color : "#374151", fontSize: 12, fontFamily: "monospace", fontWeight: 600 }}>
        {value}
      </span>
    </div>
  );
}
