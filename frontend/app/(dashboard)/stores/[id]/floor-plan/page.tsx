"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { toast } from "sonner";
import { Save } from "lucide-react";
import { FloorPlanCanvas } from "@/features/stores/components/FloorPlanCanvas";
import { FloorPlanSidePanel } from "@/features/stores/components/FloorPlanSidePanel";
import { useStore, useStores } from "@/features/stores/hooks/useStores";
import {
  parseFloorPlan,
  useUpdateFloorPlan,
  useZoneStatusCounts,
} from "@/features/stores/hooks/useFloorPlan";
import type { FloorPlanLayout } from "@/features/stores/types";

const DEFAULT_W = 160;
const DEFAULT_H = 100;

export default function FloorPlanPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const storeId = params.id;

  const { data: stores } = useStores();
  const { data: store, isLoading } = useStore(storeId);
  const { data: counts } = useZoneStatusCounts(storeId);
  const updateFloorPlan = useUpdateFloorPlan(storeId);

  const [layout, setLayout] = useState<FloorPlanLayout | null>(null);
  const [selectedZoneId, setSelectedZoneId] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);

  // Initialize local layout once per store load; resets when switching stores
  useEffect(() => {
    if (store) {
      setLayout(parseFloorPlan(store.floorPlan));
      setSelectedZoneId(null);
      setDirty(false);
    }
  }, [store?.id, store?.floorPlan]); // eslint-disable-line react-hooks/exhaustive-deps

  const zones = useMemo(() => store?.zones ?? [], [store]);

  function patchLayout(fn: (prev: FloorPlanLayout) => FloorPlanLayout) {
    setLayout((prev) => (prev ? fn(prev) : prev));
    setDirty(true);
  }

  function handleMove(zoneId: string, x: number, y: number) {
    patchLayout((prev) => ({
      ...prev,
      zones: prev.zones.map((z) => (z.zoneId === zoneId ? { ...z, x, y } : z)),
    }));
  }

  function handleResize(zoneId: string, w: number, h: number) {
    patchLayout((prev) => ({
      ...prev,
      zones: prev.zones.map((z) => (z.zoneId === zoneId ? { ...z, w, h } : z)),
    }));
  }

  function handleAdd(zoneId: string) {
    patchLayout((prev) => {
      // Place on a simple cascade so new zones don't stack exactly on top of each other
      const n = prev.zones.length;
      const x = prev.grid * 2 + (n % 4) * (DEFAULT_W + prev.grid);
      const y = prev.grid * 2 + Math.floor(n / 4) * (DEFAULT_H + prev.grid);
      return { ...prev, zones: [...prev.zones, { zoneId, x, y, w: DEFAULT_W, h: DEFAULT_H }] };
    });
    setSelectedZoneId(zoneId);
  }

  function handleRemove(zoneId: string) {
    patchLayout((prev) => ({ ...prev, zones: prev.zones.filter((z) => z.zoneId !== zoneId) }));
    setSelectedZoneId(null);
  }

  function handleSave() {
    if (!layout) return;
    updateFloorPlan.mutate(layout, {
      onSuccess: () => {
        setDirty(false);
        toast.success("План магазину збережено");
      },
      onError: (e) => toast.error(`Не вдалося зберегти: ${e.message}`),
    });
  }

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Конструктор магазину
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {store?.name ?? "…"} — розташування зон і статуси товарів
          </p>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          {stores && stores.length > 1 && (
            <select
              value={storeId}
              onChange={(e) => router.replace(`/stores/${e.target.value}/floor-plan`)}
              style={{
                background: "#161B26",
                border: "1px solid #1F2937",
                color: "#E8EDF5",
                borderRadius: 8,
                padding: "8px 12px",
                fontSize: 13,
              }}
            >
              {stores.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          )}
          <button
            onClick={handleSave}
            disabled={!dirty || updateFloorPlan.isPending}
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
            {updateFloorPlan.isPending ? "Збереження…" : "Зберегти план"}
          </button>
        </div>
      </div>

      {/* Canvas + side panel */}
      {isLoading || !layout ? (
        <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
          Завантаження…
        </div>
      ) : (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 260px", gap: 20, alignItems: "start" }}>
          <FloorPlanCanvas
            zones={zones}
            layout={layout}
            counts={counts}
            selectedZoneId={selectedZoneId}
            onSelect={setSelectedZoneId}
            onMove={handleMove}
            onResize={handleResize}
          />
          <FloorPlanSidePanel
            zones={zones}
            layout={layout}
            counts={counts}
            selectedZoneId={selectedZoneId}
            onAdd={handleAdd}
            onRemove={handleRemove}
          />
        </div>
      )}
    </div>
  );
}
