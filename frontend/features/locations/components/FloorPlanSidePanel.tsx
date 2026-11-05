"use client";

import { useRef } from "react";
import { ImagePlus, Plus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import type { FloorPlanLayout, LocationZoneDto, ZoneStatusCounts } from "../types";
import { PLAN_ACCEPT } from "../lib/floorPlanBackground";
import { ZONE_PALETTE_TYPES, zoneTypeMeta } from "../lib/zoneTypes";
import { STATUS_CONFIG, ZONE_TYPE_ICONS, worstStatus } from "./FloorPlanCanvas";

interface Props {
  zones: LocationZoneDto[];
  layout: FloorPlanLayout;
  counts: Map<string, ZoneStatusCounts> | undefined;
  selectedZoneId: string | null;
  onAdd: (zoneId: string) => void;
  onRemove: (zoneId: string) => void;
  onCreateZone: (type?: string) => void;
  onUploadBackground: (file: File) => void;
  onBackgroundOpacity: (opacity: number) => void;
  onRemoveBackground: () => void;
  uploadingBackground: boolean;
  onOpenShelves: (zoneId: string) => void;
  onResizeCanvas: (w: number, h: number) => void;
}

export function FloorPlanSidePanel({
  zones,
  layout,
  counts,
  selectedZoneId,
  onAdd,
  onRemove,
  onCreateZone,
  onUploadBackground,
  onBackgroundOpacity,
  onRemoveBackground,
  uploadingBackground,
  onOpenShelves,
  onResizeCanvas,
}: Props) {
  const t = useTranslations("Dashboard.locations.floorPlan");
  const tTypes = useTranslations("Dashboard.locations.zoneTypes");
  const fileRef = useRef<HTMLInputElement | null>(null);
  const bg = layout.background ?? null;
  const tZoneStatus = useTranslations("Dashboard.locations.zoneStatus");
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
            <Btn variant="dangerOutline" size="sm" icon={<Trash2 size={14} />} onClick={() => onRemove(selected.id)} style={{ alignSelf: "flex-start" }}>
              {t("removeFromPlan")}
            </Btn>
            <Btn variant="link" size="sm" onClick={() => onOpenShelves(selected.id)} style={{ alignSelf: "flex-start" }}>
              {t("shelfBuilderLink")}
            </Btn>
          </div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            <p style={{ color: "#6B7280", fontSize: 12, margin: 0, lineHeight: 1.5 }}>
              {t("toolsHint")}
            </p>
            <div style={{ display: "flex", gap: 8 }}>
              <Btn variant="ghost" size="sm" onClick={() => onResizeCanvas(layout.canvasW + 200, layout.canvasH)} style={{ flex: 1, justifyContent: "center" }}>
                {t("addWidth")}
              </Btn>
              <Btn variant="ghost" size="sm" onClick={() => onResizeCanvas(layout.canvasW, layout.canvasH + 200)} style={{ flex: 1, justifyContent: "center" }}>
                {t("addHeight")}
              </Btn>
            </div>
          </div>
        )}
      </Panel>

      {/* Background plan */}
      <Panel title={t("backgroundTitle")}>
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          <input
            ref={fileRef}
            type="file"
            accept={PLAN_ACCEPT}
            hidden
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) onUploadBackground(f);
              e.target.value = "";
            }}
          />
          <Btn
            size="sm"
            fullWidth
            icon={<ImagePlus size={14} />}
            onClick={() => fileRef.current?.click()}
            disabled={uploadingBackground}
          >
            {uploadingBackground ? t("backgroundUploading") : t("backgroundUpload")}
          </Btn>
          {bg ? (
            <>
              <label style={{ color: "#9CA3AF", fontSize: 12 }}>
                {t("backgroundOpacity", { value: Math.round(bg.opacity * 100) })}
                <input
                  type="range"
                  min={10}
                  max={100}
                  step={5}
                  value={Math.round(bg.opacity * 100)}
                  onChange={(e) => onBackgroundOpacity(Number(e.target.value) / 100)}
                  style={{ width: "100%" }}
                />
              </label>
              <Btn variant="dangerOutline" size="sm" fullWidth icon={<Trash2 size={14} />} onClick={onRemoveBackground}>
                {t("backgroundRemove")}
              </Btn>
            </>
          ) : (
            <p style={{ color: "#6B7280", fontSize: 12, margin: 0, lineHeight: 1.5 }}>{t("backgroundHint")}</p>
          )}
        </div>
      </Panel>

      {/* Object type palette */}
      <Panel title={t("paletteTitle")}>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", gap: 6 }}>
          {ZONE_PALETTE_TYPES.map((type) => {
            const meta = zoneTypeMeta(type);
            return (
              <button
                key={type}
                onClick={() => onCreateZone(type)}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 6,
                  background: "#10141d",
                  border: `1px solid ${meta.color}66`,
                  color: "#E8EDF5",
                  borderRadius: 8,
                  padding: "7px 8px",
                  fontSize: 11,
                  lineHeight: 1.25,
                  cursor: "pointer",
                  textAlign: "left",
                  minWidth: 0,
                }}
              >
                <span style={{ flexShrink: 0 }}>{meta.icon}</span>
                <span style={{ minWidth: 0, overflowWrap: "anywhere" }}>{tTypes(type)}</span>
              </button>
            );
          })}
        </div>
      </Panel>

      {/* Unplaced zones */}
      <Panel title={t("unplacedTitle", { count: unplaced.length })}>
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          <Btn size="sm" fullWidth icon={<Plus size={14} />} onClick={() => onCreateZone()}>
            {t("newZone")}
          </Btn>
          {unplaced.length === 0 ? (
            <p style={{ color: "#6B7280", fontSize: 12, margin: 0 }}>{t("allZonesPlaced")}</p>
          ) : (
            unplaced.map((zone) => (
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
            ))
          )}
        </div>
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
