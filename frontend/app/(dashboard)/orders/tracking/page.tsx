"use client";

import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { Check, Loader2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatusPill } from "@/components/ui/StatusPill";
import { filterInputStyle } from "@/components/ui/fieldStyles";
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
      <StatusPill bg={`${c}1A`} text={c} style={{ border: `1px solid ${c}55`, fontWeight: 700 }}>
        {t(`status.${status}`)}
      </StatusPill>
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

  type TrackedLine = TrackedOrder["lines"][number] & { _i: number };
  const lineColumns: TableColumn<TrackedLine>[] = [
    { key: "name", header: t("lines.name"), cellStyle: { color: "#E8EDF5" }, render: (l) => l.name },
    {
      key: "quantity",
      header: t("lines.quantity"),
      align: "right",
      cellStyle: { fontFamily: "monospace" },
      render: (l) => `${num(l.quantity)}${l.unit ? ` ${l.unit}` : ""}`,
    },
    {
      key: "received",
      header: t("lines.received"),
      align: "right",
      cellStyle: { fontFamily: "monospace" },
      render: (l) => (l.quantityReceived != null ? num(l.quantityReceived) : "—"),
    },
    {
      key: "amount",
      header: t("lines.amount"),
      align: "right",
      cellStyle: { fontFamily: "monospace" },
      render: (l) => (l.amount != null ? money(l.amount) : "—"),
    },
  ];

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
            <StatusPill bg="#FBBF241A" text="#FBBF24" style={{ border: "1px solid #FBBF2455", fontWeight: 700 }}>
              {t("postponedBadge", { count: o.postponeCount })}
            </StatusPill>
          )}
          {o.canPostpone && (o.isOverdue || o.postponeCount > 0) && (
            <Btn variant="link" size="sm" onClick={(e) => { e.stopPropagation(); setPostponing(o); }}>
              {t("postponeAction")}
            </Btn>
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
    <PageContainer>
      <PageHeader title={t("title")} subtitle={t("subtitle")} />

      {/* KPI / quick status filters */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(150px, 1fr))", gap: 12 }}>
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
      <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
        <input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder={t("searchPlaceholder")}
          style={{ ...filterInputStyle, width: 280 }}
        />
        <select
          value={source}
          onChange={(e) => { setSource(e.target.value as TrackedOrderSource | ""); setPage(1); }}
          style={{ ...filterInputStyle, cursor: "pointer" }}
        >
          <option value="">{t("source.all")}</option>
          <option value="receipt">{t("source.receipt")}</option>
          <option value="marketplace">{t("source.marketplace")}</option>
        </select>
        {(statuses.length > 0 || overdueOnly) && (
          <Btn variant="link" onClick={() => { setStatuses([]); setOverdueOnly(false); setPage(1); }}>
            {t("resetStatuses")}
          </Btn>
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
                <Btn size="sm" onClick={() => setPostponing(o)}>
                  {t("postponeAction")}
                </Btn>
              </div>
            ) : o.isOverdue && o.source === "marketplace" ? (
              <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 12 }}>{t("supplierDateHint")}</div>
            ) : null}
            <Table columns={lineColumns} rows={o.lines.map((l, i) => ({ ...l, _i: i }))} rowKey={(l) => String(l._i)} />
          </div>
        )}
        page={data?.page ?? 1}
        totalPages={data?.totalPages ?? 1}
        totalCount={data?.totalCount}
        onPageChange={(p) => { setPage(p); setExpandedKey(null); window.scrollTo(0, 0); }}
        emptyMessage={isLoading ? t("loading") : t("empty")}
      />
    </PageContainer>
  );
}
