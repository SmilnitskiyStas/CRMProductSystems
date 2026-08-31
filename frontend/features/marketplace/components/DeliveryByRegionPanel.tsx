"use client";

import { useTranslations } from "next-intl";
import { useRegionLabel } from "@/features/geo/hooks/useRegions";
import type { RegionDeliveryStat } from "../types";

interface Props {
  stats: RegionDeliveryStat[] | null | undefined;
}

/** One decimal place, but drop a trailing ".0" (2.0 → "2", 2.43 → "2.4"). */
function fmtDays(v: number): string {
  return Number(v.toFixed(1)).toString();
}

/**
 * Per-region delivery drill-down shown under the "average delivery time" tile on the
 * supplier profile. Sorted fastest-region-first. Empty / null → muted "not enough data".
 */
export function DeliveryByRegionPanel({ stats }: Props) {
  const t = useTranslations("Dashboard.marketplace.deliveryByRegion");
  const regionLabel = useRegionLabel();

  if (!stats || stats.length === 0) {
    return (
      <div style={{ color: "#4B5563", fontSize: 12, padding: "10px 2px" }}>
        {t("empty")}
      </div>
    );
  }

  const sorted = [...stats].sort((a, b) => a.avgDeliveryDays - b.avgDeliveryDays);

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        gap: 6,
        marginTop: 12,
      }}
    >
      {sorted.map((s) => (
        <div
          key={s.regionCode}
          style={{
            display: "flex",
            alignItems: "baseline",
            gap: 10,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "8px 12px",
          }}
        >
          <span style={{ color: "#E8EDF5", fontSize: 13, flex: 1, minWidth: 0 }}>
            {regionLabel(s.regionCode)}
          </span>
          <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, whiteSpace: "nowrap" }}>
            {t("days", { days: fmtDays(s.avgDeliveryDays) })}
          </span>
          <span style={{ color: "#4B5563", fontSize: 11, whiteSpace: "nowrap" }}>
            {t("sample", { n: s.sampleSize })}
          </span>
        </div>
      ))}
    </div>
  );
}
