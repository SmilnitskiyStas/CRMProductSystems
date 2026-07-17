"use client";

import { Plus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import type { FloorPlanLayout, StoreZoneDto, ZoneStatusCounts } from "../types";
import { STATUS_CONFIG, ZONE_TYPE_ICONS, worstStatus } from "./FloorPlanCanvas";

interface Props {
  zones: StoreZoneDto[];
  layout: FloorPlanLayout;
  counts: Map<string, ZoneStatusCounts> | undefined;
  selectedZoneId: string | null;
  onAdd: (zoneId: string) => void;
  onRemove: (zoneId: string) => void;
}

export function FloorPlanSidePanel({ zones, layout, counts, selectedZoneId, onAdd, onRemove }: Props) {
  const t = useTranslations("Dashboard.stores.floorPlan");
  const tZoneStatus = useTranslations("Dashboard.stores.zoneStatus");
  const placedIds = new Set(layout.zones.map((z) => z.zoneId));
  const unplaced = zones.filter((z) => z.isActive && !placedIds.has(z.id));
  const selected = selectedZoneId ? zones.find((z) => z.id === selectedZoneId) : null;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* Tools */}
      <Panel title={t("toolsTitle")}>
        {selected ? (
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
              {ZONE_TYPE_ICONS[selected.type] ?? "🗂️"} {selected.name}
            </div>
            <button
              onClick={() => onRemove(selected.id)}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                background: "#2a0a0a",
                border: "1px solid #991b1b",
                color: "#ef4444",
                borderRadius: 8,
                padding: "7px 12px",
                fontSize: 12,
                cursor: "pointer",
              }}
            >
              <Trash2 size={14} /> {t("removeFromPlan")}
            </button>
          </div>
        ) : (
          <p style={{ color: "#6B7280", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
            {t("toolsHint")}
          </p>
        )}
      </Panel>

      {/* Unplaced zones */}
      <Panel title={t("unplacedTitle", { count: unplaced.length })}>
        {unplaced.length === 0 ? (
          <p style={{ color: "#6B7280", fontSize: 12, margin: 0 }}>{t("allZonesPlaced")}</p>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {unplaced.map((zone) => (
              <button
                key={zone.id}
                onClick={() => onAdd(zone.id)}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 8,
                  background: "#10141d",
                  border: "1px solid #1F2937",
                  color: "#E8EDF5",
                  borderRadius: 8,
                  padding: "8px 10px",
                  fontSize: 12,
                  cursor: "pointer",
                  textAlign: "left",
                }}
              >
                <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {ZONE_TYPE_ICONS[zone.type] ?? "🗂️"} {zone.name}
                </span>
                <Plus size={14} style={{ flexShrink: 0, color: "#3B82F6" }} />
              </button>
            ))}
          </div>
        )}
      </Panel>

      {/* Legend */}
      <Panel title={t("legendTitle")}>
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {(["safe", "warning", "critical", "expired", "empty"] as const).map((s) => {
            const cfg = STATUS_CONFIG[s];
            return (
              <div key={s} style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <span
                  style={{
                    width: 12,
                    height: 12,
                    borderRadius: 3,
                    background: cfg.bg,
                    border: `1px solid ${cfg.border}`,
                  }}
                />
                <span style={{ color: "#9CA3AF", fontSize: 12 }}>{tZoneStatus(s)}</span>
              </div>
            );
          })}
        </div>
        <p style={{ color: "#4B5563", fontSize: 11, marginTop: 10, marginBottom: 0, lineHeight: 1.5 }}>
          {t("legendHint")}
        </p>
      </Panel>

      {/* Placed zone statuses */}
      <Panel title={t("placedTitle")}>
        {layout.zones.length === 0 ? (
          <p style={{ color: "#6B7280", fontSize: 12, margin: 0 }}>—</p>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {layout.zones.map((p) => {
              const zone = zones.find((z) => z.id === p.zoneId);
              if (!zone) return null;
              const cfg = STATUS_CONFIG[worstStatus(counts?.get(p.zoneId))];
              return (
                <div key={p.zoneId} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12 }}>
                  <span style={{ width: 8, height: 8, borderRadius: "50%", background: cfg.color, flexShrink: 0 }} />
                  <span style={{ color: "#9CA3AF", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                    {zone.name}
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </Panel>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 16 }}>
      <h3 style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, margin: "0 0 12px 0" }}>{title}</h3>
      {children}
    </div>
  );
}
