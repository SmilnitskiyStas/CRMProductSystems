"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ArrowLeft, Link2, Plus, Save, Trash2, Unlink2 } from "lucide-react";
import {
  DndContext,
  PointerSensor,
  useDraggable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { createSnapModifier } from "@dnd-kit/modifiers";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api";
import { useLocation } from "@/features/locations/hooks/useLocations";
import {
  parseShelfPlan,
  useUpdateZonePosition,
  useZoneItemStatusCounts,
} from "@/features/locations/hooks/useFloorPlan";
import { STATUS_CONFIG, worstStatus } from "@/features/locations/components/FloorPlanCanvas";
import type {
  ShelfItemPlacement,
  ShelfPlanLayout,
  ZoneStatus,
  ZoneStatusCounts,
} from "@/features/locations/types";
import { productsApi } from "@/features/inventory/api/products";
import { useProductsByIds } from "@/features/inventory/hooks/useProducts";
import { ProductSearchPicker } from "@/features/inventory/components/ProductSearchPicker";
import type { Product } from "@/features/inventory/types";

// Shared icon-button look for the per-section link/unlink/delete controls below (TASK-716).
const iconButtonStyle: React.CSSProperties = {
  background: "transparent",
  border: "none",
  color: "#6B7280",
  cursor: "pointer",
  padding: 2,
  flexShrink: 0,
  display: "flex",
  alignItems: "center",
};

// After the layout saves, make sure item_zone_assignments (TASK-714) reflects every product
// placed on this zone's canvas. Fires one assignZone per linked item; a 400 ("already exists" —
// e.g. the product was already tagged via ProductZonesSection, or a previous save) is expected
// and swallowed silently. Deliberately asymmetric: unlinking a product from a shelf box, or
// deleting the box, never auto-removes the zone tag — that stays a manual action from the
// product's own Zones section (confirmed in the approved plan, not a bug to "fix" into symmetry).
async function syncZoneTags(
  plan: ShelfPlanLayout,
  zoneId: string,
  queryClient: QueryClient
): Promise<string | null> {
  const itemIds = Array.from(
    new Set(plan.items.map((i) => i.itemId).filter((id): id is string => Boolean(id)))
  );
  if (itemIds.length === 0) return null;

  const results = await Promise.allSettled(
    itemIds.map((itemId) => productsApi.assignZone(itemId, zoneId))
  );
  for (const itemId of itemIds) {
    queryClient.invalidateQueries({ queryKey: ["products", itemId, "zones"] });
  }

  const unexpected = results.find(
    (r): r is PromiseRejectedResult =>
      r.status === "rejected" && !(r.reason instanceof ApiError && r.reason.status === 400)
  );
  if (!unexpected) return null;
  return unexpected.reason instanceof Error ? unexpected.reason.message : String(unexpected.reason);
}

export default function ShelvesPage() {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  const tFloorPlan = useTranslations("Dashboard.locations.floorPlan");
  const tCommon = useTranslations("Common");
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
  // shelfId of the row whose inline product picker is expanded in the side panel; null = none
  // open. A single value naturally keeps at most one row's picker open at a time (TASK-716).
  const [linkingShelfId, setLinkingShelfId] = useState<string | null>(null);

  useEffect(() => {
    if (zone) {
      setPlan(parseShelfPlan(zone.position));
      setDirty(false);
      setLinkingShelfId(null);
    }
  }, [zone?.id, zone?.position]); // eslint-disable-line react-hooks/exhaustive-deps

  const queryClient = useQueryClient();

  // Resolve display names for every linked product in one batched call (not one query per shelf
  // box — TASK-716 brief).
  const linkedItemIds = useMemo(
    () =>
      Array.from(
        new Set(
          (plan?.items ?? []).map((i) => i.itemId).filter((id): id is string => Boolean(id))
        )
      ),
    [plan]
  );
  const { data: linkedProducts = [] } = useProductsByIds(linkedItemIds);
  const productNameById = useMemo(
    () => new Map(linkedProducts.map((p) => [p.id, p.name])),
    [linkedProducts]
  );

  // Per-product stock-status counts within this zone, for the status dot on linked rows/boxes.
  const { data: itemStatusCounts = new Map<string, ZoneStatusCounts>() } = useZoneItemStatusCounts(
    locationId,
    zoneId
  );

  function patchPlan(fn: (prev: ShelfPlanLayout) => ShelfPlanLayout) {
    setPlan((prev) => (prev ? fn(prev) : prev));
    setDirty(true);
  }

  function handleAddSection() {
    patchPlan((prev) => {
      const n = prev.items.length + 1;
      const newItem: ShelfItemPlacement = {
        shelfId: crypto.randomUUID(),
        label: t("newSectionLabel", { n }),
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

  function handleLinkProduct(shelfId: string, product: Product) {
    patchPlan((prev) => ({
      ...prev,
      items: prev.items.map((i) => (i.shelfId === shelfId ? { ...i, itemId: product.id } : i)),
    }));
    setLinkingShelfId(null);
  }

  function handleUnlinkProduct(shelfId: string) {
    patchPlan((prev) => ({
      ...prev,
      items: prev.items.map((i) => (i.shelfId === shelfId ? { ...i, itemId: null } : i)),
    }));
  }

  function handleSave() {
    if (!plan || !zone) return;
    const layoutToSync = plan;
    updateZonePosition.mutate(
      { zone, shelfLayout: plan },
      {
        onSuccess: async () => {
          setDirty(false);
          toast.success(t("toastSaved"));
          const errorMessage = await syncZoneTags(layoutToSync, zoneId, queryClient);
          if (errorMessage) toast.error(t("zoneSyncError", { message: errorMessage }));
        },
        onError: (e) => toast.error(t("toastError", { message: e.message })),
      }
    );
  }

  if (isLoading || !plan) {
    return (
      <div style={{ padding: "28px 32px", color: "#4B5563", fontSize: 13, textAlign: "center" }}>
        {tCommon("loading")}
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
              {t("backToPlan")}
            </button>
          </div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
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
          {updateZonePosition.isPending ? t("saving") : t("save")}
        </button>
      </div>

      {/* Canvas + panel */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 240px", gap: 20, alignItems: "start" }}>
        {/* Canvas */}
        <ShelfCanvas
          plan={plan}
          onMove={handleMove}
          onResize={handleResize}
          productNames={productNameById}
          itemStatusCounts={itemStatusCounts}
        />

        {/* Side panel */}
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 16 }}>
            <h3 style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, margin: "0 0 12px 0" }}>
              {t("actionsTitle")}
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
                <Plus size={14} /> {t("addSection")}
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
                  {tFloorPlan("addWidth")}
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
                  {tFloorPlan("addHeight")}
                </button>
              </div>
            </div>
          </div>

          <div style={{ background: "#161B26", border: "1px solid #1F2937", borderRadius: 12, padding: 16 }}>
            <h3 style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, margin: "0 0 12px 0" }}>
              {t("sectionsTitle", { count: plan.items.length })}
            </h3>
            {plan.items.length === 0 ? (
              <p style={{ color: "#6B7280", fontSize: 12, margin: 0 }}>{t("noSections")}</p>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {plan.items.map((item) => {
                  const productName = item.itemId ? productNameById.get(item.itemId) : undefined;
                  const status = item.itemId ? worstStatus(itemStatusCounts.get(item.itemId)) : null;
                  const isLinking = linkingShelfId === item.shelfId;
                  return (
                    <div
                      key={item.shelfId}
                      style={{
                        background: "#0B0E14",
                        border: "1px solid #1F2937",
                        borderRadius: 8,
                        padding: "7px 10px",
                      }}
                    >
                      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }}>
                        <div style={{ display: "flex", alignItems: "center", gap: 6, minWidth: 0 }}>
                          {status && (
                            <span
                              style={{
                                width: 6,
                                height: 6,
                                borderRadius: "50%",
                                background: STATUS_CONFIG[status].color,
                                flexShrink: 0,
                              }}
                            />
                          )}
                          <span
                            style={{
                              color: "#E8EDF5",
                              fontSize: 12,
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap",
                            }}
                          >
                            {productName ?? item.label}
                          </span>
                        </div>
                        <div style={{ display: "flex", alignItems: "center", gap: 2, flexShrink: 0 }}>
                          <button
                            type="button"
                            onClick={() => setLinkingShelfId(isLinking ? null : item.shelfId)}
                            title={item.itemId ? t("changeProduct") : t("linkProduct")}
                            style={iconButtonStyle}
                          >
                            <Link2 size={13} />
                          </button>
                          {item.itemId && (
                            <button
                              type="button"
                              onClick={() => handleUnlinkProduct(item.shelfId)}
                              title={t("unlinkProduct")}
                              style={iconButtonStyle}
                            >
                              <Unlink2 size={13} />
                            </button>
                          )}
                          <button
                            type="button"
                            onClick={() => handleDeleteSection(item.shelfId)}
                            style={iconButtonStyle}
                          >
                            <Trash2 size={13} />
                          </button>
                        </div>
                      </div>

                      {isLinking && (
                        <div style={{ marginTop: 8, paddingTop: 8, borderTop: "1px solid #1F2937" }}>
                          <ProductSearchPicker
                            excludeIds={[]}
                            onPick={(product) => handleLinkProduct(item.shelfId, product)}
                          />
                          <button
                            type="button"
                            onClick={() => setLinkingShelfId(null)}
                            style={{
                              marginTop: 6,
                              background: "transparent",
                              border: "none",
                              color: "#6B7280",
                              fontSize: 11,
                              cursor: "pointer",
                              padding: 0,
                            }}
                          >
                            {t("cancelLinking")}
                          </button>
                        </div>
                      )}
                    </div>
                  );
                })}
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
  productNames: Map<string, string>;
  itemStatusCounts: Map<string, ZoneStatusCounts>;
}

function ShelfCanvas({ plan, onMove, onResize, productNames, itemStatusCounts }: ShelfCanvasProps) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
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
              productName={item.itemId ? productNames.get(item.itemId) : undefined}
              status={item.itemId ? worstStatus(itemStatusCounts.get(item.itemId)) : undefined}
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
              {t("canvasEmptyHint")}
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
  productName?: string;
  status?: ZoneStatus | "empty";
}

function ShelfItemBox({ item, grid, onResize, productName, status }: ShelfItemBoxProps) {
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
          paddingRight: status ? 12 : 0,
        }}
      >
        {productName ?? item.label}
      </div>
      <div style={{ color: "#6366f1", fontSize: 10 }}>
        {item.w} × {item.h}
      </div>

      {/* Linked-product status badge (TASK-716) */}
      {status && (
        <div
          style={{
            position: "absolute",
            top: 6,
            right: 6,
            width: 8,
            height: 8,
            borderRadius: "50%",
            background: STATUS_CONFIG[status].color,
            border: "1px solid rgba(0,0,0,0.4)",
          }}
        />
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
          borderRight: "2px solid #4F46E5",
          borderBottom: "2px solid #4F46E5",
          borderBottomRightRadius: 6,
        }}
      />
    </div>
  );
}
