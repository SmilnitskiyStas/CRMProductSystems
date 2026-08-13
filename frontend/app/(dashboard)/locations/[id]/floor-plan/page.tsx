"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { toast } from "sonner";
import { PanelRightClose, PanelRightOpen, Save } from "lucide-react";
import { useTranslations } from "next-intl";
import { FloorPlanCanvas } from "@/features/locations/components/FloorPlanCanvas";
import { FloorPlanSidePanel } from "@/features/locations/components/FloorPlanSidePanel";
import { ZoneDialog } from "@/features/locations/components/ZoneDialog";
import { useLocation, useLocations } from "@/features/locations/hooks/useLocations";
import {
  parseFloorPlan,
  useUpdateFloorPlan,
  useZoneStatusCounts,
} from "@/features/locations/hooks/useFloorPlan";
import type { FloorPlanLayout, LocationZoneDto } from "@/features/locations/types";

const DEFAULT_W = 160;
const DEFAULT_H = 100;

export default function FloorPlanPage() {
  const t = useTranslations("Dashboard.locations.floorPlanPage");
  const tFloorPlan = useTranslations("Dashboard.locations.floorPlan");
  const tTypes = useTranslations("Dashboard.locations.types");
  const tCommon = useTranslations("Common");
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const locationId = params.id;

  const { data: locations } = useLocations();
  const { data: location, isLoading } = useLocation(locationId);
  const { data: counts } = useZoneStatusCounts(locationId);
  const updateFloorPlan = useUpdateFloorPlan(locationId);

  const [layout, setLayout] = useState<FloorPlanLayout | null>(null);
  const [selectedZoneId, setSelectedZoneId] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [showZoneDialog, setShowZoneDialog] = useState(false);
  const [panelCollapsed, setPanelCollapsed] = useState(false);

  useEffect(() => {
    if (location) {
      setLayout(parseFloorPlan(location.floorPlan));
      setSelectedZoneId(null);
      setDirty(false);
    }
  }, [location?.id, location?.floorPlan]); // eslint-disable-line react-hooks/exhaustive-deps

  const zones = useMemo(() => location?.zones ?? [], [location]);

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

  function handleResizeCanvas(w: number, h: number) {
    patchLayout((prev) => ({ ...prev, canvasW: w, canvasH: h }));
  }

  function handleZoneCreated(zone: LocationZoneDto) {
    setShowZoneDialog(false);
    handleAdd(zone.id);
  }

  function handleOpenShelves(zoneId: string) {
    router.push(`/locations/${locationId}/zones/${zoneId}/shelves`);
  }

  function handleSave() {
    if (!layout) return;
    updateFloorPlan.mutate(layout, {
      onSuccess: () => {
        setDirty(false);
        toast.success(t("toastSaved"));
      },
      onError: (e) => toast.error(t("toastError", { message: e.message })),
    });
  }

  const locationTypeLabel = location?.locationType ? tTypes(location.locationType) : null;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {location?.name ?? "…"}
            {locationTypeLabel && (
              <span
                style={{
                  display: "inline-block",
                  marginLeft: 8,
                  background: "#1D2D4A",
                  color: "#60A5FA",
                  borderRadius: 6,
                  padding: "1px 8px",
                  fontSize: 11,
                  fontWeight: 600,
                  verticalAlign: "middle",
                }}
              >
                {locationTypeLabel}
              </span>
            )}
            {" "}— {t("subtitleSuffix")}
          </p>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          {locations && locations.length > 1 && (
            <select
              value={locationId}
              onChange={(e) => router.replace(`/locations/${e.target.value}/floor-plan`)}
              style={{
                background: "#161B26",
                border: "1px solid #1F2937",
                color: "#E8EDF5",
                borderRadius: 8,
                padding: "8px 12px",
                fontSize: 13,
              }}
            >
              {locations.map((loc) => (
                <option key={loc.id} value={loc.id}>
                  {loc.name}
                </option>
              ))}
            </select>
          )}
          <button
            onClick={() => setPanelCollapsed((v) => !v)}
            title={panelCollapsed ? tFloorPlan("showPanel") : tFloorPlan("hidePanel")}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              background: "#161B26",
              border: "1px solid #1F2937",
              color: "#9CA3AF",
              borderRadius: 8,
              padding: "9px 16px",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            {panelCollapsed ? <PanelRightOpen size={15} /> : <PanelRightClose size={15} />}
            {panelCollapsed ? tFloorPlan("showPanel") : tFloorPlan("hidePanel")}
          </button>
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
            {updateFloorPlan.isPending ? t("saving") : t("saveButton")}
          </button>
        </div>
      </div>

      {/* Canvas + side panel */}
      {isLoading || !layout ? (
        <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
          {tCommon("loading")}
        </div>
      ) : (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: panelCollapsed ? "1fr" : "1fr 260px",
            gap: 20,
            alignItems: "start",
          }}
        >
          <FloorPlanCanvas
            key={locationId}
            zones={zones}
            layout={layout}
            counts={counts}
            selectedZoneId={selectedZoneId}
            onSelect={setSelectedZoneId}
            onMove={handleMove}
            onResize={handleResize}
          />
          {!panelCollapsed && (
            <FloorPlanSidePanel
              zones={zones}
              layout={layout}
              counts={counts}
              selectedZoneId={selectedZoneId}
              onAdd={handleAdd}
              onRemove={handleRemove}
              onCreateZone={() => setShowZoneDialog(true)}
              onOpenShelves={handleOpenShelves}
              onResizeCanvas={handleResizeCanvas}
            />
          )}
        </div>
      )}

      {showZoneDialog && (
        <ZoneDialog
          locationId={locationId}
          onCreated={handleZoneCreated}
          onClose={() => setShowZoneDialog(false)}
        />
      )}
    </div>
  );
}
