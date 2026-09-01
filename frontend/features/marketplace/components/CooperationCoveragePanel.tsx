"use client";

// Advisory delivery-coverage panel shown at the top of the cooperation-request
// modal (TASK-657). Tells the buyer whether this supplier delivers to their region
// before they send the request. Purely advisory — it never blocks the submit.

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useRegionLabel } from "@/features/geo/hooks/useRegions";
import { formatDeliveryTerms } from "@/features/geo/lib/formatDeliveryTerms";
import { RegionSelect } from "@/features/geo/components/RegionSelect";
import { useSupplierCoverageForBuyer } from "../hooks/useMarketplace";

interface Props {
  supplierId: string;
}

/** One decimal place, dropping a trailing ".0" (2 → "2", 2.43 → "2.4"). */
function fmtDays(v: number): string {
  return Number(v.toFixed(1)).toString();
}

const boxStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "12px 14px",
  marginTop: 14,
  fontSize: 12.5,
  lineHeight: 1.45,
};

export function CooperationCoveragePanel({ supplierId }: Props) {
  const t = useTranslations(
    "Dashboard.marketplace.cooperationRequestModal.coverage"
  );
  const tTerms = useTranslations("Dashboard.geo.deliveryTerms");
  const regionLabel = useRegionLabel();
  const [regionOverride, setRegionOverride] = useState<string | null>(null);
  const { data, isLoading } = useSupplierCoverageForBuyer(
    supplierId,
    regionOverride
  );

  if (isLoading || !data) {
    return (
      <div style={{ ...boxStyle, color: "#6B7280" }}>{t("loading")}</div>
    );
  }

  const {
    coverage,
    buyerRegionStatus,
    buyerRegionCode,
    buyerRegionEntry,
    measuredAvgDeliveryDaysToBuyerRegion,
    measuredSampleSize,
  } = data;

  const regionName = buyerRegionCode ? regionLabel(buyerRegionCode) : "";
  const served = coverage?.served ?? [];
  const notServed = coverage?.notServed ?? [];
  const note = coverage?.note?.trim() ? coverage.note : null;
  const hasSummary = served.length > 0 || notServed.length > 0 || !!note;

  const buyerRegionTerms = buyerRegionEntry
    ? formatDeliveryTerms(buyerRegionEntry, tTerms)
    : "";
  const buyerRegionNote = buyerRegionEntry?.note?.trim()
    ? buyerRegionEntry.note
    : null;

  return (
    <div style={boxStyle}>
      {buyerRegionStatus === "served" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          <div style={{ color: "#34D399", fontWeight: 600 }}>
            {t("servesYourRegion", { region: regionName })}
          </div>
          {buyerRegionTerms && (
            <div style={{ color: "#9CA3AF" }}>
              {t("termsLabel", { terms: buyerRegionTerms })}
            </div>
          )}
          {buyerRegionNote && (
            <div style={{ color: "#9CA3AF", whiteSpace: "pre-wrap" }}>
              {buyerRegionNote}
            </div>
          )}
          {measuredAvgDeliveryDaysToBuyerRegion != null && (
            <div style={{ color: "#9CA3AF" }}>
              {t("measuredDeliveryLabel", {
                days: fmtDays(measuredAvgDeliveryDaysToBuyerRegion),
                count: measuredSampleSize ?? 0,
              })}
            </div>
          )}
        </div>
      )}

      {buyerRegionStatus === "not_served" && (
        <div style={{ color: "#F59E0B", fontWeight: 600 }}>
          {t("doesNotServeYourRegion", { region: regionName })}
        </div>
      )}

      {buyerRegionStatus === "unknown" &&
        (buyerRegionCode?.trim() ? (
          // Region IS resolved — the supplier simply didn't declare it in either
          // `served` or `notServed`. Show a neutral advisory line, not the
          // "couldn't determine your region" prompt (BUG-1 / TASK-664).
          <div style={{ color: "#9CA3AF" }}>
            {t("regionNotDeclared", { region: regionName })}
          </div>
        ) : (
          // Region genuinely unresolved — offer the manual override that re-fires
          // the coverage query with `?buyerRegionCode=`.
          <div>
            <div style={{ color: "#9CA3AF" }}>{t("regionUnknown")}</div>
            <div style={{ marginTop: 8 }}>
              <label
                style={{
                  display: "block",
                  color: "#6B7280",
                  fontSize: 11.5,
                  marginBottom: 4,
                }}
              >
                {t("yourRegionLabel")}
              </label>
              <RegionSelect
                value={regionOverride}
                onChange={setRegionOverride}
                placeholder={t("regionSelectPlaceholder")}
              />
            </div>
          </div>
        ))}

      {hasSummary && (
        <div
          style={{
            borderTop: "1px solid #1F2937",
            marginTop: 10,
            paddingTop: 10,
            display: "flex",
            flexDirection: "column",
            gap: 6,
          }}
        >
          {served.map((entry) => {
            const terms = formatDeliveryTerms(entry, tTerms);
            const regionNote = entry.note?.trim() ? entry.note : null;
            return (
              <div
                key={entry.regionCode}
                style={{ display: "flex", flexDirection: "column", gap: 2 }}
              >
                <div
                  style={{
                    display: "flex",
                    gap: 6,
                    flexWrap: "wrap",
                    alignItems: "baseline",
                  }}
                >
                  <span style={{ color: "#E8EDF5", fontWeight: 600 }}>
                    {regionLabel(entry.regionCode)}
                  </span>
                  <span style={{ color: "#6B7280", fontSize: 11.5 }}>
                    {terms || t("termsByAgreement")}
                  </span>
                </div>
                {regionNote && (
                  <span
                    style={{
                      color: "#9CA3AF",
                      fontSize: 11.5,
                      whiteSpace: "pre-wrap",
                    }}
                  >
                    {regionNote}
                  </span>
                )}
              </div>
            );
          })}

          {notServed.length > 0 && (
            <div style={{ color: "#6B7280", fontSize: 11.5 }}>
              {t("notServed", {
                regions: notServed.map((code) => regionLabel(code)).join(", "),
              })}
            </div>
          )}

          {note && (
            <div
              style={{
                color: "#9CA3AF",
                fontSize: 11.5,
                whiteSpace: "pre-wrap",
              }}
            >
              {note}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
