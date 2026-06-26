"use client";

import { useState, useMemo } from "react";
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, Legend,
} from "recharts";
import { Loader2, TrendingUp, ArrowDownToLine, ArrowUpFromLine, Trash2, RefreshCw } from "lucide-react";
import { useProductMovements } from "../hooks/useProductMovements";
import type { MovementDto } from "../api/movements";

// ── Chart legend config ───────────────────────────────────────────────────────

const LINES = [
  { key: "receipt",    label: "Поставки",   color: "#3B82F6", dash: "6 3" },
  { key: "transfer",   label: "Передачі",   color: "#A78BFA", dash: undefined },
  { key: "write_off",  label: "Списання",   color: "#F87171", dash: undefined },
  { key: "adjustment", label: "Коригування",color: "#4ADE80", dash: undefined },
] as const;

const MOVEMENT_LABELS: Record<string, string> = {
  receipt:    "Поставка",
  transfer:   "Передача",
  write_off:  "Списання",
  adjustment: "Коригування",
};

// ── Date range helpers ────────────────────────────────────────────────────────

const RANGES = [
  { label: "30 дн.", days: 30 },
  { label: "60 дн.", days: 60 },
  { label: "90 дн.", days: 90 },
] as const;

function daysAgoStr(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

function todayStr(): string {
  return new Date().toISOString().slice(0, 10);
}

// ── Group movements by day ────────────────────────────────────────────────────

interface ChartPoint {
  date: string;
  receipt: number;
  transfer: number;
  write_off: number;
  adjustment: number;
}

function groupByDay(items: MovementDto[], from: string): ChartPoint[] {
  const map = new Map<string, ChartPoint>();

  const start = new Date(from);
  const today = new Date();
  for (let d = new Date(start); d <= today; d.setDate(d.getDate() + 1)) {
    const key = d.toISOString().slice(0, 10);
    map.set(key, { date: key, receipt: 0, transfer: 0, write_off: 0, adjustment: 0 });
  }

  for (const m of items) {
    const key = m.createdAt.slice(0, 10);
    const point = map.get(key);
    if (point) {
      const type = m.movementType as keyof Omit<ChartPoint, "date">;
      if (type in point) point[type] += Math.abs(m.quantity);
    }
  }

  return Array.from(map.values());
}

function formatDateTick(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString("uk-UA", { day: "numeric", month: "short" });
}

// ── Summary cards ─────────────────────────────────────────────────────────────

function SummaryCard({
  icon, label, value, color,
}: {
  icon: React.ReactNode; label: string; value: string | number; color: string;
}) {
  return (
    <div style={{
      background: "#0D1117", border: "1px solid #1F2937",
      borderRadius: 8, padding: "10px 14px",
      display: "flex", alignItems: "center", gap: 10,
    }}>
      <div style={{
        width: 30, height: 30, borderRadius: 7, flexShrink: 0,
        background: `${color}12`, border: `1px solid ${color}30`,
        display: "flex", alignItems: "center", justifyContent: "center",
      }}>
        {icon}
      </div>
      <div>
        <div style={{ color: "#4B5563", fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
          {label}
        </div>
        <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, fontFamily: "monospace", marginTop: 1 }}>
          {value}
        </div>
      </div>
    </div>
  );
}

// ── Legend row ────────────────────────────────────────────────────────────────

function ChartLegend() {
  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: "8px 16px", marginBottom: 12 }}>
      {LINES.map(({ key, label, color, dash }) => (
        <div key={key} style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <svg width="24" height="10">
            <line
              x1="0" y1="5" x2="24" y2="5"
              stroke={color}
              strokeWidth="2"
              strokeDasharray={dash ?? "none"}
            />
          </svg>
          <span style={{ color: "#9CA3AF", fontSize: 11 }}>{label}</span>
        </div>
      ))}
    </div>
  );
}

// ── Main component ────────────────────────────────────────────────────────────

export function ProductAnalyticsTab({ productId }: { productId: string }) {
  const [rangeDays, setRangeDays] = useState(30);

  const from = daysAgoStr(rangeDays);
  const to   = todayStr();

  const { data, isLoading } = useProductMovements(productId, {
    from,
    to,
    page_size: 500,
  });

  const movements = data?.items ?? [];

  const chartData = useMemo(
    () => groupByDay(movements, from),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [movements, from],
  );

  // Summary totals
  const totals = useMemo(() => ({
    receipt:    movements.filter((m) => m.movementType === "receipt").reduce((s, m) => s + m.quantity, 0),
    transfer:   movements.filter((m) => m.movementType === "transfer").reduce((s, m) => s + Math.abs(m.quantity), 0),
    write_off:  movements.filter((m) => m.movementType === "write_off").reduce((s, m) => s + Math.abs(m.quantity), 0),
    adjustment: movements.filter((m) => m.movementType === "adjustment").reduce((s, m) => s + Math.abs(m.quantity), 0),
  }), [movements]);

  if (isLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", padding: "48px 0" }}>
        <Loader2 size={28} color="#374151" style={{ animation: "spin 1s linear infinite" }} />
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* Date range selector */}
      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
        {RANGES.map((r) => (
          <button
            key={r.days}
            onClick={() => setRangeDays(r.days)}
            style={{
              padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600,
              cursor: "pointer",
              background: rangeDays === r.days ? "#1D3461" : "transparent",
              border: `1px solid ${rangeDays === r.days ? "#3B82F6" : "#1F2937"}`,
              color: rangeDays === r.days ? "#60A5FA" : "#6B7280",
              transition: "all 0.1s",
            }}
          >
            {r.label}
          </button>
        ))}
        <span style={{ color: "#4B5563", fontSize: 11, marginLeft: 4 }}>
          {new Date(from).toLocaleDateString("uk-UA", { day: "numeric", month: "short" })}
          {" — "}
          {new Date(to).toLocaleDateString("uk-UA", { day: "numeric", month: "short", year: "numeric" })}
        </span>
      </div>

      {/* Summary cards */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 8 }}>
        <SummaryCard icon={<ArrowDownToLine size={14} color="#3B82F6" />} label="Поставки" value={totals.receipt} color="#3B82F6" />
        <SummaryCard icon={<TrendingUp size={14} color="#A78BFA" />}     label="Передачі" value={totals.transfer} color="#A78BFA" />
        <SummaryCard icon={<Trash2 size={14} color="#F87171" />}         label="Списання" value={totals.write_off} color="#F87171" />
        <SummaryCard icon={<RefreshCw size={14} color="#4ADE80" />}      label="Коригування" value={totals.adjustment} color="#4ADE80" />
      </div>

      {/* Chart */}
      <div style={{
        background: "#0D1117", border: "1px solid #1F2937",
        borderRadius: 10, padding: "16px 12px 8px",
      }}>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 4 }}>
          Рух товару
        </div>
        <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 12 }}>
          За {rangeDays} днів · {movements.length} подій
        </div>

        <ChartLegend />

        {movements.length === 0 ? (
          <div style={{ textAlign: "center", padding: "24px 0", color: "#374151", fontSize: 13 }}>
            Немає рухів за вибраний період
          </div>
        ) : (
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={chartData} margin={{ left: 0, right: 8, top: 4, bottom: 4 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#1F2937" vertical={false} />
              <XAxis
                dataKey="date"
                tick={{ fill: "#6B7280", fontSize: 10 }}
                axisLine={false}
                tickLine={false}
                tickFormatter={formatDateTick}
                interval="preserveStartEnd"
              />
              <YAxis
                tick={{ fill: "#4B5563", fontSize: 10 }}
                axisLine={false}
                tickLine={false}
                width={32}
              />
              <Tooltip
                contentStyle={{
                  background: "#111827", border: "1px solid #1F2937",
                  borderRadius: 8, color: "#E8EDF5", fontSize: 12,
                }}
                labelFormatter={(label) => new Date(label).toLocaleDateString("uk-UA", {
                  day: "numeric", month: "long",
                })}
                formatter={(val, name) => {
                  const key = typeof name === "string" ? name : "";
                  const line = LINES.find((l) => l.key === key);
                  return [String(val), line?.label ?? key];
                }}
              />
              {LINES.map(({ key, color, dash }) => (
                <Line
                  key={key}
                  type="monotone"
                  dataKey={key}
                  stroke={color}
                  strokeWidth={2}
                  strokeDasharray={dash}
                  dot={false}
                  activeDot={{ r: 3, fill: color }}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>

      {/* Movement log table */}
      {movements.length > 0 && (
        <div>
          <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 8, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
            Журнал подій
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4, maxHeight: 280, overflowY: "auto" }}>
            {movements.slice(0, 50).map((m) => {
              const line = LINES.find((l) => l.key === m.movementType);
              return (
                <div
                  key={m.id}
                  style={{
                    display: "flex", alignItems: "center", gap: 10,
                    padding: "7px 10px",
                    background: "#0A0F1A", border: "1px solid #1F2937",
                    borderRadius: 7,
                  }}
                >
                  {/* Color dot */}
                  <div style={{
                    width: 8, height: 8, borderRadius: "50%", flexShrink: 0,
                    background: line?.color ?? "#6B7280",
                  }} />

                  {/* Type */}
                  <span style={{
                    fontSize: 11, padding: "1px 6px", borderRadius: 4, flexShrink: 0,
                    background: `${line?.color ?? "#6B7280"}15`,
                    color: line?.color ?? "#6B7280",
                    fontWeight: 600,
                  }}>
                    {MOVEMENT_LABELS[m.movementType] ?? m.movementType}
                  </span>

                  {/* Route */}
                  <span style={{ color: "#9CA3AF", fontSize: 11, flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                    {m.fromStoreName && m.toStoreName
                      ? `${m.fromStoreName} → ${m.toStoreName}`
                      : m.fromStoreName ?? m.toStoreName ?? "—"}
                  </span>

                  {/* Quantity */}
                  <span style={{
                    fontFamily: "monospace", fontSize: 12,
                    color: m.movementType === "receipt" ? "#4ADE80"
                      : m.movementType === "write_off" ? "#F87171"
                      : "#E8EDF5",
                    flexShrink: 0,
                  }}>
                    {m.movementType === "write_off" || m.movementType === "transfer"
                      ? `−${Math.abs(m.quantity)}`
                      : `+${m.quantity}`}
                  </span>

                  {/* Date */}
                  <span style={{ color: "#4B5563", fontSize: 10, flexShrink: 0 }}>
                    {new Date(m.createdAt).toLocaleDateString("uk-UA", { day: "2-digit", month: "2-digit" })}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
