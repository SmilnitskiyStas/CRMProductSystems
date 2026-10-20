"use client";

import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { Check, Loader2 } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { useOrderTracking, useOrderTrackingSummary } from "@/features/order-tracking/hooks/useOrderTracking";
import { PostponeDialog } from "@/features/order-tracking/components/PostponeDialog";
import type {
  OrderTimelineStep,
  TrackedOrder,
  TrackedOrderSource,
  TrackedOrderStatus,
} from "@/features/order-tracking/types";

const PAGE_SIZE = 20;

const STATUS_COLOR: Record<TrackedOrderStatus, string> = {
  new: "#FBBF24",
  confirmed: "#60A5FA",
  in_transit: "#34D399",
  delivered: "#4ADE80",
  cancelled: "#F87171",
};

const STATUSES: TrackedOrderStatus[] = ["new", "confirmed", "in_transit", "delivered", "cancelled"];

const card: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "12px 16px",
};

export default function OrderTrackingPage() {
  const t = useTranslations("Dashboard.orderTracking");
  const locale = useLocale();
  const intl = locale === "en" ? "en-US" : "uk-UA";
  const storeId = usePrimaryStoreId();

  // Deep links from the dashboard: /orders/tracking?status=in_transit  and  /orders/tracking?overdue=1
  const searchParams = useSearchParams();
  const [statuses, setStatuses] = useState<TrackedOrderStatus[]>(() =>
    (searchParams.get("status") ?? "")
      .split(",")
      .filter((s): s is TrackedOrderStatus => (STATUSES as string[]).includes(s)),
  );
  const [overdueOnly, setOverdueOnly] = useState(() => searchParams.get("overdue") === "1");
  const [source, setSource] = useState<TrackedOrderSource | "">("");
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [expandedKey, setExpandedKey] = useState<string | null>(null);
  const [postponing, setPostponing] = useState<TrackedOrder | null>(null);

  // Debounce the search box so typing does not fire a request per keystroke.
  useEffect(() => {
    const id = setTimeout(() => { setSearch(searchInput.trim()); setPage(1); }, 350);
    return () => clearTimeout(id);
  }, [searchInput]);

  const { data, isLoading, isFetching } = useOrderTracking({
    source: source || undefined,
    statuses,
    storeId,
    search: search || undefined,
    overdue: overdueOnly || undefined,
    page,
    pageSize: PAGE_SIZE,
  });
  const { data: summary } = useOrderTrackingSummary(storeId);

  const date = (iso: string) => new Date(iso).toLocaleDateString(intl, { day: "2-digit", month: "2-digit", year: "numeric" });
  const dateTime = (iso: string) =>
    new Date(iso).toLocaleString(intl, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
  const money = (v: number) => `${v.toLocaleString(intl, { maximumFractionDigits: 0 })} ₴`;
  const num = (v: number) => v.toLocaleString(intl, { maximumFractionDigits: 2 });

  const toggleStatus = (s: TrackedOrderStatus) => {
    setStatuses((prev) => (prev.includes(s) ? prev.filter((x) => x !== s) : [...prev, s]));
    setPage(1);
    setExpandedKey(null);
  };

  const keyOf = (o: TrackedOrder) => `${o.source}:${o.id}`;

  function StatusBadge({ status }: { status: TrackedOrderStatus }) {
    const c = STATUS_COLOR[status];
    return (
      <span style={{
        background: `${c}1A`, color: c, border: `1px solid ${c}55`, borderRadius: 999,
        padding: "2px 10px", fontSize: 11, fontWeight: 700, whiteSpace: "nowrap",
      }}>
        {t(`status.${status}`)}
      </span>
    );
  }

  function Timeline({ order }: { order: TrackedOrder }) {
    const steps: OrderTimelineStep[] = order.timeline;
    return (
      <div style={{ display: "flex", alignItems: "flex-start", gap: 0, flexWrap: "wrap", margin: "4px 0 16px" }}>
        {steps.map((s, i) => {
          const done = !!s.at;
          const cancelled = s.key === "cancelled";
          const c = done ? (cancelled ? "#F87171" : "#4ADE80") : "#374151";
          return (
            <div key={s.key} style={{ display: "flex", alignItems: "flex-start" }}>
              <div style={{ display: "flex", flexDirection: "column", alignItems: "center", width: 150 }}>
                <div style={{
                  width: 26, height: 26, borderRadius: "50%", border: `2px solid ${c}`,
                  background: done ? `${c}22` : "transparent", display: "flex", alignItems: "center", justifyContent: "center",
                }}>
                  {done ? <Check size={14} color={c} /> : null}
                </div>
                <div style={{ color: done ? "#E8EDF5" : "#4B5563", fontSize: 12, fontWeight: 600, marginTop: 6, textAlign: "center" }}>
                  {t(`timeline.${s.key}`)}
                </div>
                <div style={{ color: "#6B7280", fontSize: 11, textAlign: "center", minHeight: 30 }}>
                  {s.at ? dateTime(s.at) : t("timeline.pending")}
                  {s.by && <div>{s.by}</div>}
                </div>
              </div>
              {i < steps.length - 1 && (
                <div style={{ width: 40, height: 2, background: steps[i + 1].at ? "#4ADE80" : "#1F2937", marginTop: 13 }} />
              )}
            </div>
          );
        })}
      </div>
    );
  }

  const columns: TableColumn<TrackedOrder>[] = [
    {
      key: "created",
      header: t("columns.created"),
      align: "left",
      cellStyle: { whiteSpace: "nowrap", color: "#E8EDF5" },
      render: (o) => date(o.createdAt),
    },
    {
      key: "source",
      header: t("columns.source"),
      render: (o) => (
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 12 }}>{t(`source.${o.source}`)}</div>
          {o.number && <div style={{ color: "#4B5563", fontFamily: "monospace", fontSize: 11 }}>{o.number}</div>}
        </div>
      ),
    },
    { key: "counterparty", header: t("columns.supplier"), cellStyle: { color: "#E8EDF5" }, render: (o) => o.counterparty ?? "—" },
    { key: "store", header: t("columns.store"), render: (o) => o.storeName ?? "—" },
    { key: "status", header: t("columns.status"), render: (o) => <StatusBadge status={o.status} /> },
    {
      key: "expected",
      header: t("columns.expected"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (o) =>
        o.expectedAt ? (
          <span style={{ color: o.isOverdue ? "#F87171" : "#E8EDF5" }}>
            {date(o.expectedAt)}
            {o.isOverdue && o.daysOverdue != null && (
              <span style={{ display: "block", fontSize: 11 }}>{t("overdueDays", { days: o.daysOverdue })}</span>
            )}
          </span>
        ) : (
          <span style={{ color: "#4B5563" }}>—</span>
        ),
    },
    {
      key: "delay",
      header: t("columns.delay"),
      render: (o) => (
        <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 4 }}>
          {o.postponeCount > 0 && (
            <span style={{ background: "#FBBF241A", color: "#FBBF24", border: "1px solid #FBBF2455", borderRadius: 999, padding: "1px 8px", fontSize: 11, fontWeight: 700, whiteSpace: "nowrap" }}>
              {t("postponedBadge", { count: o.postponeCount })}
            </span>
          )}
          {o.canPostpone && (o.isOverdue || o.postponeCount > 0) && (
            <button
              onClick={(e) => { e.stopPropagation(); setPostponing(o); }}
              style={{ background: "transparent", border: "none", color: "#60A5FA", cursor: "pointer", fontSize: 12, padding: 0, whiteSpace: "nowrap" }}
            >
              {t("postponeAction")}
            </button>
          )}
        </div>
      ),
    },
    { key: "qty", header: t("columns.quantity"), cellStyle: { fontFamily: "monospace" }, render: (o) => num(o.totalQuantity) },
    {
      key: "amount",
      header: t("columns.amount"),
      cellStyle: { fontFamily: "monospace" },
      render: (o) => (o.totalAmount > 0 ? money(o.totalAmount) : "—"),
    },
  ];

  return (
    <div style={{ padding: "28px 32px" }}>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, margin: "6px 0 0" }}>{t("subtitle")}</p>
      </div>

      {/* KPI / quick status filters */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(150px, 1fr))", gap: 12, marginBottom: 18 }}>
        <div style={card}>
          <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase" }}>{t("kpi.open")}</div>
          <div style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700 }}>{summary?.open ?? "—"}</div>
        </div>
        {STATUSES.map((s) => (
          <button
            key={s}
            onClick={() => toggleStatus(s)}
            style={{
              ...card, textAlign: "left", cursor: "pointer",
              borderColor: statuses.includes(s) ? STATUS_COLOR[s] : "#1F2937",
              background: statuses.includes(s) ? `${STATUS_COLOR[s]}12` : "#0D1117",
            }}
          >
            <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase" }}>{t(`status.${s}`)}</div>
            <div style={{ color: STATUS_COLOR[s], fontSize: 22, fontWeight: 700 }}>
              {summary ? ({ new: summary.new, confirmed: summary.confirmed, in_transit: summary.inTransit, delivered: summary.delivered, cancelled: summary.cancelled })[s] : "—"}
            </div>
          </button>
        ))}
        <button
          onClick={() => { setOverdueOnly((v) => !v); setPage(1); setExpandedKey(null); }}
          style={{
            ...card, textAlign: "left", cursor: "pointer",
            borderColor: overdueOnly ? "#F87171" : summary && summary.overdue > 0 ? "#7F1D1D" : "#1F2937",
            background: overdueOnly ? "#F8717112" : "#0D1117",
          }}
        >
          <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase" }}>{t("kpi.overdue")}</div>
          <div style={{ color: summary && summary.overdue > 0 ? "#F87171" : "#E8EDF5", fontSize: 22, fontWeight: 700 }}>
            {summary?.overdue ?? "—"}
          </div>
        </button>
      </div>

      {/* Filters */}
      <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center", marginBottom: 14 }}>
        <input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder={t("searchPlaceholder")}
          style={{ ...field, width: 280 }}
        />
        <select
          value={source}
          onChange={(e) => { setSource(e.target.value as TrackedOrderSource | ""); setPage(1); }}
          style={{ ...field, cursor: "pointer" }}
        >
          <option value="">{t("source.all")}</option>
          <option value="receipt">{t("source.receipt")}</option>
          <option value="marketplace">{t("source.marketplace")}</option>
        </select>
        {(statuses.length > 0 || overdueOnly) && (
          <button onClick={() => { setStatuses([]); setOverdueOnly(false); setPage(1); }} style={{ background: "transparent", border: "none", color: "#60A5FA", cursor: "pointer", fontSize: 13 }}>
            {t("resetStatuses")}
          </button>
        )}
        {isFetching && <Loader2 size={15} color="#60A5FA" style={{ animation: "spin 1s linear infinite" }} />}
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>

      {postponing && <PostponeDialog order={postponing} onClose={() => setPostponing(null)} />}

      <Table
        columns={columns}
        rows={data?.items ?? []}
        rowKey={keyOf}
        density="compact"
        minWidth={900}
        isLoading={isLoading}
        onRowClick={(o) => setExpandedKey((prev) => (prev === keyOf(o) ? null : keyOf(o)))}
        isRowSelected={(o) => keyOf(o) === expandedKey}
        expandedRowKey={expandedKey}
        renderExpanded={(o) => (
          <div style={{ padding: "16px 20px", background: "#0A0F1A" }}>
            <Timeline order={o} />
            {o.postponeCount > 0 && o.expectedAt && (
              <div style={{ color: "#FBBF24", fontSize: 12, marginBottom: 8 }}>
                {t("postponedInfo", {
                  count: o.postponeCount,
                  from: o.originalExpectedAt ? date(o.originalExpectedAt) : "—",
                  to: date(o.expectedAt),
                })}
              </div>
            )}
            {o.note && (
              <div style={{ color: "#FBBF24", fontSize: 12, marginBottom: 12 }}>{t("note")}: {o.note}</div>
            )}
            {o.canPostpone ? (
              <div style={{ marginBottom: 12 }}>
                <button
                  onClick={() => setPostponing(o)}
                  style={{ background: "#1D3461", border: "1px solid #3B82F6", color: "#93C5FD", borderRadius: 8, padding: "6px 14px", fontSize: 12, fontWeight: 600, cursor: "pointer" }}
                >
                  {t("postponeAction")}
                </button>
              </div>
            ) : o.isOverdue && o.source === "marketplace" ? (
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 12 }}>{t("supplierDateHint")}</div>
            ) : null}
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr style={{ color: "#4B5563", textAlign: "left" }}>
                  <th style={{ padding: "4px 8px" }}>{t("lines.name")}</th>
                  <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("lines.quantity")}</th>
                  <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("lines.received")}</th>
                  <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("lines.amount")}</th>
                </tr>
              </thead>
              <tbody>
                {o.lines.map((l, i) => (
                  <tr key={i} style={{ color: "#9CA3AF", borderTop: "1px solid #1F2937" }}>
                    <td style={{ padding: "5px 8px", color: "#E8EDF5" }}>{l.name}</td>
                    <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace" }}>
                      {num(l.quantity)}{l.unit ? ` ${l.unit}` : ""}
                    </td>
                    <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace" }}>
                      {l.quantityReceived != null ? num(l.quantityReceived) : "—"}
                    </td>
                    <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace" }}>
                      {l.amount != null ? money(l.amount) : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        page={data?.page ?? 1}
        totalPages={data?.totalPages ?? 1}
        totalCount={data?.totalCount}
        onPageChange={(p) => { setPage(p); setExpandedKey(null); window.scrollTo(0, 0); }}
        emptyMessage={isLoading ? t("loading") : t("empty")}
      />
    </div>
  );
}

const field: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
};
