"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { toast } from "sonner";
import { PanelRightClose, PanelRightOpen, Save } from "lucide-react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { filterInputStyle } from "@/components/ui/fieldStyles";
import { FloorPlanCanvas } from "@/features/locations/components/FloorPlanCanvas";
import { FloorPlanSidePanel } from "@/features/locations/components/FloorPlanSidePanel";
import { ZoneDialog } from "@/features/locations/components/ZoneDialog";
import { useLocation, useLocations } from "@/features/locations/hooks/useLocations";
import {
  parseFloorPlan,
  useUpdateFloorPlan,
  useUploadFloorPlanBackground,
  useZoneStatusCounts,
} from "@/features/locations/hooks/useFloorPlan";
import { PLAN_MAX_BYTES, prepareFloorPlanFile } from "@/features/locations/lib/floorPlanBackground";
import type { FloorPlanLayout, LocationZoneDto } from "@/features/locations/types";

const DEFAULT_W = 160;
const DEFAULT_H = 100;
const COMPACT_W = 60;
const COMPACT_H = 40;

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
  const uploadBackground = useUploadFloorPlanBackground(locationId);
  const [preparingPlan, setPreparingPlan] = useState(false);
  const [zoneDialogType, setZoneDialogType] = useState<string | undefined>(undefined);

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
      // Over a real plan, new objects start small so they can be dragged onto the drawn shapes.
      const w = prev.background ? COMPACT_W : DEFAULT_W;
      const h = prev.background ? COMPACT_H : DEFAULT_H;
      const x = prev.grid * 2 + (n % 4) * (w + prev.grid);
      const y = prev.grid * 2 + Math.floor(n / 4) * (h + prev.grid);
      return { ...prev, zones: [...prev.zones, { zoneId, x, y, w, h }] };
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

  async function handleUploadBackground(file: File) {
    if (file.size > PLAN_MAX_BYTES * 2) {
      toast.error(t("backgroundTooBig"));
      return;
    }
    setPreparingPlan(true);
    try {
      const prepared = await prepareFloorPlanFile(file);
      if (prepared.blob.size > PLAN_MAX_BYTES) {
        toast.error(t("backgroundTooBig"));
        return;
      }
      const { url } = await uploadBackground.mutateAsync({
        file: prepared.blob,
        fileName: prepared.fileName,
      });
      patchLayout((prev) => {
        // Adapt canvas height to the image aspect ratio (width kept), snapped to the grid.
        const h = Math.round((prev.canvasW * prepared.naturalH) / prepared.naturalW / prev.grid) * prev.grid;
        return {
          ...prev,
          canvasH: Math.max(prev.grid * 10, h),
          background: {
            url,
            opacity: prev.background?.opacity ?? 0.6,
            naturalW: prepared.naturalW,
            naturalH: prepared.naturalH,
          },
        };
      });
      toast.success(t("backgroundUploaded"));
    } catch (e) {
      toast.error(t("backgroundError", { message: e instanceof Error ? e.message : String(e) }));
    } finally {
      setPreparingPlan(false);
    }
  }

  function handleBackgroundOpacity(opacity: number) {
    patchLayout((prev) => (prev.background ? { ...prev, background: { ...prev.background, opacity } } : prev));
  }

  function handleRemoveBackground() {
    patchLayout((prev) => ({ ...prev, background: null }));
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
    <PageContainer>
      <PageHeader
        title={t("title")}
        subtitle={
          <>
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
          </>
        }
        actions={
          <>
            {locations && locations.length > 1 && (
              <select
                value={locationId}
                onChange={(e) => router.replace(`/locations/${e.target.value}/floor-plan`)}
                style={filterInputStyle}
              >
                {locations.map((loc) => (
                  <option key={loc.id} value={loc.id}>
                    {loc.name}
                  </option>
                ))}
              </select>
            )}
            <Btn
              variant="ghost"
              icon={panelCollapsed ? <PanelRightOpen size={15} /> : <PanelRightClose size={15} />}
              onClick={() => setPanelCollapsed((v) => !v)}
              title={panelCollapsed ? tFloorPlan("showPanel") : tFloorPlan("hidePanel")}
            >
              {panelCollapsed ? tFloorPlan("showPanel") : tFloorPlan("hidePanel")}
            </Btn>
            <Btn
              icon={<Save size={15} />}
              onClick={handleSave}
              disabled={!dirty || updateFloorPlan.isPending}
            >
              {updateFloorPlan.isPending ? t("saving") : t("saveButton")}
            </Btn>
          </>
        }
      />

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
              onCreateZone={(type) => {
                setZoneDialogType(type);
                setShowZoneDialog(true);
              }}
              onUploadBackground={handleUploadBackground}
              onBackgroundOpacity={handleBackgroundOpacity}
              onRemoveBackground={handleRemoveBackground}
              uploadingBackground={preparingPlan || uploadBackground.isPending}
              onOpenShelves={handleOpenShelves}
              onResizeCanvas={handleResizeCanvas}
            />
          )}
        </div>
      )}

      {showZoneDialog && (
        <ZoneDialog
          locationId={locationId}
          initialType={zoneDialogType}
          onCreated={handleZoneCreated}
          onClose={() => setShowZoneDialog(false)}
        />
      )}
    </PageContainer>
  );
}
