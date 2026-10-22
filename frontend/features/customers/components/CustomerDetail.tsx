"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useCustomer } from "../hooks/useCustomers";
import type { Customer, CustomerTransaction } from "../types";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { CustomerTierCard } from "./CustomerTierCard";
import { CustomerTicketsTab } from "./CustomerTicketsTab";
import { CustomerReviewsTab } from "./CustomerReviewsTab";
import { CustomerProfileHistoryTab } from "./CustomerProfileHistoryTab";
import { DetailDrawer } from "@/components/ui/DetailDrawer";
import { Tabs } from "@/components/ui/Tabs";
import { StatusPill } from "@/components/ui/StatusPill";

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
    <StatusPill bg={cfg.bg} text={cfg.color} style={{ border: `1px solid ${cfg.border}` }}>
      {label}
    </StatusPill>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface Props {
  customer: Customer;
  onClose: () => void;
  onEdit: () => void;
}

type DetailTab = "info" | "loyalty" | "tickets" | "reviews" | "history";

export function CustomerDetail({ customer, onClose, onEdit }: Props) {
  const t = useTranslations("Dashboard.customers.detail");
  const tTabs = useTranslations("Dashboard.customers.detail.tabs");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const uah = new Intl.NumberFormat(intlLocale, { style: "currency", currency: "UAH" });
  const { data: detail, isLoading } = useCustomer(customer.id);
  const [tab, setTab] = useState<DetailTab>("info");

  const transactionColumns: TableColumn<CustomerTransaction>[] = [
    {
      key: "date",
      header: t("txHeaderDate"),
      render: (tx) => (
        <span style={{ color: "#6B7280", fontSize: 12 }}>{formatDateTime(tx.createdAt, intlLocale)}</span>
      ),
    },
    {
      key: "amount",
      header: t("txHeaderAmount"),
      render: (tx) => (
        <span style={{ color: "#4ADE80", fontSize: 13, fontWeight: 500 }}>{uah.format(tx.totalAmount)}</span>
      ),
    },
    {
      key: "payment",
      header: t("txHeaderPayment"),
      render: (tx) => <PaymentTypeBadge type={tx.paymentType} />,
    },
    {
      key: "status",
      header: t("txHeaderStatus"),
      render: (tx) => <StatusBadge status={tx.status} />,
    },
  ];

  return (
    <DetailDrawer
      isOpen
      onClose={onClose}
      title={customer.name}
      actions={
        <Btn size="sm" onClick={onEdit}>
          {t("edit")}
        </Btn>
      }
    >
        <Tabs<DetailTab>
          value={tab}
          onChange={setTab}
          items={[
            { key: "info", label: tTabs("info") },
            { key: "loyalty", label: tTabs("loyalty") },
            { key: "tickets", label: tTabs("tickets"), count: detail?.openTicketCount || undefined },
            { key: "reviews", label: tTabs("reviews") },
            { key: "history", label: tTabs("history") },
          ]}
        />

        {/* Tab content */}
        {isLoading ? (
          <div style={{ color: "#374151", fontSize: 13, padding: "20px 0" }}>{tCommon("loading")}</div>
        ) : !detail ? null : (
          <>
            {tab === "info" && (
              <>
                {/* Info card */}
                <div
                  style={{
                    margin: "18px 0 0",
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
                <div style={{ margin: "18px 0 0" }}>
                  <h3 style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", margin: "0 0 10px" }}>
                    {t("recentTransactionsTitle")}
                  </h3>

                  <Table
                    columns={transactionColumns}
                    rows={detail.recentTransactions}
                    rowKey={(tx) => tx.id}
                    emptyMessage={t("noTransactions")}
                  />
                </div>
              </>
            )}

            {tab === "loyalty" && (
              <div style={{ margin: "18px 0 0" }}>
                <CustomerTierCard detail={detail} />
              </div>
            )}

            {tab === "tickets" && (
              <div style={{ margin: "18px 0 0" }}>
                <CustomerTicketsTab customerId={customer.id} openTicketCount={detail.openTicketCount} />
              </div>
            )}

            {tab === "reviews" && (
              <div style={{ margin: "18px 0 0" }}>
                <CustomerReviewsTab reviews={detail.recentReviews} />
              </div>
            )}

            {tab === "history" && (
              <div style={{ margin: "18px 0 0" }}>
                <CustomerProfileHistoryTab customerId={customer.id} active={tab === "history"} />
              </div>
            )}
          </>
        )}
    </DetailDrawer>
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
