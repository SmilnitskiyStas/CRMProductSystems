"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { toast } from "sonner";
import { ArrowLeft, Plus, Save, Trash2 } from "lucide-react";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { createSnapModifier } from "@dnd-kit/modifiers";
import { useLocation } from "@/features/locations/hooks/useLocations";
import {
  parseShelfPlan,
  useUpdateZonePosition,
} from "@/features/locations/hooks/useFloorPlan";
import type { ShelfItemPlacement, ShelfPlanLayout } from "@/features/locations/types";

export default function ShelvesPage() {
  const params = useParams<{ id: string; zoneId: string }>();
  const router = useRouter();
  const locationId = params.id;
  const zoneId = params.zoneId;

  const { data: location, isLoading } = useLocation(locationId);
  const updateZonePosition = useUpdateZonePosition(locationId);

  const zone = useMemo(
    () => location?.zones.find((z) => z.id === zoneId) ?? null,
    [location, zoneId]
  );

  const [plan, setPlan] = useState<ShelfPlanLayout | null>(null);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (zone) {
      setPlan(parseShelfPlan(zone.position));
      setDirty(false);
    }
  }, [zone?.id, zone?.position]); // eslint-disable-line react-hooks/exhaustive-deps

  function patchPlan(fn: (prev: ShelfPlanLayout) => ShelfPlanLayout) {
    setPlan((prev) => (prev ? fn(prev) : prev));
    setDirty(true);
  }

  function handleAddSection() {
    patchPlan((prev) => {
      const n = prev.items.length + 1;
      const newItem: ShelfItemPlacement = {
        shelfId: crypto.randomUUID(),
        label: `Секція ${n}`,
        x: prev.grid * 2,
        y: prev.grid * 2 + (n - 1) * (80 + prev.grid),
        w: 200,
        h: 80,
      };
      return { ...prev, items: [...prev.items, newItem] };
    });
  }

  function handleDeleteSection(shelfId: string) {
    patchPlan((prev) => ({ ...prev, items: prev.items.filter((i) => i.shelfId !== shelfId) }));
  }

  function handleMove(shelfId: string, x: number, y: number) {
    patchPlan((prev) => ({
      ...prev,
      items: prev.items.map((i) => (i.shelfId === shelfId ? { ...i, x, y } : i)),
    }));
  }

  function handleResize(shelfId: string, w: number, h: number) {
    patchPlan((prev) => ({
      ...prev,
      items: prev.items.map((i) => (i.shelfId === shelfId ? { ...i, w, h } : i)),
    }));
  }

  function handleResizeCanvas(w: number, h: number) {
    patchPlan((prev) => ({ ...prev, canvasW: w, canvasH: h }));
  }

  function handleSave() {
    if (!plan || !zone) return;
    updateZonePosition.mutate(
      { zone, shelfLayout: plan },
      {
        onSuccess: () => {
          setDirty(false);
          toast.success("Конструктор поличок збережено");
        },
        onError: (e) => toast.error(`Не вдалося зберегти: ${e.message}`),
      }
    );
  }

  if (isLoading || !plan) {
    return (
      <div style={{ padding: "28px 32px", color: "#4B5563", fontSize: 13, textAlign: "center" }}>
        Завантаження…
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
            <button
              onClick={() => router.back()}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 4,
                background: "transparent",
                border: "none",
                color: "#6B7280",
                fontSize: 13,
                cursor: "pointer",
                padding: 0,
              }}
            >
              <ArrowLeft size={14} />
              Назад до плану
            </button>
          </div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Конструктор поличок
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {location?.name ?? "…"} / {zone?.name ?? "…"}
          </p>
        </div>

        <button
          onClick={handleSave}
          disabled={!dirty || updateZonePosition.isPending}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: dirty ? "#2563EB" : "#1F2937",
            border: "none",
            color: dirty ? "#fff" : "#6B7280",
            borderRadius: 8,
            padding: "9px 16px",
            fontSize: 13,
            fontWeight: 600,
            cursor: dirty ? "pointer" : "default",
          }}
        >
          <Save size={15} />
          {updateZonePosition.isPending ? "Збереження…" : "Зберегти"}
        </button>
      </div>

      {/* Canvas + panel */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 240px", gap: 20, alignItems: "start" }}>
        {/* Canvas */}
        <ShelfCanvas
          plan={plan}
          onMove={handleMove}
          onResize={handleResize}
        />

        {/* Side panel */}
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 16 }}>
            <h3 style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, margin: "0 0 12px 0" }}>
              Дії
            </h3>
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              <button
                onClick={handleAddSection}
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 6,
                  background: "#0d1f3a",
                  border: "1px solid #1D4ED8",
                  color: "#60A5FA",
                  borderRadius: 8,
                  padding: "8px 12px",
                  fontSize: 13,
                  fontWeight: 600,
                  cursor: "pointer",
                }}
              >
                <Plus size={14} /> Додати секцію
              </button>
              <div style={{ display: "flex", gap: 8 }}>
                <button
                  onClick={() => handleResizeCanvas(plan.canvasW + 200, plan.canvasH)}
                  style={{
                    flex: 1,
                    background: "#0B0E14",
                    border: "1px solid #1F2937",
                    color: "#9CA3AF",
                    borderRadius: 8,
                    padding: "6px 8px",
                    fontSize: 12,
                    cursor: "pointer",
                  }}
                >
                  + Ширина
                </button>
                <button
                  onClick={() => handleResizeCanvas(plan.canvasW, plan.canvasH + 200)}
                  style={{
                    flex: 1,
                    background: "#0B0E14",
                    border: "1px solid #1F2937",
                    color: "#9CA3AF",
                    borderRadius: 8,
                    padding: "6px 8px",
                    fontSize: 12,
                    cursor: "pointer",
                  }}
                >
                  + Висота
                </button>
              </div>
            </div>
          </div>

          <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 16 }}>
            <h3 style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, margin: "0 0 12px 0" }}>
              Секції ({plan.items.length})
            </h3>
            {plan.items.length === 0 ? (
              <p style={{ color: "#6B7280", fontSize: 12, margin: 0 }}>Немає секцій</p>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {plan.items.map((item) => (
                  <div
                    key={item.shelfId}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: 8,
                      background: "#0B0E14",
                      border: "1px solid #1F2937",
                      borderRadius: 8,
                      padding: "7px 10px",
                    }}
                  >
                    <span style={{ color: "#E8EDF5", fontSize: 12, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                      {item.label}
                    </span>
                    <button
                      onClick={() => handleDeleteSection(item.shelfId)}
                      style={{
                        background: "transparent",
                        border: "none",
                        color: "#6B7280",
                        cursor: "pointer",
                        padding: 2,
                        flexShrink: 0,
                        display: "flex",
                        alignItems: "center",
                      }}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

interface ShelfCanvasProps {
  plan: ShelfPlanLayout;
  onMove: (shelfId: string, x: number, y: number) => void;
  onResize: (shelfId: string, w: number, h: number) => void;
}

function ShelfCanvas({ plan, onMove, onResize }: ShelfCanvasProps) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } })
  );
  const grid = plan.grid;

  function handleDragEnd(event: DragEndEvent) {
    const item = plan.items.find((i) => i.shelfId === event.active.id);
    if (!item) return;
    const x = Math.max(0, item.x + event.delta.x);
    const y = Math.max(0, item.y + event.delta.y);
    onMove(item.shelfId, Math.round(x / grid) * grid, Math.round(y / grid) * grid);
  }

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
          style={{
            position: "relative",
            width: plan.canvasW,
            height: plan.canvasH,
            background: "#0B0E14",
            backgroundImage:
              "linear-gradient(#161B26 1px, transparent 1px), linear-gradient(90deg, #161B26 1px, transparent 1px)",
            backgroundSize: `${grid}px ${grid}px`,
          }}
        >
          {plan.items.map((item) => (
            <ShelfItemBox
              key={item.shelfId}
              item={item}
              grid={grid}
              onResize={onResize}
            />
          ))}
          {plan.items.length === 0 && (
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
              Додайте секції з панелі праворуч
            </div>
          )}
        </div>
      </DndContext>
    </div>
  );
}

interface ShelfItemBoxProps {
  item: ShelfItemPlacement;
  grid: number;
  onResize: (shelfId: string, w: number, h: number) => void;
}

function ShelfItemBox({ item, grid, onResize }: ShelfItemBoxProps) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: item.shelfId,
  });

  function startResize(e: React.PointerEvent) {
    e.stopPropagation();
    e.preventDefault();
    const startX = e.clientX;
    const startY = e.clientY;
    const startW = item.w;
    const startH = item.h;

    function move(ev: PointerEvent) {
      const w = Math.max(grid * 4, startW + ev.clientX - startX);
      const h = Math.max(grid * 2, startH + ev.clientY - startY);
      onResize(item.shelfId, Math.round(w / grid) * grid, Math.round(h / grid) * grid);
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
      style={{
        position: "absolute",
        left: item.x,
        top: item.y,
        width: item.w,
        height: item.h,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined,
        background: "#1e1b4b",
        border: "1px solid #4F46E5",
        borderRadius: 8,
        padding: "10px 12px",
        cursor: isDragging ? "grabbing" : "grab",
        zIndex: isDragging ? 50 : 1,
        userSelect: "none",
        touchAction: "none",
        display: "flex",
        flexDirection: "column",
        gap: 4,
        overflow: "visible",
      }}
    >
      <div
        style={{
          color: "#a5b4fc",
          fontSize: 12,
          fontWeight: 600,
          lineHeight: 1.3,
          overflow: "hidden",
          textOverflow: "ellipsis",
          whiteSpace: "nowrap",
        }}
      >
        {item.label}
      </div>
      <div style={{ color: "#6366f1", fontSize: 10 }}>
        {item.w} × {item.h}
      </div>

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
          borderRight: "2px solid #4F46E5",
          borderBottom: "2px solid #4F46E5",
          borderBottomRightRadius: 6,
        }}
      />
    </div>
  );
}
