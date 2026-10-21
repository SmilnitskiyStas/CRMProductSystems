"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useParams, useRouter } from "next/navigation";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ArrowLeft, Camera, LayoutGrid, Link2, Plus, Save, Trash2 } from "lucide-react";
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
import { invalidateShelfItems, useShelfItems, useShelfScans } from "@/features/locations/hooks/useShelves";
import { shelvesApi } from "@/features/locations/api/shelves";
import type {
  PlanogramEntry,
  ShelfItemPlacement,
  ShelfPlanLayout,
  ShelfScanDto,
  ShelfScanItemStatus,
  ZoneItemSummary,
} from "@/features/locations/types";
import { productsApi } from "@/features/inventory/api/products";
import { useProductsByIds } from "@/features/inventory/hooks/useProducts";
import { ShelfProductModal } from "@/features/locations/components/ShelfProductModal";
import { ShelfInsightPanel } from "@/features/locations/components/ShelfInsightPanel";
import { ShelfScanPanel, SCAN_STATUS_COLOR } from "@/features/locations/components/ShelfScanPanel";

const DEFAULT_SECTION_W = 200;
const DEFAULT_SECTION_H = 80;
const PLACEMENT_COLUMNS = 4;

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

// Reconciliation pass on top of ShelfProductModal's own immediate assignZone call (fired the
// moment a link is confirmed, so "this product is in this zone" is already true elsewhere —
// catalog Zones column, the product's own Zones section — without waiting for this page's Save).
// This still runs after every layout save as a belt-and-suspenders catch-up: it re-asserts
// item_zone_assignments (TASK-714) for every product currently placed on the canvas, so a link
// whose immediate call failed (a network blip) gets fixed the next time the user saves anyway.
// Fires one assignZone per linked item; a 400 ("already exists") is expected and swallowed
// silently either way. Deliberately asymmetric: unlinking a product from a shelf box, or
// deleting the box, never auto-removes the zone tag — that stays a manual action from the
// product's own Zones section (confirmed in the approved plan, not a bug to "fix" into symmetry).
async function syncZoneTags(
  allItemIds: string[],
  zoneId: string,
  queryClient: QueryClient
): Promise<string | null> {
  const itemIds = Array.from(new Set(allItemIds));
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
  // shelfId of the section whose detail/pick modal is open; null = none open. A single value
  // naturally keeps at most one section's modal open at a time.
  const [modalShelfId, setModalShelfId] = useState<string | null>(null);
  // Relational planogram (TASK-726): shelfId -> items with expected facings. The JSON `itemId`
  // on each section is kept as the first item for backward compatibility only.
  const [planogram, setPlanogram] = useState<Record<string, PlanogramEntry[]>>({});
  const serverPlanogramRef = useRef<Record<string, PlanogramEntry[]>>({});
  const deletedShelfIdsRef = useRef<Set<string>>(new Set());
  const planogramInitForRef = useRef<string | null>(null);
  const [insightShelfId, setInsightShelfId] = useState<string | null>(null);
  const [scanOpen, setScanOpen] = useState(false);
  const [activeScanId, setActiveScanId] = useState<string | null>(null);

  const shelfItemsQuery = useShelfItems(locationId, zoneId);
  const { data: scans = [] } = useShelfScans(locationId, zoneId);
  const activeScan: ShelfScanDto | null =
    scans.find((s) => s.id === activeScanId) ?? scans[0] ?? null;

  useEffect(() => {
    if (zone) {
      setPlan(parseShelfPlan(zone.position));
      setDirty(false);
      setModalShelfId(null);
    }
  }, [zone?.id, zone?.position]); // eslint-disable-line react-hooks/exhaustive-deps

  const queryClient = useQueryClient();

  // Load the planogram once per zone: server rows win; sections without rows fall back to the
  // single `itemId` stored in the layout JSON (pre-planogram data).
  useEffect(() => {
    if (!zone || planogramInitForRef.current === zone.id) return;
    if (!shelfItemsQuery.isSuccess && !shelfItemsQuery.isError) return;
    const server: Record<string, PlanogramEntry[]> = {};
    for (const row of shelfItemsQuery.data ?? []) {
      (server[row.shelfId] ??= []).push({ itemId: row.itemId, expectedFacings: row.expectedFacings });
    }
    const merged: Record<string, PlanogramEntry[]> = { ...server };
    for (const it of parseShelfPlan(zone.position).items) {
      if (!merged[it.shelfId] && it.itemId) merged[it.shelfId] = [{ itemId: it.itemId, expectedFacings: 1 }];
    }
    serverPlanogramRef.current = server;
    planogramInitForRef.current = zone.id;
    setPlanogram(merged);
  }, [zone, shelfItemsQuery.isSuccess, shelfItemsQuery.isError, shelfItemsQuery.data]);

  // Resolve display names for every linked product in one batched call (not one query per shelf
  // box — TASK-716 brief).
  const linkedItemIds = useMemo(
    () =>
      Array.from(new Set(Object.values(planogram).flatMap((es) => es.map((e) => e.itemId)))),
    [planogram]
  );
  const { data: linkedProducts = [] } = useProductsByIds(linkedItemIds);
  const productNameById = useMemo(
    () => new Map(linkedProducts.map((p) => [p.id, p.name])),
    [linkedProducts]
  );
  // Unit label for the hover popover's quantity line (e.g. "12 шт").
  const productUnitById = useMemo(
    () => new Map(linkedProducts.map((p) => [p.id, p.unit])),
    [linkedProducts]
  );

  // Per-product stock summary within this zone — status dot on linked rows/boxes, plus the
  // canvas box's hover popover (quantity + nearest expiry).
  const { data: itemStatusCounts = new Map<string, ZoneItemSummary>() } = useZoneItemStatusCounts(
    locationId,
    zoneId
  );

  function patchPlan(fn: (prev: ShelfPlanLayout) => ShelfPlanLayout) {
    setPlan((prev) => (prev ? fn(prev) : prev));
    setDirty(true);
  }

  // New sections start unplaced (not drawn on the canvas) — see handlePlaceSection below for
  // why, and ShelfItemPlacement.placed for the flag itself.
  function handleAddSection() {
    patchPlan((prev) => {
      const n = prev.items.length + 1;
      const newItem: ShelfItemPlacement = {
        shelfId: crypto.randomUUID(),
        label: t("newSectionLabel", { n }),
        x: 0,
        y: 0,
        w: DEFAULT_SECTION_W,
        h: DEFAULT_SECTION_H,
        placed: false,
      };
      return { ...prev, items: [...prev.items, newItem] };
    });
  }

  // Places an unplaced section on the canvas at the next free grid cell — a 4-column, row-by-row
  // layout (same pattern as the location floor-plan editor's handleAdd), instead of the old
  // behavior of always stacking every new section below the last one regardless of where the
  // user had since dragged existing sections, which pushed the list further and further down.
  function handlePlaceSection(shelfId: string) {
    patchPlan((prev) => {
      const placedCount = prev.items.filter((i) => i.placed !== false).length;
      const x = prev.grid * 2 + (placedCount % PLACEMENT_COLUMNS) * (DEFAULT_SECTION_W + prev.grid);
      const y =
        prev.grid * 2 + Math.floor(placedCount / PLACEMENT_COLUMNS) * (DEFAULT_SECTION_H + prev.grid);
      return {
        ...prev,
        items: prev.items.map((i) => (i.shelfId === shelfId ? { ...i, placed: true, x, y } : i)),
      };
    });
  }

  function handleDeleteSection(shelfId: string) {
    deletedShelfIdsRef.current.add(shelfId);
    setPlanogram((prev) => {
      const { [shelfId]: _removed, ...rest } = prev;
      void _removed;
      return rest;
    });
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

  function handlePlanogramChange(shelfId: string, entries: PlanogramEntry[]) {
    setPlanogram((prev) => ({ ...prev, [shelfId]: entries }));
    setDirty(true);
  }

  async function savePlanogram(currentPlan: ShelfPlanLayout): Promise<string | null> {
    const ids = new Set(currentPlan.items.map((i) => i.shelfId));
    const targets = [...ids, ...deletedShelfIdsRef.current];
    const changed = targets.filter((id) => {
      const next = ids.has(id) ? planogram[id] ?? [] : [];
      const prev = serverPlanogramRef.current[id] ?? [];
      return JSON.stringify(next) !== JSON.stringify(prev);
    });
    const results = await Promise.allSettled(
      changed.map((id) =>
        shelvesApi.setShelfItems(locationId, zoneId, id, ids.has(id) ? planogram[id] ?? [] : [])
      )
    );
    let error: string | null = null;
    results.forEach((r, i) => {
      const id = changed[i];
      if (r.status === "fulfilled") {
        if (r.value.length) {
          serverPlanogramRef.current[id] = r.value.map((v) => ({
            itemId: v.itemId,
            expectedFacings: v.expectedFacings,
          }));
        } else {
          delete serverPlanogramRef.current[id];
        }
        deletedShelfIdsRef.current.delete(id);
      } else {
        error = r.reason instanceof Error ? r.reason.message : String(r.reason);
      }
    });
    invalidateShelfItems(queryClient, locationId, zoneId);
    return error;
  }

  function handleSave() {
    if (!plan || !zone) return;
    // Keep the layout JSON `itemId` = first planogram item (backward compatibility).
    const layoutToSave: ShelfPlanLayout = {
      ...plan,
      items: plan.items.map((i) => ({ ...i, itemId: planogram[i.shelfId]?.[0]?.itemId ?? null })),
    };
    const syncIds = Object.values(planogram).flatMap((es) => es.map((e) => e.itemId));
    updateZonePosition.mutate(
      { zone, shelfLayout: layoutToSave },
      {
        onSuccess: async () => {
          setDirty(false);
          const planogramError = await savePlanogram(layoutToSave);
          if (planogramError) {
            setDirty(true);
            toast.error(t("planogramSaveError", { message: planogramError }));
          } else {
            toast.success(t("toastSaved"));
          }
          const errorMessage = await syncZoneTags(syncIds, zoneId, queryClient);
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

  // Per-section result of the active scan: worst of missing/low among that section's planogram
  // items. A scan limited to one section only colours that section.
  const scanStatusByShelf = new Map<string, ShelfScanItemStatus>();
  if (activeScan && activeScan.status !== "failed") {
    const byItem = new Map(activeScan.items.map((it) => [it.itemId, it.status]));
    for (const sec of plan.items) {
      if (activeScan.shelfId && activeScan.shelfId !== sec.shelfId) continue;
      let worst: ShelfScanItemStatus | null = null;
      for (const e of planogram[sec.shelfId] ?? []) {
        const st = byItem.get(e.itemId);
        if (st === "missing") worst = "missing";
        else if (st === "low" && worst !== "missing") worst = "low";
      }
      if (worst) scanStatusByShelf.set(sec.shelfId, worst);
    }
  }
  const sectionLabelOf = (item: ShelfItemPlacement) => {
    const first = planogram[item.shelfId]?.[0];
    return (first && productNameById.get(first.itemId)) ?? item.label;
  };

  const modalItem = modalShelfId ? plan.items.find((i) => i.shelfId === modalShelfId) ?? null : null;
  const insightSection = insightShelfId ? plan.items.find((i) => i.shelfId === insightShelfId) ?? null : null;

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

        <div style={{ display: "flex", gap: 10 }}>
        <button
          onClick={() => setScanOpen(true)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            background: "#0d1f3a",
            border: "1px solid #1D4ED8",
            color: "#60A5FA",
            borderRadius: 8,
            padding: "9px 16px",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Camera size={15} />
          {t("scanButton")}
        </button>
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
      </div>

      {/* Canvas + panel */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 240px", gap: 20, alignItems: "start" }}>
        {/* Canvas — only sections placed on it (see ShelfItemPlacement.placed); a freshly
            created section stays list-only until the user places it. */}
        <ShelfCanvas
          plan={{ ...plan, items: plan.items.filter((i) => i.placed !== false) }}
          onMove={handleMove}
          onResize={handleResize}
          productNames={productNameById}
          productUnits={productUnitById}
          itemStatusCounts={itemStatusCounts}
          planogram={planogram}
          scanStatusByShelf={scanStatusByShelf}
          onRequestLink={(shelfId) =>
            // Sections with products open the insight panel; empty ones go straight to the picker.
            (planogram[shelfId]?.length ?? 0) > 0 ? setInsightShelfId(shelfId) : setModalShelfId(shelfId)
          }
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
                  const entries = planogram[item.shelfId] ?? [];
                  const productName = entries[0] ? productNameById.get(entries[0].itemId) : undefined;
                  const status = entries[0] ? worstStatus(itemStatusCounts.get(entries[0].itemId)?.counts) : null;
                  const scanSt = scanStatusByShelf.get(item.shelfId);
                  const notPlaced = item.placed === false;
                  return (
                    <div
                      key={item.shelfId}
                      style={{
                        background: "#0B0E14",
                        border: `1px solid ${scanSt ? SCAN_STATUS_COLOR[scanSt].border : notPlaced ? "#854D0E" : "#1F2937"}`,
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
                            {entries.length > 1 && (
                              <span style={{ color: "#6B7280" }}> +{entries.length - 1}</span>
                            )}
                          </span>
                        </div>
                        <div style={{ display: "flex", alignItems: "center", gap: 2, flexShrink: 0 }}>
                          {notPlaced && (
                            <button
                              type="button"
                              onClick={() => handlePlaceSection(item.shelfId)}
                              title={t("placeOnCanvas")}
                              style={iconButtonStyle}
                            >
                              <LayoutGrid size={13} color="#FCD34D" />
                            </button>
                          )}
                          <button
                            type="button"
                            onClick={() => setModalShelfId(item.shelfId)}
                            title={entries.length ? t("changeProduct") : t("linkProduct")}
                            style={iconButtonStyle}
                          >
                            <Link2 size={13} />
                          </button>
                          <button
                            type="button"
                            onClick={() => handleDeleteSection(item.shelfId)}
                            style={iconButtonStyle}
                          >
                            <Trash2 size={13} />
                          </button>
                        </div>
                      </div>
                      {notPlaced && (
                        <div style={{ color: "#FCD34D", fontSize: 11, marginTop: 4 }}>
                          {t("notPlacedBadge")}
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

      {modalItem && (
        <ShelfProductModal
          key={modalItem.shelfId}
          onClose={() => setModalShelfId(null)}
          storeId={locationId}
          zoneId={zoneId}
          sectionLabel={sectionLabelOf(modalItem)}
          entries={planogram[modalItem.shelfId] ?? []}
          onChange={(entries) => handlePlanogramChange(modalItem.shelfId, entries)}
        />
      )}

      {insightSection && (
        <ShelfInsightPanel
          key={insightSection.shelfId}
          onClose={() => setInsightShelfId(null)}
          onEditProducts={() => {
            setInsightShelfId(null);
            setModalShelfId(insightSection.shelfId);
          }}
          locationId={locationId}
          zoneId={zoneId}
          shelfId={insightSection.shelfId}
          sectionLabel={sectionLabelOf(insightSection)}
        />
      )}

      {scanOpen && (
        <ShelfScanPanel
          onClose={() => setScanOpen(false)}
          locationId={locationId}
          zoneId={zoneId}
          sections={plan.items.map((i) => ({ shelfId: i.shelfId, label: sectionLabelOf(i) }))}
          scans={scans}
          activeScan={activeScan}
          onSelectScan={setActiveScanId}
        />
      )}
    </div>
  );
}

interface ShelfCanvasProps {
  plan: ShelfPlanLayout;
  onMove: (shelfId: string, x: number, y: number) => void;
  onResize: (shelfId: string, w: number, h: number) => void;
  productNames: Map<string, string>;
  productUnits: Map<string, string>;
  itemStatusCounts: Map<string, ZoneItemSummary>;
  planogram: Record<string, PlanogramEntry[]>;
  scanStatusByShelf: Map<string, ShelfScanItemStatus>;
  onRequestLink: (shelfId: string) => void;
}

function ShelfCanvas({
  plan,
  onMove,
  onResize,
  productNames,
  productUnits,
  itemStatusCounts,
  planogram,
  scanStatusByShelf,
  onRequestLink,
}: ShelfCanvasProps) {
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
              onRequestLink={() => onRequestLink(item.shelfId)}
              entries={planogram[item.shelfId] ?? []}
              productNames={productNames}
              productUnits={productUnits}
              itemStatusCounts={itemStatusCounts}
              scanStatus={scanStatusByShelf.get(item.shelfId)}
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

// Days-to-expiry color, same thresholds as features/shelf/components/StockTable.tsx's
// getDaysColor (kept as a local copy — no shared "expiry formatting" module today, same
// posture that file's own comments already document for its sibling helpers).
function daysColor(days: number): string {
  if (days <= 0) return STATUS_CONFIG.expired.color;
  if (days <= 3) return STATUS_CONFIG.critical.color;
  if (days <= 7) return STATUS_CONFIG.warning.color;
  return "#6B7280";
}

function formatExpiryDate(dateStr: string): string {
  const [y, m, d] = dateStr.split("-");
  return `${d}.${m}.${y}`;
}

interface ShelfItemBoxProps {
  item: ShelfItemPlacement;
  grid: number;
  onResize: (shelfId: string, w: number, h: number) => void;
  onRequestLink: () => void;
  entries: PlanogramEntry[];
  productNames: Map<string, string>;
  productUnits: Map<string, string>;
  itemStatusCounts: Map<string, ZoneItemSummary>;
  scanStatus?: ShelfScanItemStatus;
}

interface PopPos {
  top: number;
  left: number;
}

function ShelfItemBox({
  item,
  grid,
  onResize,
  onRequestLink,
  entries,
  productNames,
  productUnits,
  itemStatusCounts,
  scanStatus,
}: ShelfItemBoxProps) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  const tZoneStatus = useTranslations("Dashboard.locations.zoneStatus");
  const tOverdue = useTranslations("Dashboard.shelf.stockTable");
  const productName = entries[0] ? productNames.get(entries[0].itemId) : undefined;
  // Worst batch status across all items of the section.
  const totalCounts = { safe: 0, warning: 0, critical: 0, expired: 0 };
  for (const e of entries) {
    const c = itemStatusCounts.get(e.itemId)?.counts;
    if (!c) continue;
    totalCounts.safe += c.safe;
    totalCounts.warning += c.warning;
    totalCounts.critical += c.critical;
    totalCounts.expired += c.expired;
  }
  const status = entries.length ? worstStatus(totalCounts) : undefined;
  const scanColors = scanStatus ? SCAN_STATUS_COLOR[scanStatus] : null;

  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: item.shelfId,
  });

  // Guards the click handler below against firing right after a real drag (dnd-kit's
  // PointerSensor only "activates" a drag past a 4px movement threshold, but the browser's
  // native click still fires on pointerup regardless of the movement in between — without
  // this, every drag-to-reposition would also pop the link picker open).
  const didDragRef = useRef(false);
  useEffect(() => {
    if (isDragging) didDragRef.current = true;
  }, [isDragging]);

  function handleClick() {
    if (didDragRef.current) {
      didDragRef.current = false;
      return;
    }
    onRequestLink();
  }

  const boxRef = useRef<HTMLDivElement | null>(null);
  const [hovered, setHovered] = useState(false);
  const [pos, setPos] = useState<PopPos | null>(null);
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const showPopover = useCallback(() => {
    if (hideTimer.current) clearTimeout(hideTimer.current);
    if (!boxRef.current) return;
    const rect = boxRef.current.getBoundingClientRect();
    setPos({ top: rect.top + window.scrollY, left: rect.right + 8 + window.scrollX });
    setHovered(true);
  }, []);

  const hidePopoverSoon = useCallback(() => {
    if (hideTimer.current) clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => setHovered(false), 100);
  }, []);

  useEffect(() => () => {
    if (hideTimer.current) clearTimeout(hideTimer.current);
  }, []);

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
      ref={(node) => {
        setNodeRef(node);
        boxRef.current = node;
      }}
      {...listeners}
      {...attributes}
      onClick={handleClick}
      onMouseEnter={showPopover}
      onMouseLeave={hidePopoverSoon}
      title={entries.length ? t("clickToChange") : t("clickToAdd")}
      style={{
        position: "absolute",
        left: item.x,
        top: item.y,
        width: item.w,
        height: item.h,
        transform: transform ? `translate(${transform.x}px, ${transform.y}px)` : undefined,
        background: scanColors ? scanColors.bg : "#1e1b4b",
        border: `${scanColors ? 2 : 1}px solid ${scanColors ? scanColors.fg : "#4F46E5"}`,
        borderRadius: 8,
        padding: "10px 12px",
        cursor: isDragging ? "grabbing" : "pointer",
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
        {entries.length > 1 && ` +${entries.length - 1}`}
      </div>
      <div style={{ color: "#6366f1", fontSize: 10 }}>
        {item.w} × {item.h}
      </div>
      {scanColors && scanStatus && (
        <div style={{ color: scanColors.fg, fontSize: 10, fontWeight: 700 }}>
          {t(`scanStatus_${scanStatus}`)}
        </div>
      )}

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

      {/* Hover popover — product detail (stock quantity, nearest expiry, batch-status
          breakdown) when linked; a plain "click to add" hint otherwise. */}
      {hovered &&
        pos &&
        createPortal(
          <div
            onMouseEnter={showPopover}
            onMouseLeave={hidePopoverSoon}
            style={{
              position: "absolute",
              top: pos.top,
              left: pos.left,
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.6)",
              zIndex: 9999,
              padding: "10px 12px",
              minWidth: 200,
              maxWidth: 260,
            }}
          >
            {entries.length > 0 ? (
              entries.map((e, idx) => {
                const summary = itemStatusCounts.get(e.itemId);
                return (
                  <div
                    key={e.itemId}
                    style={{ paddingTop: idx ? 8 : 0, marginTop: idx ? 8 : 0, borderTop: idx ? "1px solid #1F2937" : undefined }}
                  >
                    <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                      {productNames.get(e.itemId) ?? "…"}{" "}
                      <span style={{ color: "#6B7280", fontWeight: 400 }}>
                        {t("facingsShort", { n: e.expectedFacings })}
                      </span>
                    </div>
                    {summary ? (
                      <>
                        <div style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 4 }}>
                          {t("hoverQuantity", { qty: summary.totalQuantity, unit: productUnits.get(e.itemId) ?? "" })}
                        </div>
                        {summary.nearestExpiryDate && summary.nearestExpiryDays !== null && (
                          <div style={{ fontSize: 12, marginBottom: 4, color: "#9CA3AF" }}>
                            {t("hoverExpiry", { date: formatExpiryDate(summary.nearestExpiryDate) })}{" "}
                            <span style={{ color: daysColor(summary.nearestExpiryDays), fontWeight: 600 }}>
                              (
                              {summary.nearestExpiryDays <= 0
                                ? tOverdue("overdue", { days: Math.abs(summary.nearestExpiryDays) })
                                : summary.nearestExpiryDays}
                              )
                            </span>
                          </div>
                        )}
                        <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
                          {(["expired", "critical", "warning", "safe"] as const)
                            .filter((s) => summary.counts[s] > 0)
                            .map((s) => (
                              <div key={s} style={{ display: "flex", justifyContent: "space-between", gap: 12, fontSize: 11 }}>
                                <span style={{ color: STATUS_CONFIG[s].color }}>{tZoneStatus(s)}</span>
                                <span style={{ color: "#9CA3AF", fontFamily: "monospace" }}>{summary.counts[s]}</span>
                              </div>
                            ))}
                        </div>
                      </>
                    ) : null}
                  </div>
                );
              })
            ) : (
              <div style={{ color: "#6B7280", fontSize: 12 }}>{t("clickToAdd")}</div>
            )}
          </div>,
          document.body,
        )}
    </div>
  );
}
