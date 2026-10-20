"use client";

import { Fragment, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { ArrowLeft, Loader2 } from "lucide-react";
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
} from "recharts";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import {
  useCustomer,
  useCustomerActivity,
  useCustomerReceipts,
} from "@/features/customers/hooks/useCustomers";
import { CustomerTierCard } from "@/features/customers/components/CustomerTierCard";
import { CustomerTicketsTab } from "@/features/customers/components/CustomerTicketsTab";
import { CustomerReviewsTab } from "@/features/customers/components/CustomerReviewsTab";
import { CustomerProfileHistoryTab } from "@/features/customers/components/CustomerProfileHistoryTab";
import type {
  CustomerReceipt,
  CustomerStoreActivity,
  CustomerTopProduct,
} from "@/features/customers/types";

const RECEIPTS_PAGE_SIZE = 20;

type SectionTab = "loyalty" | "tickets" | "reviews" | "history";

const card: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: "18px 20px",
};

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h3 style={{
      color: "#9CA3AF", fontSize: 12, fontWeight: 600, textTransform: "uppercase",
      letterSpacing: "0.05em", margin: "0 0 12px",
    }}>
      {children}
    </h3>
  );
}

function Kpi({ label, value, sub, color }: { label: string; value: string; sub?: string; color?: string }) {
  return (
    <div style={{ ...card, padding: "14px 16px", display: "flex", flexDirection: "column", gap: 6 }}>
      <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
        {label}
      </span>
      <span style={{ color: color ?? "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace", lineHeight: 1.1 }}>
        {value}
      </span>
      {sub && <span style={{ color: "#4B5563", fontSize: 11 }}>{sub}</span>}
    </div>
  );
}

/**
 * Customer profile page: who the customer is, how long they have been with us, their loyalty rank
 * and the full purchase picture (receipts, stores, trend, top products) for per-customer analysis.
 */
export default function CustomerProfilePage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const t = useTranslations("Dashboard.customers.profile");
  const tTabs = useTranslations("Dashboard.customers.detail.tabs");
  const tPay = useTranslations("Dashboard.customers.paymentType");
  const locale = useLocale();
  const intl = locale === "en" ? "en-US" : "uk-UA";

  const { data: detail, isLoading } = useCustomer(id);
  const { data: activity } = useCustomerActivity(id);
  const [page, setPage] = useState(1);
  const { data: receipts, isFetching: receiptsFetching } = useCustomerReceipts(id, page, RECEIPTS_PAGE_SIZE);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [tab, setTab] = useState<SectionTab>("loyalty");

  const money = (v: number) => `${v.toLocaleString(intl, { maximumFractionDigits: 0 })} ₴`;
  const money2 = (v: number) => `${v.toLocaleString(intl, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`;
  const date = (iso: string) => new Date(iso).toLocaleDateString(intl, { day: "2-digit", month: "2-digit", year: "numeric" });
  const dateTime = (iso: string) =>
    new Date(iso).toLocaleString(intl, { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
  const num = (v: number, max = 2) => v.toLocaleString(intl, { maximumFractionDigits: max });

  if (isLoading) {
    return (
      <div style={{ display: "flex", justifyContent: "center", padding: "80px 0" }}>
        <Loader2 size={26} color="#374151" style={{ animation: "spin 1s linear infinite" }} />
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>
    );
  }
  if (!detail) {
    return <div style={{ padding: 40, textAlign: "center", color: "#F87171", fontSize: 13 }}>{t("notFound")}</div>;
  }

  const stats = activity?.stats;

  // ── tables ─────────────────────────────────────────────────────────────────────
  const storeColumns: TableColumn<CustomerStoreActivity>[] = [
    { key: "store", header: t("stores.store"), cellStyle: { color: "#E8EDF5", fontWeight: 500 }, render: (s) => s.storeName },
    { key: "receipts", header: t("stores.receipts"), render: (s) => s.receipts },
    { key: "spent", header: t("stores.spent"), cellStyle: { fontFamily: "monospace" }, render: (s) => money(s.spent) },
    {
      key: "share",
      header: t("stores.share"),
      render: (s) => (
        <div style={{ display: "flex", alignItems: "center", gap: 8, justifyContent: "center" }}>
          <div style={{ width: 70, height: 5, borderRadius: 3, background: "#161B26", overflow: "hidden" }}>
            <div style={{ width: `${Math.min(100, s.sharePercent)}%`, height: "100%", background: "#38BDF8" }} />
          </div>
          <span style={{ fontFamily: "monospace", fontSize: 12 }}>{num(s.sharePercent, 1)}%</span>
        </div>
      ),
    },
    { key: "first", header: t("stores.first"), cellStyle: { whiteSpace: "nowrap" }, render: (s) => date(s.firstAt) },
    { key: "last", header: t("stores.last"), cellStyle: { whiteSpace: "nowrap" }, render: (s) => date(s.lastAt) },
  ];

  const productColumns: TableColumn<CustomerTopProduct>[] = [
    { key: "name", header: t("products.name"), cellStyle: { color: "#E8EDF5", fontWeight: 500 }, render: (p) => p.name },
    { key: "qty", header: t("products.quantity"), cellStyle: { fontFamily: "monospace" }, render: (p) => num(p.quantity) },
    { key: "spent", header: t("products.spent"), cellStyle: { fontFamily: "monospace" }, render: (p) => money(p.spent) },
    { key: "receipts", header: t("products.receipts"), render: (p) => p.receipts },
    { key: "last", header: t("products.last"), cellStyle: { whiteSpace: "nowrap" }, render: (p) => date(p.lastAt) },
  ];

  const receiptColumns: TableColumn<CustomerReceipt>[] = [
    { key: "date", header: t("receipts.date"), cellStyle: { whiteSpace: "nowrap", color: "#E8EDF5" }, render: (r) => dateTime(r.createdAt) },
    { key: "store", header: t("receipts.store"), render: (r) => r.storeName },
    { key: "number", header: t("receipts.number"), cellStyle: { fontFamily: "monospace", fontSize: 12 }, render: (r) => r.receiptNumber },
    { key: "items", header: t("receipts.items"), render: (r) => r.lines.length },
    { key: "payment", header: t("receipts.payment"), render: (r) => (tPay.has(r.paymentType) ? tPay(r.paymentType) : r.paymentType) },
    { key: "total", header: t("receipts.total"), cellStyle: { fontFamily: "monospace", color: "#E8EDF5", fontWeight: 600 }, render: (r) => money2(r.total) },
  ];

  const tabBtn = (key: SectionTab): React.CSSProperties => ({
    background: "transparent", border: "none", cursor: "pointer", padding: "9px 14px", fontSize: 13, fontWeight: 600,
    color: tab === key ? "#E8EDF5" : "#6B7280",
    borderBottom: `2px solid ${tab === key ? "#3B82F6" : "transparent"}`,
  });

  return (
    <div style={{ padding: "24px 32px", display: "flex", flexDirection: "column", gap: 20, maxWidth: 1280 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", gap: 14, flexWrap: "wrap" }}>
        <button
          onClick={() => router.push("/customers")}
          style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", display: "flex", alignItems: "center", gap: 4, fontSize: 13, padding: "4px 0" }}
        >
          <ArrowLeft size={15} /> {t("back")}
        </button>

        <div style={{ flex: 1, minWidth: 260 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{detail.name}</h1>
            {detail.currentTierName && (
              <span style={{
                background: "#1D3461", color: "#93C5FD", border: "1px solid #1D4ED8",
                borderRadius: 999, padding: "2px 12px", fontSize: 12, fontWeight: 700,
              }}>
                {t("rank")}: {detail.currentTierName}
              </span>
            )}
            {detail.tags.map((tag) => (
              <span key={tag} style={{ background: "#0A1628", color: "#60A5FA", border: "1px solid #1D4ED8", borderRadius: 999, padding: "1px 9px", fontSize: 11 }}>
                {tag}
              </span>
            ))}
          </div>
          <div style={{ display: "flex", gap: 18, flexWrap: "wrap", marginTop: 8, color: "#9CA3AF", fontSize: 13 }}>
            <span>{t("phone")}: <span style={{ color: "#E8EDF5" }}>{detail.phone ?? "—"}</span></span>
            <span>{t("email")}: <span style={{ color: "#E8EDF5" }}>{detail.email ?? "—"}</span></span>
            <span>
              {t("customerSince")}: <span style={{ color: "#E8EDF5" }}>{date(detail.createdAt)}</span>
              {stats && <span style={{ color: "#4B5563" }}> · {t("daysAgo", { days: stats.daysSinceRegistered })}</span>}
            </span>
          </div>
          {detail.notes && <p style={{ color: "#6B7280", fontSize: 12, margin: "8px 0 0" }}>{detail.notes}</p>}
        </div>

        <Btn size="sm" onClick={() => router.push("/customers")}>{t("toList")}</Btn>
      </div>

      {/* KPIs */}
      {stats && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12 }}>
          <Kpi label={t("kpi.receipts")} value={num(stats.receiptCount, 0)} color="#60A5FA" />
          <Kpi label={t("kpi.totalSpent")} value={money(stats.totalSpent)} color="#4ADE80" />
          <Kpi label={t("kpi.averageCheck")} value={money2(stats.averageCheck)} color="#FBBF24" />
          <Kpi
            label={t("kpi.lastPurchase")}
            value={stats.lastPurchaseAt ? date(stats.lastPurchaseAt) : "—"}
            sub={stats.daysSinceLastPurchase != null ? t("daysAgo", { days: stats.daysSinceLastPurchase }) : undefined}
          />
          <Kpi
            label={t("kpi.frequency")}
            value={stats.averageDaysBetweenPurchases != null ? t("everyDays", { days: num(stats.averageDaysBetweenPurchases, 1) }) : "—"}
            sub={stats.firstPurchaseAt ? `${t("kpi.firstPurchase")}: ${date(stats.firstPurchaseAt)}` : undefined}
          />
          <Kpi label={t("kpi.last30")} value={money(stats.spent30d)} sub={t("receiptsCount", { count: stats.receipts30d })} />
          <Kpi label={t("kpi.last90")} value={money(stats.spent90d)} sub={t("receiptsCount", { count: stats.receipts90d })} />
          <Kpi label={t("kpi.items")} value={num(stats.itemsQuantity, 0)} />
        </div>
      )}

      {/* Monthly trend + stores */}
      {activity && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(420px, 1fr))", gap: 16 }}>
          <div style={card}>
            <SectionTitle>{t("trend.title")}</SectionTitle>
            {activity.months.length === 0 ? (
              <div style={{ color: "#4B5563", fontSize: 13, padding: "30px 0", textAlign: "center" }}>{t("trend.empty")}</div>
            ) : (
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={activity.months} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
                  <CartesianGrid stroke="#1F2937" vertical={false} />
                  <XAxis dataKey="month" tick={{ fill: "#6B7280", fontSize: 11 }} axisLine={false} tickLine={false} />
                  <YAxis tick={{ fill: "#6B7280", fontSize: 11 }} axisLine={false} tickLine={false} width={52} />
                  <Tooltip
                    cursor={{ fill: "#ffffff08" }}
                    contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, fontSize: 12 }}
                    formatter={(v, name) => [name === "spent" ? money(Number(v)) : v, name === "spent" ? t("trend.spent") : t("trend.receipts")]}
                    labelStyle={{ color: "#9CA3AF" }}
                  />
                  <Bar dataKey="spent" fill="#38BDF8" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </div>

          <div style={card}>
            <SectionTitle>{t("stores.title")}</SectionTitle>
            <Table
              columns={storeColumns}
              rows={activity.stores}
              rowKey={(s) => s.storeId}
              density="compact"
              minWidth={520}
              emptyMessage={t("stores.empty")}
            />
          </div>
        </div>
      )}

      {/* Top products */}
      {activity && (
        <div style={card}>
          <SectionTitle>{t("products.title")}</SectionTitle>
          <Table
            columns={productColumns}
            rows={activity.topProducts}
            rowKey={(p) => p.productId}
            density="compact"
            minWidth={560}
            emptyMessage={t("products.empty")}
          />
        </div>
      )}

      {/* Receipts */}
      <div style={card}>
        <SectionTitle>
          {t("receipts.title")}
          {receipts ? <span style={{ color: "#374151" }}> · {num(receipts.totalCount, 0)}</span> : null}
        </SectionTitle>
        <div style={{ opacity: receiptsFetching ? 0.6 : 1, transition: "opacity 0.15s" }}>
          <Table
            columns={receiptColumns}
            rows={receipts?.items ?? []}
            rowKey={(r) => r.id}
            density="compact"
            minWidth={680}
            onRowClick={(r) => setExpandedId((prev) => (prev === r.id ? null : r.id))}
            isRowSelected={(r) => r.id === expandedId}
            expandedRowKey={expandedId}
            renderExpanded={(r) => (
              <div style={{ padding: "10px 16px 14px 24px", background: "#0A0F1A" }}>
                <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                  <thead>
                    <tr style={{ color: "#4B5563", textAlign: "left" }}>
                      <th style={{ padding: "4px 8px" }}>{t("receipts.line.name")}</th>
                      <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("receipts.line.quantity")}</th>
                      <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("receipts.line.price")}</th>
                      <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("receipts.line.discount")}</th>
                      <th style={{ padding: "4px 8px", textAlign: "right" }}>{t("receipts.line.total")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {r.lines.map((l, i) => (
                      <Fragment key={`${l.productId}-${i}`}>
                        <tr style={{ color: "#9CA3AF", borderTop: "1px solid #1F2937" }}>
                          <td style={{ padding: "5px 8px", color: "#E8EDF5" }}>{l.name}</td>
                          <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace" }}>{num(l.quantity, 3)}</td>
                          <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace" }}>{money2(l.unitPrice)}</td>
                          <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace" }}>{l.discount ? money2(l.discount) : "—"}</td>
                          <td style={{ padding: "5px 8px", textAlign: "right", fontFamily: "monospace", color: "#E8EDF5" }}>{money2(l.total)}</td>
                        </tr>
                      </Fragment>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            page={page}
            totalPages={receipts ? Math.max(1, Math.ceil(receipts.totalCount / RECEIPTS_PAGE_SIZE)) : 1}
            totalCount={receipts?.totalCount}
            onPageChange={(p) => { setPage(p); setExpandedId(null); }}
            emptyMessage={t("receipts.empty")}
          />
        </div>
      </div>

      {/* Loyalty / support */}
      <div style={card}>
        <div style={{ display: "flex", gap: 2, borderBottom: "1px solid #1F2937", marginBottom: 16, overflowX: "auto" }}>
          <button style={tabBtn("loyalty")} onClick={() => setTab("loyalty")}>{tTabs("loyalty")}</button>
          <button style={tabBtn("tickets")} onClick={() => setTab("tickets")}>
            {tTabs("tickets")}{detail.openTicketCount > 0 ? ` (${detail.openTicketCount})` : ""}
          </button>
          <button style={tabBtn("reviews")} onClick={() => setTab("reviews")}>{tTabs("reviews")}</button>
          <button style={tabBtn("history")} onClick={() => setTab("history")}>{tTabs("history")}</button>
        </div>
        {tab === "loyalty" && <CustomerTierCard detail={detail} />}
        {tab === "tickets" && <CustomerTicketsTab customerId={detail.id} openTicketCount={detail.openTicketCount} />}
        {tab === "reviews" && <CustomerReviewsTab reviews={detail.recentReviews} />}
        {tab === "history" && <CustomerProfileHistoryTab customerId={detail.id} active={tab === "history"} />}
      </div>
    </div>
  );
}
