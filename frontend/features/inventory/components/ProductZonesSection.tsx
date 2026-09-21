"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useAssignItemZone, useItemZones, useUnassignItemZone } from "../hooks/useProducts";

interface Props {
  productId: string;
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 5,
};

const emptyStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  margin: 0,
};

const errorBoxStyle: React.CSSProperties = {
  background: "#2D0F0F",
  border: "1px solid #7F1D1D",
  borderRadius: 8,
  color: "#F87171",
  fontSize: 12,
  padding: "8px 12px",
};

// TASK-715 — product↔zone tagging inside ProductForm's edit mode. Backend (TASK-714) is a
// plain many-to-many: GET/POST/DELETE /api/items/{id}/zones. useLocations() already nests each
// location's zones (LocationZoneDto[]), so no extra endpoint is needed to drive the cascade.
export function ProductZonesSection({ productId }: Props) {
  const t = useTranslations("Dashboard.inventory.itemZones");
  const tZoneTypes = useTranslations("Dashboard.locations.zoneTypes");

  const { data: locations = [], isLoading: locationsLoading } = useLocations();
  const { data: zones = [], isLoading: zonesLoading } = useItemZones(productId);
  const assignZone = useAssignItemZone(productId);
  const unassignZone = useUnassignItemZone(productId);

  const [selectedLocationId, setSelectedLocationId] = useState("");
  const [selectedZoneId, setSelectedZoneId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [removingZoneId, setRemovingZoneId] = useState<string | null>(null);

  const activeLocations = locations.filter((l) => l.isActive);
  // Already-tagged zones are excluded from the picker so a normal add can't hit the backend's
  // "already exists" 400 — the catch below is only a safety net for a concurrent-tab race.
  const assignedZoneIds = new Set(zones.map((z) => z.zoneId));
  const selectedLocation = activeLocations.find((l) => l.id === selectedLocationId);
  const availableZones = (selectedLocation?.zones ?? []).filter(
    (z) => z.isActive && !assignedZoneIds.has(z.id),
  );

  function handleLocationChange(id: string) {
    setSelectedLocationId(id);
    setSelectedZoneId(""); // cascade: switching location invalidates the picked zone
    setError(null);
  }

  async function handleAdd() {
    if (!selectedZoneId) return;
    setError(null);
    try {
      await assignZone.mutateAsync(selectedZoneId);
      setSelectedZoneId(""); // keep the location selected so several zones can be added in a row
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("addError"));
    }
  }

  async function handleRemove(zoneId: string) {
    setError(null);
    setRemovingZoneId(zoneId);
    try {
      await unassignZone.mutateAsync(zoneId);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("removeError"));
    } finally {
      setRemovingZoneId(null);
    }
  }

  if (locationsLoading || zonesLoading) {
    return <p style={emptyStyle}>{t("loading")}</p>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      {zones.length === 0 ? (
        <p style={emptyStyle}>{t("empty")}</p>
      ) : (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
          {zones.map((z) => {
            const removing = removingZoneId === z.zoneId;
            return (
              <span
                key={z.id}
                style={{
                  display: "flex", alignItems: "center", gap: 4,
                  background: "#0F1F3D",
                  border: "1px solid #1E3A5F",
                  borderRadius: 6, padding: "3px 8px",
                  color: "#93C5FD", fontSize: 12,
                  opacity: removing ? 0.5 : 1,
                }}
              >
                {z.locationName} — {z.zoneName}
                <button
                  type="button"
                  onClick={() => handleRemove(z.zoneId)}
                  disabled={removing}
                  title={t("removeTooltip")}
                  style={{
                    background: "none", border: "none", color: "#60A5FA",
                    cursor: removing ? "default" : "pointer", padding: 0, fontSize: 13, lineHeight: 1,
                  }}
                >
                  ×
                </button>
              </span>
            );
          })}
        </div>
      )}

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("locationLabel")}</label>
          <select
            value={selectedLocationId}
            onChange={(e) => handleLocationChange(e.target.value)}
            style={{ ...inputStyle, cursor: "pointer" }}
          >
            <option value="">{t("locationPlaceholder")}</option>
            {activeLocations.map((l) => (
              <option key={l.id} value={l.id}>{l.name}</option>
            ))}
          </select>
        </div>

        <div>
          <label style={labelStyle}>{t("zoneLabel")}</label>
          <select
            value={selectedZoneId}
            onChange={(e) => setSelectedZoneId(e.target.value)}
            disabled={!selectedLocation}
            style={{ ...inputStyle, cursor: selectedLocation ? "pointer" : "default" }}
          >
            <option value="">
              {!selectedLocation
                ? t("zonePlaceholderPickLocation")
                : availableZones.length === 0
                ? t("zoneNoneAvailable")
                : t("zonePlaceholder")}
            </option>
            {availableZones.map((z) => (
              <option key={z.id} value={z.id}>
                {z.name} — {tZoneTypes.has(z.type) ? tZoneTypes(z.type) : z.type}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div style={{ display: "flex", justifyContent: "flex-end" }}>
        <Btn
          type="button"
          size="sm"
          onClick={handleAdd}
          disabled={!selectedZoneId || assignZone.isPending}
        >
          {assignZone.isPending ? t("adding") : t("addButton")}
        </Btn>
      </div>

      {error && <div style={errorBoxStyle}>{error}</div>}
    </div>
  );
}
