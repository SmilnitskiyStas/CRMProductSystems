"use client";

import { useTranslations } from "next-intl";
import { useRegionLabel } from "@/features/geo/hooks/useRegions";
import { formatDeliveryTerms } from "@/features/geo/lib/formatDeliveryTerms";
import type { DeliveryCoverageEntry } from "@/features/geo/types";
import type { RegionDeliveryStat } from "../types";

interface Props {
  /** Worker-measured average delivery time per destination region. */
  stats: RegionDeliveryStat[] | null | undefined;
  /** The supplier's declared per-region delivery terms (`deliveryCoverage.served`). */
  served: DeliveryCoverageEntry[] | null | undefined;
}

/** One decimal place, drop a trailing ".0" (mirrors DeliveryByRegionPanel). */
function fmtDays(v: number): string {
  return Number(v.toFixed(1)).toString();
}

/**
 * Delivery section of the supplier-metrics detail page (TASK-672): the full
 * per-region breakdown with a "declared vs actual" two-column presentation.
 * Rows are driven by the measured `stats` (fastest region first); for each one
 * the supplier's declared terms for that region are shown alongside when present.
 * `DeliveryByRegionPanel` only renders the measured column, so this is its
 * comparison-aware sibling.
 */
export function DeliveryRegionComparison({ stats, served }: Props) {
  const t = useTranslations("Dashboard.marketplace.metricsPage");
  const tRegion = useTranslations("Dashboard.marketplace.deliveryByRegion");
  const tTerms = useTranslations("Dashboard.geo.deliveryTerms");
  const regionLabel = useRegionLabel();

  const measured = [...(stats ?? [])].sort(
    (a, b) => a.avgDeliveryDays - b.avgDeliveryDays,
  );

  if (measured.length === 0) {
    return (
      <div style={{ color: "#4B5563", fontSize: 12, padding: "10px 2px" }}>
        {tRegion("empty")}
      </div>
    );
  }

  const declaredByCode = new Map(
    (served ?? []).map((entry) => [entry.regionCode, entry]),
  );

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 6, marginTop: 12 }}>
      {measured.map((s) => {
        const declaredEntry = declaredByCode.get(s.regionCode);
        const declared = declaredEntry
          ? formatDeliveryTerms(declaredEntry, tTerms)
          : "";
        return (
          <div
            key={s.regionCode}
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "10px 12px",
              display: "flex",
              flexDirection: "column",
              gap: 6,
            }}
          >
            <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
              {regionLabel(s.regionCode)}
            </span>
            <div style={{ display: "flex", gap: 20, flexWrap: "wrap" }}>
              <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <span style={{ color: "#4B5563", fontSize: 11 }}>{t("declared")}</span>
                <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                  {declared || "—"}
                </span>
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 2 }}>
                <span style={{ color: "#4B5563", fontSize: 11 }}>{t("actual")}</span>
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                  {tRegion("days", { days: fmtDays(s.avgDeliveryDays) })}{" "}
                  <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 400 }}>
                    {tRegion("sample", { n: s.sampleSize })}
                  </span>
                </span>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
