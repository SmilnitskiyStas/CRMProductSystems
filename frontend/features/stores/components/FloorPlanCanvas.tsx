"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { createSnapModifier, restrictToParentElement } from "@dnd-kit/modifiers";
import type {
  FloorPlanLayout,
  FloorPlanZonePlacement,
  StoreZoneDto,
  ZoneStatus,
  ZoneStatusCounts,
} from "../types";

// Labels moved to i18n messages under `Dashboard.stores.zoneStatus` (i18n Block 2b,
// TASK-380) — render via `useTranslations("Dashboard.stores.zoneStatus")` keyed by the
// status value. Colors stay here since they're not language-dependent.
export const STATUS_CONFIG: Record<ZoneStatus | "empty", { color: string; bg: string; border: string }> = {
  safe:     { color: "#22c55e", bg: "#0d2818", border: "#166534" },
  warning:  { color: "#f59e0b", bg: "#261c05", border: "#854d0e" },
  critical: { color: "#ef4444", bg: "#2a0a0a", border: "#991b1b" },
  expired:  { color: "#6b7280", bg: "#141414", border: "#374151" },
  empty:    { color: "#4B5563", bg: "#10141d", border: "#1F2937" },
};

export const ZONE_TYPE_ICONS: Record<string, string> = {
  refrigerated: "❄️",
  fresh: "🌿",
  dry: "📦",
  frozen: "🧊",
};

// Worst status wins: expired > critical > warning > safe; no stock → empty
export function worstStatus(counts: ZoneStatusCounts | undefined): ZoneStatus | "empty" {
  if (!counts) return "empty";
  if (counts.expired > 0) return "expired";
  if (counts.critical > 0) return "critical";
  if (counts.warning > 0) return "warning";
  if (counts.safe > 0) return "safe";
  return "empty";
}

interface CanvasProps {
  zones: StoreZoneDto[];
  layout: FloorPlanLayout;
  counts: Map<string, ZoneStatusCounts> | undefined;
  selectedZoneId: string | null;
  onSelect: (zoneId: string | null) => void;
  onMove: (zoneId: string, x: number, y: number) => void;
  onResize: (zoneId: string, w: number, h: number) => void;
}

export function FloorPlanCanvas({
  zones,
  layout,
  counts,
  selectedZoneId,
  onSelect,
  onMove,
  onResize,
}: CanvasProps) {
  const t = useTranslations("Dashboard.stores.floorPlan");
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  );
  const grid = layout.grid;

  function handleDragEnd(event: DragEndEvent) {
    const placement = layout.zones.find((z) => z.zoneId === event.active.id);
    if (!placement) return;
    const x = Math.max(0, placement.x + event.delta.x);
    const y = Math.max(0, placement.y + event.delta.y);
    onMove(placement.zoneId, Math.round(x / grid) * grid, Math.round(y / grid) * grid);
  }

  const zoneById = new Map(zones.map((z) => [z.id, z]));

  return (
    <DndContext
      sensors={sensors}
      modifiers={[createSnapModifier(grid), restrictToParentElement]}
      onDragEnd={handleDragEnd}
    >
      <div
        onClick={() => onSelect(null)}
        style={{
          position: "relative",
          height: 620,
          background: "#0B0E14",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "hidden",
          backgroundImage:
            "linear-gradient(#161B26 1px, transparent 1px), linear-gradient(90deg, #161B26 1px, transparent 1px)",
          backgroundSize: `${grid}px ${grid}px`,
        }}
      >
        {layout.zones.map((placement) => {
          const zone = zoneById.get(placement.zoneId);
          if (!zone) return null; // zone deleted after layout was saved
          return (
            <ZoneBox
              key={placement.zoneId}
              zone={zone}
              placement={placement}
              counts={counts?.get(placement.zoneId)}
              grid={grid}
              selected={selectedZoneId === placement.zoneId}
              onSelect={onSelect}
              onResize={onResize}
            />
          );
        })}
        {layout.zones.length === 0 && (
          <div
            style={{
              position: "absolute",
              inset: 0,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              color: "#4B5563",
              fontSize: 13,
            }}
          >
            {t("emptyCanvas")}
          </div>
        )}
      </div>
    </DndContext>
  );
}

interface ZoneBoxProps {
  zone: StoreZoneDto;
  placement: FloorPlanZonePlacement;
  counts: ZoneStatusCounts | undefined;
  grid: number;
  selected: boolean;
  onSelect: (zoneId: string) => void;
  onResize: (zoneId: string, w: number, h: number) => void;
}

function ZoneBox({ zone, placement, counts, grid, selected, onSelect, onResize }: ZoneBoxProps) {
  const t = useTranslations("Dashboard.stores.zoneStatus");
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: placement.zoneId,
  });
  const [hovered, setHovered] = useState(false);

  const status = worstStatus(counts);
  const cfg = STATUS_CONFIG[status];
  const icon = ZONE_TYPE_ICONS[zone.type] ?? "🗂️";

  // Native pointer-event resize: dnd-kit owns dragging, the handle owns sizing
  function startResize(e: React.PointerEvent) {
    e.stopPropagation();
    e.preventDefault();
    const startX = e.clientX;
    const startY = e.clientY;
    const startW = placement.w;
    const startH = placement.h;

    function move(ev: PointerEvent) {
      const w = Math.max(grid * 4, startW + ev.clientX - startX);
      const h = Math.max(grid * 3, startH + ev.clientY - startY);
      onResize(placement.zoneId, Math.round(w / grid) * grid, Math.round(h / grid) * grid);
    }
    function up() {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
    }
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }

  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      onClick={(e) => {
        e.stopPropagation();
        onSelect(placement.zoneId);
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: "absolute",
        left: placement.x,
        top: placement.y,
        width: placement.w,
        height: placement.h,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined,
        background: cfg.bg,
        border: `1px solid ${selected ? "#3B82F6" : cfg.border}`,
        boxShadow: selected ? "0 0 0 2px #3B82F655" : undefined,
        borderRadius: 8,
        padding: "10px 12px",
        cursor: isDragging ? "grabbing" : "grab",
        zIndex: isDragging || hovered ? 10 : 1,
        userSelect: "none",
        touchAction: "none",
        display: "flex",
        flexDirection: "column",
        gap: 4,
        overflow: "visible",
      }}
    >
      <div style={{ fontSize: 16, lineHeight: 1 }}>{icon}</div>
      <div
        style={{
          color: "#E8EDF5",
          fontSize: 12,
          fontWeight: 600,
          lineHeight: 1.3,
          overflow: "hidden",
          textOverflow: "ellipsis",
        }}
      >
        {zone.name}
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
        <span style={{ width: 6, height: 6, borderRadius: "50%", background: cfg.color }} />
        <span style={{ color: cfg.color, fontSize: 10, fontWeight: 600 }}>{t(status)}</span>
      </div>

      {/* Hover tooltip: safe/warning/critical breakdown (spec §6.4) */}
      {hovered && !isDragging && (
        <div
          style={{
            position: "absolute",
            bottom: "100%",
            left: 0,
            marginBottom: 6,
            background: "#1F2733",
            border: "1px solid #2D3748",
            borderRadius: 8,
            padding: "8px 10px",
            whiteSpace: "nowrap",
            pointerEvents: "none",
            zIndex: 20,
            boxShadow: "0 4px 12px rgba(0,0,0,0.4)",
          }}
        >
          <TooltipRow label={t("safe")} value={counts?.safe ?? 0} color="#22c55e" />
          <TooltipRow label={t("warning")} value={counts?.warning ?? 0} color="#f59e0b" />
          <TooltipRow label={t("critical")} value={counts?.critical ?? 0} color="#ef4444" />
          {(counts?.expired ?? 0) > 0 && (
            <TooltipRow label={t("expired")} value={counts!.expired} color="#6b7280" />
          )}
        </div>
      )}

      {/* Resize handle */}
      <div
        onPointerDown={startResize}
        style={{
          position: "absolute",
          right: 0,
          bottom: 0,
          width: 14,
          height: 14,
          cursor: "nwse-resize",
          borderRight: `2px solid ${selected ? "#3B82F6" : cfg.border}`,
          borderBottom: `2px solid ${selected ? "#3B82F6" : cfg.border}`,
          borderBottomRightRadius: 6,
        }}
      />
    </div>
  );
}

function TooltipRow({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11, lineHeight: 1.7 }}>
      <span style={{ width: 7, height: 7, borderRadius: 2, background: color }} />
      <span style={{ color: "#9CA3AF" }}>{label}:</span>
      <span style={{ color: "#E8EDF5", fontFamily: "monospace", fontWeight: 600 }}>{value}</span>
    </div>
  );
}
