"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { createSnapModifier } from "@dnd-kit/modifiers";
import { Maximize2, ZoomIn, ZoomOut } from "lucide-react";
import type {
  FloorPlanLayout,
  FloorPlanZonePlacement,
  LocationZoneDto,
  ZoneStatus,
  ZoneStatusCounts,
} from "../types";
import { ZONE_TYPE_META, zoneTypeMeta } from "../lib/zoneTypes";
import { resolveScanImageUrl } from "./ShelfScanPanel";

const ZOOM_MIN = 0.4;
const ZOOM_MAX = 2;
const ZOOM_STEP = 0.1;
const WHEEL_ZOOM_SENSITIVITY = 0.001;
const FIT_VIEW_PADDING = 0.92;

// Labels moved to i18n messages under `Dashboard.locations.zoneStatus` (i18n Block 2b,
// TASK-380) — render via `useTranslations("Dashboard.locations.zoneStatus")` keyed by the
// status value. Colors stay here since they're not language-dependent.
export const STATUS_CONFIG: Record<ZoneStatus | "empty", { color: string; bg: string; border: string }> = {
  safe:     { color: "#22c55e", bg: "#0d2818", border: "#166534" },
  warning:  { color: "#f59e0b", bg: "#261c05", border: "#854d0e" },
  critical: { color: "#ef4444", bg: "#2a0a0a", border: "#991b1b" },
  expired:  { color: "#6b7280", bg: "#141414", border: "#374151" },
  empty:    { color: "#4B5563", bg: "#10141d", border: "#1F2937" },
};

export const ZONE_TYPE_ICONS: Record<string, string> = Object.fromEntries(
  Object.entries(ZONE_TYPE_META).map(([k, v]) => [k, v.icon]),
);

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
  /** "view": no drag/resize, click opens the zone's shelves page. */
  mode?: "edit" | "view";
  onOpen?: (zoneId: string) => void;
}

export function FloorPlanCanvas({
  zones,
  layout,
  counts,
  selectedZoneId,
  onSelect,
  onMove,
  onResize,
  mode = "edit",
  onOpen,
}: CanvasProps) {
  const t = useTranslations("Dashboard.locations.floorPlan");
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  );
  // Snap step for drag/resize — finer than the visual grid so small objects fit the plan.
  const [snap, setSnap] = useState(10);
  const [zoom, setZoom] = useState(1);
  const wrapperRef = useRef<HTMLDivElement>(null);

  const clampZoom = (z: number) => Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, z));
  const setZoomClamped = (z: number) => setZoom(Math.round(clampZoom(z) * 100) / 100);

  function zoomIn() {
    setZoomClamped(zoom + ZOOM_STEP);
  }
  function zoomOut() {
    setZoomClamped(zoom - ZOOM_STEP);
  }
  function fitToView() {
    const el = wrapperRef.current;
    if (!el) return;
    const scaleX = el.clientWidth / layout.canvasW;
    const scaleY = el.clientHeight / layout.canvasH;
    setZoomClamped(Math.min(scaleX, scaleY) * FIT_VIEW_PADDING);
    el.scrollTo(0, 0);
  }
  function handleWheel(e: React.WheelEvent) {
    if (!e.ctrlKey) return; // plain scroll/pan must pass through untouched
    e.preventDefault();
    setZoomClamped(zoom - e.deltaY * WHEEL_ZOOM_SENSITIVITY);
  }

  function handleDragEnd(event: DragEndEvent) {
    const placement = layout.zones.find((z) => z.zoneId === event.active.id);
    if (!placement) return;
    const dx = event.delta.x / zoom;
    const dy = event.delta.y / zoom;
    const x = Math.max(0, placement.x + dx);
    const y = Math.max(0, placement.y + dy);
    onMove(placement.zoneId, Math.round(x / snap) * snap, Math.round(y / snap) * snap);
  }

  const bg = layout.background ?? null;
  const zoneById = new Map(zones.map((z) => [z.id, z]));

  return (
    <div style={{ position: "relative", minWidth: 0 }}>
      <div
        ref={wrapperRef}
        onWheel={handleWheel}
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
          modifiers={[createSnapModifier(snap * zoom)]}
          onDragEnd={handleDragEnd}
        >
          <div
            style={{
              width: layout.canvasW * zoom,
              height: layout.canvasH * zoom,
              position: "relative",
            }}
          >
            <div
              onClick={() => onSelect(null)}
              style={{
                position: "absolute",
                top: 0,
                left: 0,
                width: layout.canvasW,
                height: layout.canvasH,
                transform: `scale(${zoom})`,
                transformOrigin: "0 0",
                background: "#0B0E14",
                backgroundImage:
                  "linear-gradient(#161B26 1px, transparent 1px), linear-gradient(90deg, #161B26 1px, transparent 1px)",
                backgroundSize: `${layout.grid}px ${layout.grid}px`,
              }}
            >
              {bg && (
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  src={resolveScanImageUrl(bg.url)}
                  alt=""
                  draggable={false}
                  style={{
                    position: "absolute",
                    top: 0,
                    left: 0,
                    width: layout.canvasW,
                    height: layout.canvasH,
                    objectFit: "fill",
                    opacity: bg.opacity,
                    pointerEvents: "none",
                    userSelect: "none",
                  }}
                />
              )}
              {layout.zones.map((placement) => {
                const zone = zoneById.get(placement.zoneId);
                if (!zone) return null;
                return (
                  <ZoneBox
                    key={placement.zoneId}
                    zone={zone}
                    placement={placement}
                    counts={counts?.get(placement.zoneId)}
                    grid={snap}
                    hasBackground={!!bg}
                    zoom={zoom}
                    selected={selectedZoneId === placement.zoneId}
                    onSelect={onSelect}
                    onResize={onResize}
                    mode={mode}
                    onOpen={onOpen}
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
          </div>
        </DndContext>
      </div>

      {/* Zoom toolbar */}
      <div
        style={{
          position: "absolute",
          bottom: 12,
          right: 12,
          zIndex: 300,
          display: "flex",
          alignItems: "center",
          gap: 4,
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 8,
          padding: 4,
        }}
      >
        <ToolbarButton
          onClick={zoomOut}
          disabled={zoom <= ZOOM_MIN}
          title={t("zoomOut")}
        >
          <ZoomOut size={15} />
        </ToolbarButton>
        <span
          style={{
            color: "#9CA3AF",
            fontSize: 12,
            fontWeight: 600,
            minWidth: 40,
            textAlign: "center",
            userSelect: "none",
          }}
        >
          {Math.round(zoom * 100)}%
        </span>
        <ToolbarButton
          onClick={zoomIn}
          disabled={zoom >= ZOOM_MAX}
          title={t("zoomIn")}
        >
          <ZoomIn size={15} />
        </ToolbarButton>
        <div style={{ width: 1, height: 20, background: "#1F2937", margin: "0 2px" }} />
        {SNAP_STEPS.map((s) => (
          <button
            key={s}
            onClick={() => setSnap(s)}
            title={t("snapStep", { step: s })}
            style={{
              height: 26,
              minWidth: 26,
              padding: "0 6px",
              fontSize: 11,
              fontWeight: 600,
              borderRadius: 6,
              cursor: "pointer",
              background: snap === s ? "#1D3461" : "#0B0E14",
              color: snap === s ? "#93C5FD" : "#9CA3AF",
              border: "1px solid #1F2937",
            }}
          >
            {s}
          </button>
        ))}
        <div style={{ width: 1, height: 20, background: "#1F2937", margin: "0 2px" }} />
        <ToolbarButton onClick={fitToView} title={t("fitToView")}>
          <Maximize2 size={15} />
        </ToolbarButton>
      </div>
    </div>
  );
}

function ToolbarButton({
  onClick,
  disabled,
  title,
  children,
}: {
  onClick: () => void;
  disabled?: boolean;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      title={title}
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: 26,
        height: 26,
        background: "#0B0E14",
        border: "1px solid #1F2937",
        color: disabled ? "#374151" : "#9CA3AF",
        borderRadius: 6,
        cursor: disabled ? "not-allowed" : "pointer",
      }}
    >
      {children}
    </button>
  );
}

interface ZoneBoxProps {
  zone: LocationZoneDto;
  placement: FloorPlanZonePlacement;
  counts: ZoneStatusCounts | undefined;
  grid: number;
  hasBackground: boolean;
  zoom: number;
  selected: boolean;
  onSelect: (zoneId: string) => void;
  onResize: (zoneId: string, w: number, h: number) => void;
  mode: "edit" | "view";
  onOpen?: (zoneId: string) => void;
}

const SNAP_STEPS = [5, 10, 20];

function ZoneBox({ zone, placement, counts, grid, hasBackground, zoom, selected, onSelect, onResize, mode, onOpen }: ZoneBoxProps) {
  const t = useTranslations("Dashboard.locations.zoneStatus");
  const tTypes = useTranslations("Dashboard.locations.zoneTypes");
  const viewOnly = mode === "view";
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: placement.zoneId,
    disabled: viewOnly,
  });
  const [hovered, setHovered] = useState(false);

  const status = worstStatus(counts);
  const cfg = STATUS_CONFIG[status];
  const meta = zoneTypeMeta(zone.type);
  const icon = meta.icon;
  const markerOnly = !meta.holdsStock;
  const accent = markerOnly ? meta.color : cfg.border;

  const tooltipAbove = placement.y >= 120;

  // Small objects drawn over a real plan: drop padding/status text so they don't cover it.
  const compact = placement.w < 100 || placement.h < 70;
  const tiny = placement.w < 44 || placement.h < 36;
  const typeLabel = tTypes.has(zone.type) ? tTypes(zone.type) : zone.type;

  function startResize(e: React.PointerEvent) {
    e.stopPropagation();
    e.preventDefault();
    const startX = e.clientX;
    const startY = e.clientY;
    const startW = placement.w;
    const startH = placement.h;

    function move(ev: PointerEvent) {
      const w = Math.max(grid * 2, startW + (ev.clientX - startX) / zoom);
      const h = Math.max(grid * 2, startH + (ev.clientY - startY) / zoom);
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
        if (viewOnly) onOpen?.(placement.zoneId);
        else onSelect(placement.zoneId);
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: "absolute",
        left: placement.x,
        top: placement.y,
        width: placement.w,
        height: placement.h,
        transform: transform ? `translate(${transform.x / zoom}px, ${transform.y / zoom}px)` : undefined,
        // Over a real plan the type colour must stand out; stock status stays as a corner dot.
        background: markerOnly ? `${meta.color}33` : hasBackground ? `${meta.color}4D` : cfg.bg,
        border: `${hasBackground ? 2 : 1}px solid ${selected ? "#3B82F6" : hasBackground ? meta.color : accent}`,
        boxShadow: selected
          ? "0 0 0 2px #3B82F655"
          : hasBackground
            ? `0 0 0 1px #0B0E14AA, 0 0 8px ${meta.color}88`
            : undefined,
        borderRadius: compact ? 4 : 8,
        padding: tiny ? 1 : compact ? "3px 5px" : "10px 12px",
        boxSizing: "border-box",
        cursor: viewOnly ? "pointer" : isDragging ? "grabbing" : "grab",
        zIndex: isDragging ? 50 : hovered ? 50 : 1,
        userSelect: "none",
        touchAction: "none",
        display: "flex",
        flexDirection: "column",
        gap: compact ? 1 : 4,
        overflow: "visible",
      }}
      title={compact ? `${zone.name} · ${typeLabel}` : undefined}
    >
      {hasBackground && compact && !markerOnly && (
        <span
          style={{
            position: "absolute",
            top: 2,
            right: 2,
            width: 8,
            height: 8,
            borderRadius: "50%",
            background: cfg.color,
            border: "1px solid #0B0E14",
          }}
        />
      )}
      <div style={{ fontSize: compact ? 12 : 16, lineHeight: 1 }}>{icon}</div>
      {!tiny && (
        <div
          style={{
            color: "#E8EDF5",
            fontSize: compact ? 10 : 12,
            fontWeight: 600,
            lineHeight: 1.2,
            overflow: "hidden",
            textOverflow: "ellipsis",
            whiteSpace: compact ? "nowrap" : undefined,
          }}
        >
          {zone.name}
        </div>
      )}
      {compact ? null : markerOnly ? (
        <div style={{ color: meta.color, fontSize: 10, fontWeight: 600 }}>{typeLabel}</div>
      ) : (
        <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
          <span style={{ width: 6, height: 6, borderRadius: "50%", background: cfg.color }} />
          <span style={{ color: cfg.color, fontSize: 10, fontWeight: 600 }}>{t(status)}</span>
        </div>
      )}

      {hovered && !isDragging && !markerOnly && (
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
          <TooltipRow label={t("safe")} value={counts?.safe ?? 0} color="#22c55e" />
          <TooltipRow label={t("warning")} value={counts?.warning ?? 0} color="#f59e0b" />
          <TooltipRow label={t("critical")} value={counts?.critical ?? 0} color="#ef4444" />
          {(counts?.expired ?? 0) > 0 && (
            <TooltipRow label={t("expired")} value={counts!.expired} color="#6b7280" />
          )}
        </div>
      )}

      {/* Resize handle */}
      {!viewOnly && <div
        onPointerDown={startResize}
        style={{
          position: "absolute",
          right: 0,
          bottom: 0,
          width: 14,
          height: 14,
          cursor: "nwse-resize",
          borderRight: `2px solid ${selected ? "#3B82F6" : accent}`,
          borderBottom: `2px solid ${selected ? "#3B82F6" : accent}`,
          borderBottomRightRadius: 6,
        }}
      />}
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
