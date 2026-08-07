"use client";

import { X } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { usePosSummary, usePosTopProducts, usePosCashiers } from "../hooks/usePosAnalytics";
import { PosSummaryCards } from "./PosSummaryCards";
import { PosTopProductsTable } from "./PosTopProductsTable";
import { PosCashierStatsTable } from "./PosCashierStatsTable";

interface Props {
  /** yyyy-mm-dd — the day selected on the revenue trend chart. Used as both `from` and `to` for
   * every query below, so all three panels resolve to exactly that one day. */
  date: string;
  /** Current store filter from the page, if any — kept live (not snapshotted at selection time)
   * so the panel stays consistent with whatever the revenue trend chart above it is showing. */
  storeId?: string;
  onClose: () => void;
}

/**
 * Pure composition, no new data-fetching hook or endpoint: re-runs the existing POS
 * summary/top-products/cashiers hooks scoped to a single day (from = to = date) and renders the
 * existing PosSummaryCards + PosTopProductsTable + PosCashierStatsTable unmodified. Always the
 * CURRENT period's day — PosRevenueTrendChart's onDayClick only ever resolves a current-period
 * point to a date, never a comparison-period one, so this never needs to know about compare mode.
 */
export function PosDayDetailPanel({ date, storeId, onClose }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.dayDetail");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const params = { from: date, to: date, store_id: storeId };
  const { data: summary, isLoading: summaryLoading } = usePosSummary(params);
  const { data: topProducts, isLoading: topLoading } = usePosTopProducts({ ...params, limit: "10" });
  const { data: cashiers, isLoading: cashiersLoading } = usePosCashiers(params);

  const formattedDate = new Date(`${date}T00:00:00Z`).toLocaleDateString(intlLocale, {
    day: "numeric",
    month: "long",
    year: "numeric",
  });

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 16px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 16 }}>
        <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title", { date: formattedDate })}</div>
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

      {summaryLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
      ) : summary ? (
        <PosSummaryCards data={summary} />
      ) : (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noData")}</div>
      )}

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
        <div>
          {topLoading ? (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
          ) : topProducts ? (
            <PosTopProductsTable data={topProducts} />
          ) : null}
        </div>
        <div>
          {cashiersLoading ? (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</div>
          ) : cashiers ? (
            <PosCashierStatsTable data={cashiers} />
          ) : null}
        </div>
      </div>
    </div>
  );
}
