"use client";

import { useState, useMemo } from "react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import {
  usePosSummary,
  usePosRevenueTrend,
  usePosTopProducts,
  usePosCashiers,
} from "@/features/analytics/hooks/usePosAnalytics";
import { PosSummaryCards } from "@/features/analytics/components/PosSummaryCards";
import { PosRevenueTrendChart } from "@/features/analytics/components/PosRevenueTrendChart";
import { PosTopProductsTable } from "@/features/analytics/components/PosTopProductsTable";
import { PosCashierStatsTable } from "@/features/analytics/components/PosCashierStatsTable";
import { PosPaymentPieChart } from "@/features/analytics/components/PosPaymentPieChart";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";

// ── helpers ──────────────────────────────────────────────────────────────────

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function defaultFrom(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return toDateStr(d);
}

function defaultTo(): string {
  return toDateStr(new Date());
}

// ── Store selector (simple) ───────────────────────────────────────────────────

interface StoreOption {
  id: string;
  name: string;
}

function useStores() {
  return useQuery<StoreOption[]>({
    queryKey: ["stores-list"],
    queryFn: () => api.get<StoreOption[]>("/api/stores"),
    staleTime: 5 * 60 * 1000,
  });
}

// ── style tokens (match rest of analytics) ────────────────────────────────────

const labelStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  marginBottom: 4,
  display: "block",
};

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
  outline: "none",
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  minWidth: 160,
  cursor: "pointer",
};

const toggleBtn = (active: boolean): React.CSSProperties => ({
  padding: "7px 16px",
  fontSize: 12,
  fontWeight: 500,
  borderRadius: 6,
  border: "1px solid #1F2937",
  cursor: "pointer",
  background: active ? "#1D3461" : "#0D1117",
  color: active ? "#93C5FD" : "#6B7280",
  transition: "background 0.1s, color 0.1s",
});

const sectionTitle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 15,
  fontWeight: 700,
  margin: 0,
  marginBottom: 12,
};

// ── page ─────────────────────────────────────────────────────────────────────

export default function PosAnalyticsPage() {
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;

  const [from, setFrom] = useState<string>(defaultFrom);
  const [to, setTo] = useState<string>(defaultTo);
  const [storeId, setStoreId] = useState<string>("");
  const [groupBy, setGroupBy] = useState<"day" | "week">("day");

  const { data: stores } = useStores();

  const params = useMemo(
    () => ({ from, to, store_id: storeId || undefined }),
    [from, to, storeId],
  );

  const enabled = access === true;

  const { data: summary, isLoading: summaryLoading } = usePosSummary(
    { from, to, store_id: storeId || undefined },
    enabled,
  );
  const { data: trend, isLoading: trendLoading } = usePosRevenueTrend(
    { ...params, group_by: groupBy },
    enabled,
  );
  const { data: topProducts, isLoading: topLoading } = usePosTopProducts(
    { ...params, limit: "10" },
    enabled,
  );
  const { data: cashiers, isLoading: cashiersLoading } = usePosCashiers(
    { from, to, store_id: storeId || undefined },
    enabled,
  );

  if (access === null) return null;
  if (!access) return <AccessDenied title="POS Аналітика" />;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 28, width: "100%" }}>
      {/* Header */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>POS Аналітика</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          Виручка, транзакції, топ-товари та статистика по касирах
        </p>
      </div>

      {/* Filters */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "16px 20px",
          display: "flex",
          flexWrap: "wrap",
          gap: 16,
          alignItems: "flex-end",
        }}
      >
        <div>
          <label style={labelStyle}>З дати</label>
          <input
            type="date"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            style={inputStyle}
          />
        </div>
        <div>
          <label style={labelStyle}>По дату</label>
          <input
            type="date"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            style={inputStyle}
          />
        </div>
        <div>
          <label style={labelStyle}>Магазин</label>
          <select
            value={storeId}
            onChange={(e) => setStoreId(e.target.value)}
            style={selectStyle}
          >
            <option value="">Всі магазини</option>
            {stores?.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label style={labelStyle}>Тренд</label>
          <div style={{ display: "flex", gap: 4 }}>
            <button
              style={toggleBtn(groupBy === "day")}
              onClick={() => setGroupBy("day")}
            >
              День
            </button>
            <button
              style={toggleBtn(groupBy === "week")}
              onClick={() => setGroupBy("week")}
            >
              Тиждень
            </button>
          </div>
        </div>
      </div>

      {/* KPI summary cards */}
      <section>
        <h2 style={sectionTitle}>Зведення</h2>
        {summaryLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>
        ) : summary ? (
          <PosSummaryCards data={summary} />
        ) : (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Немає даних</div>
        )}
      </section>

      {/* Revenue trend */}
      <section>
        <h2 style={sectionTitle}>Динаміка виручки</h2>
        {trendLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>
        ) : trend ? (
          <PosRevenueTrendChart data={trend} />
        ) : (
          <div
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 10,
              padding: "20px 16px",
              color: "#4B5563",
              fontSize: 13,
            }}
          >
            Немає даних
          </div>
        )}
      </section>

      {/* Top products + Cashiers side by side */}
      <section>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
          <div>
            {topLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>
            ) : topProducts ? (
              <PosTopProductsTable data={topProducts} />
            ) : null}
          </div>
          <div>
            {cashiersLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>
            ) : cashiers ? (
              <PosCashierStatsTable data={cashiers} />
            ) : null}
          </div>
        </div>
      </section>

      {/* Payment pie */}
      {summary && summary.totalRevenue > 0 && (
        <section>
          <h2 style={sectionTitle}>Методи оплати</h2>
          <div style={{ maxWidth: 400 }}>
            <PosPaymentPieChart data={summary} />
          </div>
        </section>
      )}
    </div>
  );
}
