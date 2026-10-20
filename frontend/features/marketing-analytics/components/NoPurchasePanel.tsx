"use client";

import { useTranslations, useLocale } from "next-intl";
import { X } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useNoPurchaseCustomers } from "../hooks/useMarketingAnalytics";
import type { NoPurchaseCustomerRowDto } from "../types";

const LIMIT = 100;

interface Props {
  storeIds: string[];
  /** Count shown on the card — used for the "showing first N of M" note. */
  totalCount: number;
  enabled: boolean;
  onClose: () => void;
}

/**
 * Drill-down for the "Без покупок" card: registered customers who never purchased in the selected
 * stores. Contact data is always masked here (no unmask escape hatch on screen, same rule as the
 * store-migration list).
 */
export function NoPurchasePanel({ storeIds, totalCount, enabled, onClose }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.noPurchase");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: rows = [], isLoading } = useNoPurchaseCustomers(storeIds, LIMIT, enabled);

  const columns: TableColumn<NoPurchaseCustomerRowDto>[] = [
    { key: "name", header: t("headerName"), cellStyle: { color: "#E8EDF5" }, render: (r) => r.name },
    { key: "phone", header: t("headerPhone"), cellStyle: { fontFamily: "monospace", fontSize: 12 }, render: (r) => r.phone ?? "—" },
    { key: "email", header: t("headerEmail"), cellStyle: { fontFamily: "monospace", fontSize: 12 }, render: (r) => r.email ?? "—" },
    {
      key: "registered",
      header: t("headerRegistered"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (r) => new Date(r.registeredAt).toLocaleDateString(intlLocale),
    },
  ];

  return (
    <section style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 20, marginTop: 20 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 12, marginBottom: 14 }}>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "4px 0 0", maxWidth: 640 }}>{t("description")}</p>
        </div>
        <button
          onClick={onClose}
          aria-label={t("close")}
          style={{ background: "transparent", border: "1px solid #1F2937", borderRadius: 6, color: "#6B7280", cursor: "pointer", padding: 6, display: "flex" }}
        >
          <X size={14} />
        </button>
      </div>

      <Table
        columns={columns}
        rows={rows}
        rowKey={(r) => r.customerId}
        density="compact"
        minWidth={600}
        isLoading={isLoading}
        emptyMessage={isLoading ? t("loading") : t("empty")}
      />

      {!isLoading && totalCount > rows.length && rows.length > 0 && (
        <p style={{ color: "#4B5563", fontSize: 11, margin: "10px 0 0" }}>
          {t("limitNote", { shown: rows.length.toLocaleString(intlLocale), total: totalCount.toLocaleString(intlLocale) })}
        </p>
      )}
    </section>
  );
}
