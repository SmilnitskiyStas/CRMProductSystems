"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type { LocationZoneDto } from "../types";
import { useCreateZone } from "../hooks/useFloorPlan";

const ZONE_TYPE_VALUES = ["shelf", "fridge", "freezer", "display", "production", "warehouse"] as const;

const TEMP_TYPES = new Set(["fridge", "freezer"]);

interface Props {
  locationId: string;
  onCreated: (zone: LocationZoneDto) => void;
  onClose: () => void;
}

export function ZoneDialog({ locationId, onCreated, onClose }: Props) {
  const t = useTranslations("Dashboard.locations.zoneDialog");
  const tZoneTypes = useTranslations("Dashboard.locations.zoneTypes");
  const tCommon = useTranslations("Common");
  const createZone = useCreateZone(locationId);
  const [name, setName] = useState("");
  const [type, setType] = useState("shelf");
  const [shelvesCount, setShelvesCount] = useState(1);
  const [tempMin, setTempMin] = useState<string>("");
  const [tempMax, setTempMax] = useState<string>("");

  const showTemp = TEMP_TYPES.has(type);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    createZone.mutate(
      {
        name: name.trim(),
        type,
        shelvesCount,
        tempMin: showTemp && tempMin !== "" ? Number(tempMin) : null,
        tempMax: showTemp && tempMax !== "" ? Number(tempMax) : null,
      },
      {
        onSuccess: (zone) => {
          onCreated(zone);
          onClose();
        },
      }
    );
  }

  const inputStyle: React.CSSProperties = {
    background: "#0B0E14",
    border: "1px solid #1F2937",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    padding: "8px 12px",
    width: "100%",
    boxSizing: "border-box",
    outline: "none",
  };

  const labelStyle: React.CSSProperties = {
    color: "#9CA3AF",
    fontSize: 12,
    marginBottom: 4,
    display: "block",
  };

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 1000,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 16,
          padding: 28,
          width: 420,
          maxWidth: "calc(100vw - 32px)",
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: "0 0 20px 0" }}>
          {t("title")}
        </h2>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <div>
            <label style={labelStyle}>{t("nameLabel")}</label>
            <input
              style={inputStyle}
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t("namePlaceholder")}
              autoFocus
              required
            />
          </div>

          <div>
            <label style={labelStyle}>{t("typeLabel")}</label>
            <select
              style={inputStyle}
              value={type}
              onChange={(e) => setType(e.target.value)}
            >
              {ZONE_TYPE_VALUES.map((value) => (
                <option key={value} value={value}>
                  {tZoneTypes(value)}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label style={labelStyle}>{t("shelvesCountLabel")}</label>
            <input
              style={inputStyle}
              type="number"
              min={1}
              value={shelvesCount}
              onChange={(e) => setShelvesCount(Math.max(1, Number(e.target.value)))}
            />
          </div>

          {showTemp && (
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("tempMinLabel")}</label>
                <input
                  style={inputStyle}
                  type="number"
                  value={tempMin}
                  onChange={(e) => setTempMin(e.target.value)}
                  placeholder={t("tempMinPlaceholder")}
                />
              </div>
              <div>
                <label style={labelStyle}>{t("tempMaxLabel")}</label>
                <input
                  style={inputStyle}
                  type="number"
                  value={tempMax}
                  onChange={(e) => setTempMax(e.target.value)}
                  placeholder={t("tempMaxPlaceholder")}
                />
              </div>
            </div>
          )}

          <div style={{ display: "flex", gap: 10, marginTop: 4 }}>
            <button
              type="button"
              onClick={onClose}
              style={{
                flex: 1,
                background: "#0B0E14",
                border: "1px solid #1F2937",
                color: "#9CA3AF",
                borderRadius: 8,
                padding: "9px 16px",
                fontSize: 13,
                cursor: "pointer",
              }}
            >
              {tCommon("cancel")}
            </button>
            <button
              type="submit"
              disabled={createZone.isPending || !name.trim()}
              style={{
                flex: 2,
                background: createZone.isPending || !name.trim() ? "#1F2937" : "#2563EB",
                border: "none",
                color: createZone.isPending || !name.trim() ? "#6B7280" : "#fff",
                borderRadius: 8,
                padding: "9px 16px",
                fontSize: 13,
                fontWeight: 600,
                cursor: createZone.isPending || !name.trim() ? "default" : "pointer",
              }}
            >
              {createZone.isPending ? t("creating") : t("submit")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
