"use client";

// Supplier team performance (Phase 8, TASK-696). Per-employee KPI table over a date range
// (order throughput + timing, on-time delivery, discrepancy-free receiving, chat
// responsiveness, buyer ratings) with period-over-period deltas on the headline columns.
// Row → modal listing that employee's individual buyer reviews. Dark-theme inline styles to
// match the rest of the supplier cabinet.

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Btn } from "@/components/ui/Btn";
import { StarRating } from "@/features/marketplace/components/StarRating";
import {
  useSupplierTeamPerformance,
  useEmployeeReviews,
} from "../hooks/useSupplierTeamPerformance";
import type {
  SupplierEmployeePerformance,
  SupplierPeriodMetric,
} from "../types";

const dateInputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 10px",
  outline: "none",
};

const labelStyle: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
  display: "block",
};

function isoDaysAgo(days: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() - days);
  return d.toISOString().slice(0, 10);
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

type DeltaKind = "count" | "rate" | "rating";

function MetricDelta({
  metric,
  kind,
}: {
  metric: SupplierPeriodMetric;
  kind: DeltaKind;
}) {
  const t = useTranslations("Dashboard.supplierCabinet.teamPerformance");
  const diff = metric.current - metric.previous;
  const eps = kind === "count" ? 0.5 : kind === "rate" ? 0.005 : 0.05;

  if (Math.abs(diff) < eps) {
    return <span style={{ color: "#4B5563", fontSize: 10 }}>—</span>;
  }

  const up = diff > 0;
  const sign = up ? "+" : "";
  const text =
    kind === "count"
      ? `${sign}${Math.round(diff)}`
      : kind === "rate"
      ? `${sign}${(diff * 100).toFixed(0)} ${t("deltaPp")}`
      : `${sign}${diff.toFixed(1)}`;

  return (
    <span
      title={t("vsPreviousPeriod")}
      style={{
        color: up ? "#4ADE80" : "#F87171",
        fontSize: 10,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {up ? "▲" : "▼"} {text}
    </span>
  );
}

function StackedCell({
  value,
  delta,
}: {
  value: React.ReactNode;
  delta: React.ReactNode;
}) {
  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: 2,
      }}
    >
      <span style={{ color: "#E8EDF5" }}>{value}</span>
      {delta}
    </div>
  );
}

export function TeamPerformanceView() {
  const t = useTranslations("Dashboard.supplierCabinet.teamPerformance");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [from, setFrom] = useState<string>(() => isoDaysAgo(29));
  const [to, setTo] = useState<string>(() => todayIso());
  const [reviewsFor, setReviewsFor] = useState<SupplierEmployeePerformance | null>(null);

  const { data, isLoading, isError } = useSupplierTeamPerformance(from, to);

  const pct = (v: number | null) => (v == null ? "—" : `${Math.round(v * 100)}%`);
  const hrs = (v: number | null) =>
    v == null ? "—" : `${v.toLocaleString(intlLocale, { maximumFractionDigits: 1 })} ${t("hoursSuffix")}`;
  const num = (v: number) => v.toLocaleString(intlLocale);

  const columns: TableColumn<SupplierEmployeePerformance>[] = useMemo(
    () => [
      {
        key: "employee",
        header: t("colEmployee"),
        align: "left",
        cellStyle: { color: "#E8EDF5", fontWeight: 500, whiteSpace: "nowrap" },
        render: (r) => r.userName,
      },
      { key: "confirmed", header: t("colConfirmed"), render: (r) => num(r.ordersConfirmed) },
      {
        key: "shipped",
        header: t("colShipped"),
        render: (r) => (
          <StackedCell
            value={num(r.ordersShipped)}
            delta={<MetricDelta metric={r.ordersShippedDelta} kind="count" />}
          />
        ),
      },
      { key: "avgConfirmH", header: t("colAvgConfirmH"), render: (r) => hrs(r.avgHoursToConfirm) },
      { key: "avgShipH", header: t("colAvgShipH"), render: (r) => hrs(r.avgHoursToShip) },
      {
        key: "onTime",
        header: t("colOnTime"),
        render: (r) => (
          <StackedCell
            value={pct(r.onTimeDeliveryRate)}
            delta={<MetricDelta metric={r.onTimeDeliveryRateDelta} kind="rate" />}
          />
        ),
      },
      {
        key: "discrepancyFree",
        header: t("colDiscrepancyFree"),
        render: (r) => pct(r.discrepancyFreeRate),
      },
      { key: "chatMsgs", header: t("colChatMsgs"), render: (r) => num(r.chatMessagesSent) },
      {
        key: "medianResponseH",
        header: t("colMedianResponseH"),
        render: (r) => hrs(r.medianFirstResponseHours),
      },
      { key: "sessions", header: t("colSessions"), render: (r) => num(r.chatSessionsHandled) },
      {
        key: "buyerRating",
        header: t("colBuyerRating"),
        render: (r) =>
          r.avgBuyerRating == null ? (
            <span style={{ color: "#4B5563" }}>—</span>
          ) : (
            <StackedCell
              value={
                <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
                  <StarRating value={r.avgBuyerRating} size={12} />
                  <span style={{ color: "#6B7280", fontSize: 11 }}>
                    {r.avgBuyerRating.toFixed(1)} · {r.buyerReviewCount}
                  </span>
                </span>
              }
              delta={<MetricDelta metric={r.avgBuyerRatingDelta} kind="rating" />}
            />
          ),
      },
      {
        key: "reviews",
        header: "",
        render: (r) => (
          <Btn size="sm" variant="ghost" onClick={() => setReviewsFor(r)}>
            {t("reviewsAction")}
          </Btn>
        ),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, intlLocale],
  );

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Range picker */}
      <div style={{ display: "flex", gap: 16, flexWrap: "wrap", alignItems: "flex-end" }}>
        <div>
          <label style={labelStyle}>{t("from")}</label>
          <input
            type="date"
            value={from}
            max={to}
            onChange={(e) => e.target.value && setFrom(e.target.value)}
            style={dateInputStyle}
          />
        </div>
        <div>
          <label style={labelStyle}>{t("to")}</label>
          <input
            type="date"
            value={to}
            min={from}
            max={todayIso()}
            onChange={(e) => e.target.value && setTo(e.target.value)}
            style={dateInputStyle}
          />
        </div>
        <div style={{ display: "flex", gap: 8 }}>
          {[30, 90, 365].map((d) => (
            <button
              key={d}
              type="button"
              onClick={() => {
                setFrom(isoDaysAgo(d - 1));
                setTo(todayIso());
              }}
              style={{
                background: "transparent",
                border: "1px solid #374151",
                borderRadius: 8,
                color: "#9CA3AF",
                fontSize: 12,
                fontWeight: 600,
                padding: "8px 12px",
                cursor: "pointer",
              }}
            >
              {t("presetDays", { n: d })}
            </button>
          ))}
        </div>
      </div>

      {isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>
      ) : (
        <Table
          columns={columns}
          rows={data?.employees ?? []}
          rowKey={(r) => r.userId}
          minWidth={1180}
          onRowClick={(r) => setReviewsFor(r)}
          isLoading={isLoading}
          emptyMessage={isLoading ? t("reviewsLoading") : t("empty")}
        />
      )}

      {reviewsFor && (
        <EmployeeReviewsModal
          userId={reviewsFor.userId}
          userName={reviewsFor.userName}
          onClose={() => setReviewsFor(null)}
        />
      )}
    </div>
  );
}

function EmployeeReviewsModal({
  userId,
  userName,
  onClose,
}: {
  userId: string;
  userName: string;
  onClose: () => void;
}) {
  const t = useTranslations("Dashboard.supplierCabinet.teamPerformance");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: reviews, isLoading } = useEmployeeReviews(userId);

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 14,
          padding: "24px 28px",
          width: 520,
          maxWidth: "calc(100vw - 32px)",
          maxHeight: "calc(100vh - 80px)",
          display: "flex",
          flexDirection: "column",
          gap: 16,
        }}
      >
        <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
          {t("reviewsModalTitle", { name: userName })}
        </h2>

        <div style={{ overflowY: "auto", display: "flex", flexDirection: "column", gap: 12 }}>
          {isLoading ? (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{t("reviewsLoading")}</div>
          ) : !reviews || reviews.length === 0 ? (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{t("reviewsEmpty")}</div>
          ) : (
            reviews.map((r) => (
              <div
                key={r.id}
                style={{
                  background: "#0D1117",
                  border: "1px solid #1F2937",
                  borderRadius: 10,
                  padding: "12px 14px",
                  display: "flex",
                  flexDirection: "column",
                  gap: 6,
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
                  <StarRating value={r.rating} size={14} />
                  <span
                    style={{
                      fontSize: 10,
                      fontWeight: 600,
                      textTransform: "uppercase",
                      letterSpacing: "0.04em",
                      color: "#9CA3AF",
                      background: "#1F2937",
                      border: "1px solid #374151",
                      borderRadius: 6,
                      padding: "1px 7px",
                    }}
                  >
                    {r.source === "order" ? t("sourceOrder") : t("sourceChat")}
                  </span>
                  <span style={{ color: "#4B5563", fontSize: 11, marginLeft: "auto" }}>
                    {new Date(r.createdAt).toLocaleDateString(intlLocale, {
                      day: "2-digit",
                      month: "2-digit",
                      year: "numeric",
                    })}
                  </span>
                </div>
                {r.comment && (
                  <p style={{ color: "#E8EDF5", fontSize: 13, margin: 0, whiteSpace: "pre-wrap" }}>
                    {r.comment}
                  </p>
                )}
                {r.ratedByName && (
                  <span style={{ color: "#6B7280", fontSize: 11 }}>
                    {t("ratedBy", { name: r.ratedByName })}
                  </span>
                )}
              </div>
            ))
          )}
        </div>

        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <Btn variant="ghost" size="sm" onClick={onClose}>
            {t("close")}
          </Btn>
        </div>
      </div>
    </div>
  );
}
