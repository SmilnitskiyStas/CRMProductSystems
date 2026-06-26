"use client";

import { useState, useMemo } from "react";
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, ReferenceLine, ReferenceArea,
} from "recharts";
import { Loader2, TrendingUp, ArrowDownToLine, Trash2, RefreshCw, Package } from "lucide-react";
import { useProductMovements } from "../hooks/useProductMovements";
import type { MovementDto } from "../api/movements";

// ── Line config ───────────────────────────────────────────────────────────────

const LINES = [
  { key: "stock",      label: "Залишок",    color: "#38BDF8", dash: undefined, width: 2.5 },
  { key: "receipt",    label: "Поставки",   color: "#3B82F6", dash: "6 3",     width: 1.5 },
  { key: "transfer",   label: "Передачі",   color: "#A78BFA", dash: undefined, width: 1.5 },
  { key: "write_off",  label: "Списання",   color: "#F87171", dash: undefined, width: 1.5 },
  { key: "adjustment", label: "Коригування",color: "#4ADE80", dash: undefined, width: 1.5 },
];

const MOVEMENT_LABELS: Record<string, string> = {
  receipt:    "Поставка",
  transfer:   "Передача",
  write_off:  "Списання",
  adjustment: "Коригування",
};

// ── Zone config (background bands) ───────────────────────────────────────────

const ZONES = [
  { label: "Критична зона", color: "#EF4444", fill: "#EF444418" },
  { label: "Зона мінімуму", color: "#FACC15", fill: "#FACC1514" },
  { label: "Зона норми",    color: "#22C55E", fill: "#22C55E0C" },
  { label: "Зона надлишку", color: "#3B82F6", fill: "#3B82F60A" },
];

// ── Buffer reference lines ────────────────────────────────────────────────────

const BUFFER_LINES = [
  { key: "safetyBuffer", label: "Буфер безпеки", color: "#F97316", dash: "4 2" },
  { key: "minStock",     label: "Мін. залишок",  color: "#FACC15", dash: "4 2" },
  { key: "maxStock",     label: "Макс. залишок", color: "#34D399", dash: "4 2" },
];

export interface ProductBuffers {
  safetyBuffer: number;
  minStock: number;
  maxStock: number;
}

// ── Date ranges ───────────────────────────────────────────────────────────────

const RANGES = [
  { label: "7 дн.",  days: 7   },
  { label: "14 дн.", days: 14  },
  { label: "30 дн.", days: 30  },
  { label: "90 дн.", days: 90  },
  { label: "180 дн.",days: 180 },
  { label: "1 рік",  days: 365 },
];

function daysAgoStr(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() - days);
  return d.toISOString().slice(0, 10);
}

function todayStr(): string {
  return new Date().toISOString().slice(0, 10);
}

// ── Chart data ────────────────────────────────────────────────────────────────

interface ChartPoint {
  date: string;
  stock: number | null;
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
    map.set(key, { date: key, stock: null, receipt: 0, transfer: 0, write_off: 0, adjustment: 0 });
  }

  // Sort ascending so last quantityAfter per day is the end-of-day balance
  const sorted = [...items].sort((a, b) => a.createdAt.localeCompare(b.createdAt));

  for (const m of sorted) {
    const key = m.createdAt.slice(0, 10);
    const pt = map.get(key);
    if (!pt) continue;

    if (m.movementType === "receipt")    pt.receipt    += Math.abs(m.quantity);
    if (m.movementType === "transfer")   pt.transfer   += Math.abs(m.quantity);
    if (m.movementType === "write_off")  pt.write_off  += Math.abs(m.quantity);
    if (m.movementType === "adjustment") pt.adjustment += Math.abs(m.quantity);
    if (m.quantityAfter != null) pt.stock = m.quantityAfter;
  }

  // Forward-fill stock for days with no movements
  let lastStock: number | null = null;
  for (const pt of map.values()) {
    if (pt.stock !== null) lastStock = pt.stock;
    else if (lastStock !== null) pt.stock = lastStock;
  }

  return Array.from(map.values());
}

function formatDateTick(iso: string): string {
  return new Date(iso).toLocaleDateString("uk-UA", { day: "numeric", month: "short" });
}

// ── Summary cards ─────────────────────────────────────────────────────────────

function SummaryCard({
  icon, label, value, sub, color,
}: {
  icon: React.ReactNode; label: string; value: string | number; sub?: string; color: string;
}) {
  return (
    <div style={{
      background: "#0D1117", border: "1px solid #1F2937",
      borderRadius: 8, padding: "10px 14px",
      display: "flex", alignItems: "center", gap: 10,
    }}>
      <div style={{
        width: 32, height: 32, borderRadius: 7, flexShrink: 0,
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
        {sub && <div style={{ color: "#4B5563", fontSize: 10, marginTop: 1 }}>{sub}</div>}
      </div>
    </div>
  );
}

// ── Legend ────────────────────────────────────────────────────────────────────

function ChartLegend({
  showBuffers,
  hidden,
  onToggle,
}: {
  showBuffers: boolean;
  hidden: Set<string>;
  onToggle: (key: string) => void;
}) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 14 }}>
      {/* Clickable data lines */}
      <div style={{ display: "flex", flexWrap: "wrap", gap: "4px 12px" }}>
        {LINES.map(({ key, label, color, dash, width }) => {
          const off = hidden.has(key);
          return (
            <button
              key={key}
              onClick={() => onToggle(key)}
              title={off ? `Показати «${label}»` : `Приховати «${label}»`}
              style={{
                display: "flex", alignItems: "center", gap: 5,
                background: off ? "#0A0F1A" : "transparent",
                border: `1px solid ${off ? "#1F2937" : "transparent"}`,
                borderRadius: 6, padding: "3px 8px",
                cursor: "pointer", transition: "all 0.15s",
                opacity: off ? 0.45 : 1,
              }}
            >
              <svg width="24" height="10">
                <line
                  x1="0" y1="5" x2="24" y2="5"
                  stroke={off ? "#4B5563" : color}
                  strokeWidth={width}
                  strokeDasharray={dash ?? "none"}
                />
              </svg>
              <span style={{
                fontSize: 11,
                fontWeight: key === "stock" ? 600 : 400,
                color: off ? "#374151" : (key === "stock" ? "#9CA3AF" : "#6B7280"),
                textDecoration: off ? "line-through" : "none",
              }}>
                {label}
              </span>
            </button>
          );
        })}
      </div>

      {/* Buffer lines + zones (display-only, not toggleable) */}
      {showBuffers && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: "6px 16px", paddingTop: 6, borderTop: "1px solid #1F2937" }}>
          {BUFFER_LINES.map(({ key, label, color, dash }) => (
            <div key={key} style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <svg width="24" height="10">
                <line x1="0" y1="5" x2="24" y2="5" stroke={color} strokeWidth="1.5" strokeDasharray={dash} />
              </svg>
              <span style={{ color: "#6B7280", fontSize: 11 }}>{label}</span>
            </div>
          ))}
          {ZONES.map(({ label, color, fill }) => (
            <div key={label} style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <div style={{
                width: 14, height: 10, borderRadius: 2,
                background: fill, border: `1px solid ${color}50`,
              }} />
              <span style={{ color: "#6B7280", fontSize: 11 }}>{label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Tooltip ───────────────────────────────────────────────────────────────────

function CustomTooltip({ active, payload, label, buffers }: {
  active?: boolean;
  payload?: { dataKey: string; value: number | null; color: string }[];
  label?: string;
  buffers?: ProductBuffers;
}) {
  if (!active || !payload?.length) return null;

  const stock = payload.find((p) => p.dataKey === "stock")?.value ?? null;
  const movements = payload.filter((p) => p.dataKey !== "stock" && (p.value ?? 0) > 0);

  // Determine zone
  let zone: string | null = null;
  let zoneColor = "#6B7280";
  if (buffers && stock != null) {
    if (stock <= buffers.safetyBuffer) { zone = "Критична зона"; zoneColor = "#EF4444"; }
    else if (stock <= buffers.minStock) { zone = "Зона мінімуму"; zoneColor = "#FACC15"; }
    else if (stock <= buffers.maxStock) { zone = "Зона норми";    zoneColor = "#22C55E"; }
    else                               { zone = "Зона надлишку"; zoneColor = "#3B82F6"; }
  }

  return (
    <div style={{
      background: "#111827", border: "1px solid #1F2937",
      borderRadius: 10, padding: "10px 14px", fontSize: 12, minWidth: 180,
    }}>
      <div style={{ color: "#6B7280", fontSize: 11, marginBottom: 8, fontWeight: 600 }}>
        {label ? new Date(label).toLocaleDateString("uk-UA", { day: "numeric", month: "long", year: "numeric" }) : ""}
      </div>

      {stock != null && (
        <div style={{ display: "flex", justifyContent: "space-between", gap: 16, marginBottom: 6, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
          <span style={{ color: "#38BDF8", fontWeight: 600 }}>Залишок</span>
          <span style={{ color: "#38BDF8", fontFamily: "monospace", fontWeight: 700 }}>{stock}</span>
        </div>
      )}

      {zone && (
        <div style={{ display: "flex", justifyContent: "space-between", gap: 16, marginBottom: 6, paddingBottom: 6, borderBottom: "1px solid #1F2937" }}>
          <span style={{ color: "#4B5563" }}>Зона</span>
          <span style={{ color: zoneColor, fontWeight: 600, fontSize: 11 }}>{zone}</span>
        </div>
      )}

      {movements.map((p) => {
        const line = LINES.find((l) => l.key === p.dataKey);
        return (
          <div key={p.dataKey} style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#6B7280" }}>{line?.label ?? p.dataKey}</span>
            <span style={{ color: p.color, fontFamily: "monospace" }}>{p.value}</span>
          </div>
        );
      })}

      {buffers && stock != null && (
        <div style={{ marginTop: 8, paddingTop: 6, borderTop: "1px solid #1F2937", display: "flex", flexDirection: "column", gap: 3 }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#4B5563", fontSize: 10 }}>Буфер безпеки</span>
            <span style={{ color: "#F97316", fontFamily: "monospace", fontSize: 10 }}>{buffers.safetyBuffer}</span>
          </div>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#4B5563", fontSize: 10 }}>Мін. залишок</span>
            <span style={{ color: "#FACC15", fontFamily: "monospace", fontSize: 10 }}>{buffers.minStock}</span>
          </div>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 16 }}>
            <span style={{ color: "#4B5563", fontSize: 10 }}>Макс. залишок</span>
            <span style={{ color: "#34D399", fontFamily: "monospace", fontSize: 10 }}>{buffers.maxStock}</span>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Main component ────────────────────────────────────────────────────────────

export function ProductAnalyticsTab({
  productId,
  chartHeight = 220,
  buffers,
}: {
  productId: string;
  chartHeight?: number;
  buffers?: ProductBuffers;
}) {
  const [rangeDays, setRangeDays] = useState(30);
  const [hiddenLines, setHiddenLines] = useState<Set<string>>(new Set());

  function toggleLine(key: string) {
    setHiddenLines((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

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

  const totals = useMemo(() => ({
    receipt:    movements.filter((m) => m.movementType === "receipt").reduce((s, m) => s + m.quantity, 0),
    transfer:   movements.filter((m) => m.movementType === "transfer").reduce((s, m) => s + Math.abs(m.quantity), 0),
    write_off:  movements.filter((m) => m.movementType === "write_off").reduce((s, m) => s + Math.abs(m.quantity), 0),
    adjustment: movements.filter((m) => m.movementType === "adjustment").reduce((s, m) => s + Math.abs(m.quantity), 0),
  }), [movements]);

  // Current stock = last known quantityAfter in the period
  const currentStock = useMemo(() => {
    const sorted = [...movements].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
    return sorted.find((m) => m.quantityAfter != null)?.quantityAfter ?? null;
  }, [movements]);

  // Determine current zone
  const currentZone = useMemo(() => {
    if (!buffers || currentStock == null) return null;
    if (currentStock <= buffers.safetyBuffer) return { label: "Критична зона", color: "#EF4444" };
    if (currentStock <= buffers.minStock)     return { label: "Зона мінімуму", color: "#FACC15" };
    if (currentStock <= buffers.maxStock)     return { label: "Зона норми",    color: "#22C55E" };
    return                                           { label: "Зона надлишку", color: "#3B82F6" };
  }, [buffers, currentStock]);

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
      <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap" }}>
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
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(140px, 1fr))", gap: 8 }}>
        {currentStock != null && (
          <SummaryCard
            icon={<Package size={14} color="#38BDF8" />}
            label="Поточний залишок"
            value={currentStock}
            sub={currentZone?.label}
            color="#38BDF8"
          />
        )}
        <SummaryCard icon={<ArrowDownToLine size={14} color="#3B82F6" />} label="Поставки" value={totals.receipt}    color="#3B82F6" />
        <SummaryCard icon={<TrendingUp      size={14} color="#A78BFA" />} label="Передачі" value={totals.transfer}   color="#A78BFA" />
        <SummaryCard icon={<Trash2          size={14} color="#F87171" />} label="Списання" value={totals.write_off}  color="#F87171" />
        <SummaryCard icon={<RefreshCw       size={14} color="#4ADE80" />} label="Коригування" value={totals.adjustment} color="#4ADE80" />
      </div>

      {/* Chart */}
      <div style={{
        background: "#0D1117", border: "1px solid #1F2937",
        borderRadius: 10, padding: "16px 16px 8px",
      }}>
        <div style={{ display: "flex", alignItems: "baseline", gap: 8, marginBottom: 4 }}>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>Динаміка залишків</div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>{movements.length} подій за {rangeDays} дн.</div>
        </div>

        <ChartLegend showBuffers={!!buffers} hidden={hiddenLines} onToggle={toggleLine} />

        {movements.length === 0 ? (
          <div style={{ textAlign: "center", padding: "32px 0", color: "#374151", fontSize: 13 }}>
            Немає рухів за вибраний період
          </div>
        ) : (
          <ResponsiveContainer width="100%" height={chartHeight}>
            <LineChart data={chartData} margin={{ left: 0, right: 20, top: 4, bottom: 4 }}>

              {/* Zone backgrounds — rendered first so they're behind everything */}
              {buffers && (
                <>
                  <ReferenceArea y1={0}                    y2={buffers.safetyBuffer} fill="#EF444418" />
                  <ReferenceArea y1={buffers.safetyBuffer} y2={buffers.minStock}     fill="#FACC1514" />
                  <ReferenceArea y1={buffers.minStock}     y2={buffers.maxStock}     fill="#22C55E0C" />
                  <ReferenceArea y1={buffers.maxStock}     y2={99999}                fill="#3B82F60A" />
                </>
              )}

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
                width={36}
              />
              <Tooltip
                content={<CustomTooltip buffers={buffers} />}
              />

              {/* Buffer reference lines */}
              {buffers && (
                <>
                  <ReferenceLine
                    y={buffers.safetyBuffer}
                    stroke="#F97316" strokeWidth={1.5} strokeDasharray="4 2"
                    label={{ value: `Буфер: ${buffers.safetyBuffer}`, fill: "#F97316", fontSize: 10, position: "insideTopRight" }}
                  />
                  <ReferenceLine
                    y={buffers.minStock}
                    stroke="#FACC15" strokeWidth={1.5} strokeDasharray="4 2"
                    label={{ value: `Мін: ${buffers.minStock}`, fill: "#FACC15", fontSize: 10, position: "insideTopRight" }}
                  />
                  <ReferenceLine
                    y={buffers.maxStock}
                    stroke="#34D399" strokeWidth={1.5} strokeDasharray="4 2"
                    label={{ value: `Макс: ${buffers.maxStock}`, fill: "#34D399", fontSize: 10, position: "insideTopRight" }}
                  />
                </>
              )}

              {/* Stock balance line — most prominent */}
              <Line
                type="monotone"
                dataKey="stock"
                stroke="#38BDF8"
                strokeWidth={2.5}
                dot={false}
                activeDot={{ r: 4, fill: "#38BDF8" }}
                connectNulls
                hide={hiddenLines.has("stock")}
              />

              {/* Movement lines */}
              {LINES.filter((l) => l.key !== "stock").map(({ key, color, dash, width }) => (
                <Line
                  key={key}
                  type="monotone"
                  dataKey={key}
                  stroke={color}
                  strokeWidth={width}
                  strokeDasharray={dash}
                  dot={false}
                  activeDot={{ r: 3, fill: color }}
                  hide={hiddenLines.has(key)}
                />
              ))}
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>

      {/* Movement log */}
      {movements.length > 0 && (
        <div>
          <div style={{
            color: "#4B5563", fontSize: 11, fontWeight: 600,
            textTransform: "uppercase", letterSpacing: "0.05em",
            marginBottom: 8, paddingBottom: 6, borderBottom: "1px solid #1F2937",
          }}>
            Журнал подій
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4, maxHeight: 320, overflowY: "auto" }}>
            {movements.slice(0, 50).map((m) => {
              const line = LINES.find((l) => l.key === m.movementType);
              const isPlus = m.movementType === "receipt" || m.movementType === "adjustment";
              return (
                <div
                  key={m.id}
                  style={{
                    display: "flex", alignItems: "center", gap: 10,
                    padding: "7px 12px",
                    background: "#0A0F1A", border: "1px solid #1F2937",
                    borderRadius: 7,
                  }}
                >
                  <div style={{
                    width: 8, height: 8, borderRadius: "50%", flexShrink: 0,
                    background: line?.color ?? "#6B7280",
                  }} />

                  <span style={{
                    fontSize: 11, padding: "1px 7px", borderRadius: 4, flexShrink: 0,
                    background: `${line?.color ?? "#6B7280"}15`,
                    color: line?.color ?? "#6B7280",
                    fontWeight: 600,
                  }}>
                    {MOVEMENT_LABELS[m.movementType] ?? m.movementType}
                  </span>

                  <span style={{
                    color: "#9CA3AF", fontSize: 11, flex: 1, minWidth: 0,
                    overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
                  }}>
                    {m.fromStoreName && m.toStoreName
                      ? `${m.fromStoreName} → ${m.toStoreName}`
                      : m.fromStoreName ?? m.toStoreName ?? "—"}
                  </span>

                  {/* quantityBefore → quantityAfter */}
                  {m.quantityBefore != null && m.quantityAfter != null ? (
                    <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0, whiteSpace: "nowrap" }}>
                      <span style={{ fontFamily: "monospace" }}>{m.quantityBefore}</span>
                      <span style={{ margin: "0 4px" }}>→</span>
                      <span style={{ fontFamily: "monospace", color: "#38BDF8" }}>{m.quantityAfter}</span>
                    </span>
                  ) : m.quantityAfter != null ? (
                    <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0 }}>
                      залишок: <span style={{ fontFamily: "monospace", color: "#38BDF8" }}>{m.quantityAfter}</span>
                    </span>
                  ) : null}

                  <span style={{
                    fontFamily: "monospace", fontSize: 12, flexShrink: 0,
                    color: isPlus ? "#4ADE80" : "#F87171",
                  }}>
                    {isPlus ? `+${m.quantity}` : `−${Math.abs(m.quantity)}`}
                  </span>

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
