"use client";

import { useEffect, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useCustomer } from "../hooks/useCustomers";
import type { Customer, CustomerTransaction } from "../types";
import { Btn } from "@/components/ui/Btn";
import { CustomerTierCard } from "./CustomerTierCard";
import { CustomerTicketsTab } from "./CustomerTicketsTab";
import { CustomerReviewsTab } from "./CustomerReviewsTab";
import { CustomerProfileHistoryTab } from "./CustomerProfileHistoryTab";

function formatDate(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

function formatDateTime(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function PaymentTypeBadge({ type }: { type: string }) {
  const t = useTranslations("Dashboard.customers.paymentType");
  return (
    <span style={{ color: "#9CA3AF", fontSize: 12 }}>
      {t.has(type) ? t(type) : type}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const t = useTranslations("Dashboard.customers.transactionStatus");
  const configs: Record<string, { bg: string; color: string; border: string }> = {
    completed: { bg: "#0d2318", color: "#4ADE80", border: "#14532D" },
    pending:   { bg: "#1c1a0a", color: "#FCD34D", border: "#78350F" },
    cancelled: { bg: "#2d0a0a", color: "#F87171", border: "#7F1D1D" },
    refunded:  { bg: "#0a1628", color: "#60A5FA", border: "#1D4ED8" },
  };
  const cfg = configs[status] ?? { bg: "#111827", color: "#6B7280", border: "#374151" };
  const label = t.has(status) ? t(status) : status;
  return (
    <span
      style={{
        background: cfg.bg,
        border: `1px solid ${cfg.border}`,
        borderRadius: 20,
        padding: "2px 10px",
        color: cfg.color,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {label}
    </span>
  );
}

function TransactionRow({ tx }: { tx: CustomerTransaction }) {
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const uah = new Intl.NumberFormat(intlLocale, { style: "currency", currency: "UAH" });
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "1fr 120px 100px 130px",
        padding: "10px 14px",
        borderBottom: "1px solid #0F1924",
        alignItems: "center",
      }}
    >
      <div style={{ color: "#6B7280", fontSize: 12 }}>{formatDateTime(tx.createdAt, intlLocale)}</div>
      <div style={{ color: "#4ADE80", fontSize: 13, fontWeight: 500 }}>{uah.format(tx.totalAmount)}</div>
      <PaymentTypeBadge type={tx.paymentType} />
      <div><StatusBadge status={tx.status} /></div>
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface Props {
  customer: Customer;
  onClose: () => void;
  onEdit: () => void;
}

type DetailTab = "info" | "loyalty" | "tickets" | "reviews" | "history";

const TAB_STYLE = (active: boolean): React.CSSProperties => ({
  background: "transparent",
  border: "none",
  borderBottom: `2px solid ${active ? "#3B82F6" : "transparent"}`,
  color: active ? "#93C5FD" : "#4B5563",
  fontSize: 12,
  fontWeight: active ? 600 : 400,
  padding: "9px 12px",
  cursor: "pointer",
  transition: "color 0.15s, border-color 0.15s",
  whiteSpace: "nowrap",
});

export function CustomerDetail({ customer, onClose, onEdit }: Props) {
  const t = useTranslations("Dashboard.customers.detail");
  const tTabs = useTranslations("Dashboard.customers.detail.tabs");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const uah = new Intl.NumberFormat(intlLocale, { style: "currency", currency: "UAH" });
  const { data: detail, isLoading } = useCustomer(customer.id);
  const [tab, setTab] = useState<DetailTab>("info");

  // Close on Escape
  useEffect(() => {
    function handler(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.5)",
          zIndex: 200,
        }}
      />

      {/* Drawer */}
      <div
        style={{
          position: "fixed",
          top: 0,
          right: 0,
          bottom: 0,
          width: "min(520px, 96vw)",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 201,
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {customer.name}
          </h2>
          <div style={{ display: "flex", gap: 8 }}>
            <Btn size="sm" onClick={onEdit}>
              {t("edit")}
            </Btn>
            <button
              onClick={onClose}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 8,
                padding: "5px 9px",
                color: "#4B5563",
                fontSize: 16,
                cursor: "pointer",
              }}
            >
              ✕
            </button>
          </div>
        </div>

        {/* Tabs */}
        <div
          style={{
            display: "flex",
            gap: 2,
            padding: "0 22px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
            overflowX: "auto",
          }}
        >
          <button style={TAB_STYLE(tab === "info")} onClick={() => setTab("info")}>
            {tTabs("info")}
          </button>
          <button style={TAB_STYLE(tab === "loyalty")} onClick={() => setTab("loyalty")}>
            {tTabs("loyalty")}
          </button>
          <button style={TAB_STYLE(tab === "tickets")} onClick={() => setTab("tickets")}>
            {tTabs("tickets")}
            {!!detail?.openTicketCount && (
              <span
                style={{
                  marginLeft: 6,
                  background: "#78350F",
                  color: "#FCD34D",
                  borderRadius: 999,
                  padding: "1px 6px",
                  fontSize: 10,
                  fontWeight: 700,
                }}
              >
                {detail.openTicketCount}
              </span>
            )}
          </button>
          <button style={TAB_STYLE(tab === "reviews")} onClick={() => setTab("reviews")}>
            {tTabs("reviews")}
          </button>
          <button style={TAB_STYLE(tab === "history")} onClick={() => setTab("history")}>
            {tTabs("history")}
          </button>
        </div>

        {/* Tab content */}
        {isLoading ? (
          <div style={{ color: "#374151", fontSize: 13, padding: "20px 22px" }}>{tCommon("loading")}</div>
        ) : !detail ? null : (
          <>
            {tab === "info" && (
              <>
                {/* Info card */}
                <div
                  style={{
                    margin: "18px 22px 0",
                    background: "#0A1020",
                    border: "1px solid #1F2937",
                    borderRadius: 10,
                    padding: 16,
                  }}
                >
                  <div
                    style={{
                      display: "grid",
                      gridTemplateColumns: "1fr 1fr",
                      gap: "14px 20px",
                    }}
                  >
                    <InfoField label={t("phoneLabel")}     value={customer.phone  ?? "—"} />
                    <InfoField label={t("emailLabel")}       value={customer.email  ?? "—"} />
                    <InfoField label={t("ordersLabel")}   value={String(customer.totalOrders)} />
                    <InfoField label={t("spentLabel")}   value={uah.format(customer.totalSpent)} valueColor="#4ADE80" />
                    <InfoField label={t("registeredLabel")} value={formatDate(customer.createdAt, intlLocale)} />
                    {customer.notes && (
                      <div style={{ gridColumn: "1 / -1" }}>
                        <InfoField label={t("notesLabel")} value={customer.notes} />
                      </div>
                    )}
                  </div>

                  {/* Tags */}
                  {customer.tags.length > 0 && (
                    <div style={{ marginTop: 12 }}>
                      <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
                        {t("tagsLabel")}
                      </div>
                      <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                        {customer.tags.map((tag) => (
                          <span
                            key={tag}
                            style={{
                              background: "#0a1628",
                              border: "1px solid #1D4ED8",
                              borderRadius: 20,
                              padding: "2px 10px",
                              color: "#60A5FA",
                              fontSize: 12,
                            }}
                          >
                            {tag}
                          </span>
                        ))}
                      </div>
                    </div>
                  )}
                </div>

                {/* Recent transactions */}
                <div style={{ margin: "18px 22px 22px" }}>
                  <h3 style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", margin: "0 0 10px" }}>
                    {t("recentTransactionsTitle")}
                  </h3>

                  {detail.recentTransactions.length === 0 ? (
                    <div style={{ color: "#374151", fontSize: 13, padding: "20px 0" }}>{t("noTransactions")}</div>
                  ) : (
                    <div
                      style={{
                        background: "#0A1020",
                        border: "1px solid #1F2937",
                        borderRadius: 10,
                        overflow: "hidden",
                      }}
                    >
                      {/* Tx header */}
                      <div
                        style={{
                          display: "grid",
                          gridTemplateColumns: "1fr 120px 100px 130px",
                          padding: "8px 14px",
                          borderBottom: "1px solid #1F2937",
                          background: "#060D18",
                        }}
                      >
                        {[t("txHeaderDate"), t("txHeaderAmount"), t("txHeaderPayment"), t("txHeaderStatus")].map((h, i) => (
                          <div key={i} style={{ color: "#374151", fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
                            {h}
                          </div>
                        ))}
                      </div>
                      {detail.recentTransactions.map((tx) => (
                        <TransactionRow key={tx.id} tx={tx} />
                      ))}
                    </div>
                  )}
                </div>
              </>
            )}

            {tab === "loyalty" && (
              <div style={{ margin: "18px 22px 22px" }}>
                <CustomerTierCard detail={detail} />
              </div>
            )}

            {tab === "tickets" && (
              <div style={{ margin: "18px 22px 22px" }}>
                <CustomerTicketsTab customerId={customer.id} openTicketCount={detail.openTicketCount} />
              </div>
            )}

            {tab === "reviews" && (
              <div style={{ margin: "18px 22px 22px" }}>
                <CustomerReviewsTab reviews={detail.recentReviews} />
              </div>
            )}

            {tab === "history" && (
              <div style={{ margin: "18px 22px 22px" }}>
                <CustomerProfileHistoryTab customerId={customer.id} active={tab === "history"} />
              </div>
            )}
          </>
        )}
      </div>
    </>
  );
}

// ── Helper ─────────────────────────────────────────────────────────────────────

function InfoField({
  label,
  value,
  valueColor,
}: {
  label: string;
  value: string;
  valueColor?: string;
}) {
  return (
    <div>
      <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 3 }}>
        {label}
      </div>
      <div style={{ color: valueColor ?? "#E8EDF5", fontSize: 13 }}>
        {value}
      </div>
    </div>
  );
}
