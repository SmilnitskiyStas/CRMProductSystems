"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import type { LocationZoneDto } from "../types";
import { useCreateZone } from "../hooks/useFloorPlan";

import { ZONE_PALETTE_TYPES } from "../lib/zoneTypes";

const ZONE_TYPE_VALUES = ZONE_PALETTE_TYPES;

const TEMP_TYPES = new Set(["fridge", "freezer"]);

interface Props {
  locationId: string;
  initialType?: string;
  onCreated: (zone: LocationZoneDto) => void;
  onClose: () => void;
}

export function ZoneDialog({ locationId, initialType, onCreated, onClose }: Props) {
  const t = useTranslations("Dashboard.locations.zoneDialog");
  const tZoneTypes = useTranslations("Dashboard.locations.zoneTypes");
  const tCommon = useTranslations("Common");
  const createZone = useCreateZone(locationId);
  const [name, setName] = useState("");
  const [type, setType] = useState(initialType ?? "shelf");
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

  return (
    <Modal title={t("title")} onClose={onClose} width={420}>
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
            <Btn variant="ghost" onClick={onClose} style={{ flex: 1, justifyContent: "center" }}>
              {tCommon("cancel")}
            </Btn>
            <Btn type="submit" disabled={createZone.isPending || !name.trim()} style={{ flex: 2, justifyContent: "center" }}>
              {createZone.isPending ? t("creating") : t("submit")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
