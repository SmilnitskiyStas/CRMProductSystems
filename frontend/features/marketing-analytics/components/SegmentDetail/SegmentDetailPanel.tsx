"use client";

import { useState, useEffect } from "react";
import { useTranslations } from "next-intl";
import { X, Users } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canExportMarketingAnalyticsPii } from "@/lib/roles";
import { useRfmSegmentDetail } from "../../hooks/useMarketingAnalytics";
import { TopProductsPanel } from "./TopProductsPanel";
import { AffinityBasketTabs } from "./AffinityBasketTabs";
import { BehaviorPanel } from "./BehaviorPanel";
import { RecommendationCard } from "./RecommendationCard";
import { ExportSegmentButton, PiiUnmaskToggle } from "../ExportButtons";
import type { MarketingAnalyticsFilters, RfmSegmentKey } from "../../types";

interface Props {
  segmentKey: RfmSegmentKey;
  filters: MarketingAnalyticsFilters;
  /** Resolved concrete YYYY-MM-DD range for the CURRENT filters (from the already-loaded
   * overview's periodFrom/periodTo) — exports need real dates even when the UI filter is a
   * preset like "6m"; re-deriving "6 months ago" client-side could subtly disagree with the
   * backend's own resolution, so the server's own resolved range is reused instead. */
  periodFrom: string;
  periodTo: string;
  onClose: () => void;
}

/**
 * The container for an expanded segment (RFM_ANALYSIS.md §10-14): renders BELOW SegmentGrid,
 * never in place of it. Ties together the three detail columns (top products / cross-sell /
 * behavior) plus the recommendation block, and owns the one piece of state those columns
 * share — which top product is selected for the affinity/basket tabs.
 *
 * Observed in manual browser verification (TASK-409): the parent page only renders this
 * component while `overview` is loaded (`selectedSegment && overview && <SegmentDetailPanel/>`),
 * and a brand-new filter combination briefly makes `overview` undefined again (no
 * `keepPreviousData`, by design — see useMarketingAnalytics.ts). That unmounts and remounts
 * this component across a period/store change, which resets `selectedProductName` and
 * `unmaskPii` back to their defaults too, not just on an explicit segment change. This is
 * safe (never shows a stale product selection or an accidentally-still-checked PII toggle
 * against new data) and satisfies the "never mix old and new numbers" requirement, just
 * slightly stronger than originally intended — flagged here rather than silently left
 * undocumented.
 */
export function SegmentDetailPanel({ segmentKey, filters, periodFrom, periodTo, onClose }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.detail");
  const { data: me } = useMe();
  const canExportPii = canExportMarketingAnalyticsPii(me?.role, me?.permissions);
  const [unmaskPii, setUnmaskPii] = useState(false);
  const [selectedProductName, setSelectedProductName] = useState<string | null>(null);

  const { data: detail, isLoading } = useRfmSegmentDetail(segmentKey, filters);

  // A newly opened segment has its own top-product list — the previously selected product
  // (from a different segment) may not even exist here, so always land back on the
  // "Оберіть товар зліва" empty state for a fresh segment.
  useEffect(() => {
    setSelectedProductName(null);
  }, [segmentKey]);

  const exportBase = { segmentKey, from: periodFrom, to: periodTo, storeIds: filters.storeIds, unmaskPii };
  const filterKey = `${filters.period}:${filters.from ?? ""}:${filters.to ?? ""}:${filters.storeIds.join(",")}`;
  const isEmpty = !!detail && detail.customerCount === 0;

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 18 }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700 }}>{detail?.labelUa ?? "…"}</div>
          {detail?.shortDescriptionUa && (
            <div style={{ color: "#6B7280", fontSize: 13, marginTop: 3 }}>{detail.shortDescriptionUa}</div>
          )}
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 12, flexShrink: 0 }}>
          {!isEmpty && detail && (
            <>
              <PiiUnmaskToggle checked={unmaskPii} onChange={setUnmaskPii} allowed={canExportPii} />
              <ExportSegmentButton {...exportBase} />
            </>
          )}
          <button
            onClick={onClose}
            title={t("closeButton")}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 8,
              width: 30,
              height: 30,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              cursor: "pointer",
              color: "#9CA3AF",
              flexShrink: 0,
            }}
          >
            <X size={15} />
          </button>
        </div>
      </div>

      {isLoading || !detail ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("loading")}</div>
      ) : isEmpty ? (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: 10,
            padding: "36px 20px",
            color: "#4B5563",
            textAlign: "center",
          }}
        >
          <Users size={30} strokeWidth={1.5} />
          <div style={{ color: "#9CA3AF", fontSize: 14, fontWeight: 600 }}>{t("emptySegmentTitle")}</div>
          <p style={{ fontSize: 13, maxWidth: 380, margin: 0 }}>{t("emptySegmentBody")}</p>
        </div>
      ) : (
        <>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", gap: 16, alignItems: "start" }}>
            <TopProductsPanel
              products={detail.topProducts}
              isLoading={false}
              selectedProductName={selectedProductName}
              onSelectProduct={setSelectedProductName}
              exportBase={exportBase}
            />
            <AffinityBasketTabs
              segmentKey={segmentKey}
              productName={selectedProductName}
              filters={filters}
              exportBase={exportBase}
            />
            <BehaviorPanel behavior={detail.behavior} isLoading={false} />
          </div>

          <RecommendationCard
            key={`${segmentKey}:${filterKey}`}
            recommendation={detail.recommendation}
            segmentKey={segmentKey}
            filters={filters}
          />
        </>
      )}
    </div>
  );
}
