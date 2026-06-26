"use client";

import { useState } from "react";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { createSnapModifier } from "@dnd-kit/modifiers";
import type {
  FloorPlanLayout,
  FloorPlanZonePlacement,
  LocationZoneDto,
  ZoneStatus,
  ZoneStatusCounts,
} from "../types";

export const STATUS_CONFIG: Record<ZoneStatus | "empty", { color: string; bg: string; border: string; label: string }> = {
  safe:     { color: "#22c55e", bg: "#0d2818", border: "#166534", label: "Безпечно" },
  warning:  { color: "#f59e0b", bg: "#261c05", border: "#854d0e", label: "Попередження" },
  critical: { color: "#ef4444", bg: "#2a0a0a", border: "#991b1b", label: "Критично" },
  expired:  { color: "#6b7280", bg: "#141414", border: "#374151", label: "Протерміновано" },
  empty:    { color: "#4B5563", bg: "#10141d", border: "#1F2937", label: "Без товарів" },
};

export const ZONE_TYPE_ICONS: Record<string, string> = {
  refrigerated: "❄️",
  fresh: "🌿",
  dry: "📦",
  frozen: "🧊",
  fridge: "❄️",
  freezer: "🧊",
  shelf: "📦",
  display: "🪟",
  production: "⚙️",
  warehouse: "🏭",
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
  zones: LocationZoneDto[];
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
    <div
      style={{
        height: 600,
        overflow: "auto",
        border: "1px solid #1F2937",
        borderRadius: 12,
        background: "#0B0E14",
      }}
    >
      <DndContext
        sensors={sensors}
        modifiers={[createSnapModifier(grid)]}
        onDragEnd={handleDragEnd}
      >
        <div
          onClick={() => onSelect(null)}
          style={{
            position: "relative",
            width: layout.canvasW,
            height: layout.canvasH,
            background: "#0B0E14",
            backgroundImage:
              "linear-gradient(#161B26 1px, transparent 1px), linear-gradient(90deg, #161B26 1px, transparent 1px)",
            backgroundSize: `${grid}px ${grid}px`,
          }}
        >
          {layout.zones.map((placement) => {
            const zone = zoneById.get(placement.zoneId);
            if (!zone) return null;
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
              План порожній — додайте зони з панелі праворуч
            </div>
          )}
        </div>
      </DndContext>
    </div>
  );
}

interface ZoneBoxProps {
  zone: LocationZoneDto;
  placement: FloorPlanZonePlacement;
  counts: ZoneStatusCounts | undefined;
  grid: number;
  selected: boolean;
  onSelect: (zoneId: string) => void;
  onResize: (zoneId: string, w: number, h: number) => void;
}

function ZoneBox({ zone, placement, counts, grid, selected, onSelect, onResize }: ZoneBoxProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: placement.zoneId,
  });
  const [hovered, setHovered] = useState(false);

  const status = worstStatus(counts);
  const cfg = STATUS_CONFIG[status];
  const icon = ZONE_TYPE_ICONS[zone.type] ?? "🗂️";

  const tooltipAbove = placement.y >= 120;

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
        zIndex: isDragging ? 50 : hovered ? 50 : 1,
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
        <span style={{ color: cfg.color, fontSize: 10, fontWeight: 600 }}>{cfg.label}</span>
      </div>

      {hovered && !isDragging && (
        <div
          style={{
            position: "absolute",
            ...(tooltipAbove
              ? { bottom: "100%", marginBottom: 6 }
              : { top: "100%", marginTop: 6 }),
            left: 0,
            background: "#1F2733",
            border: "1px solid #2D3748",
            borderRadius: 8,
            padding: "8px 10px",
            whiteSpace: "nowrap",
            pointerEvents: "none",
            zIndex: 100,
            boxShadow: "0 4px 12px rgba(0,0,0,0.4)",
          }}
        >
          <TooltipRow label="Safe" value={counts?.safe ?? 0} color="#22c55e" />
          <TooltipRow label="Warning" value={counts?.warning ?? 0} color="#f59e0b" />
          <TooltipRow label="Critical" value={counts?.critical ?? 0} color="#ef4444" />
          {(counts?.expired ?? 0) > 0 && (
            <TooltipRow label="Expired" value={counts!.expired} color="#6b7280" />
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
