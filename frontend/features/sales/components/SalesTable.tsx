"use client";

import { useRouter } from "next/navigation";
import { BarChart2, AlertTriangle } from "lucide-react";
import { useTranslations } from "next-intl";
import type { DailySale, SalesSortBy } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { Table, type TableColumn } from "@/components/ui/Table";

interface Props {
  sales: DailySale[];
  onToggleAnomaly: (id: string, isAnomaly: boolean) => void;
  sortBy?: SalesSortBy;
  sortDescending?: boolean;
  onSort?: (key: SalesSortBy) => void;
}

export function SalesTable({ sales, onToggleAnomaly, sortBy, sortDescending, onSort }: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.sales.table");

  const sourceLabels: Record<string, string> = {
    manual: t("source.manual"),
    pos: t("source.pos"),
    import: t("source.import"),
  };

  const columns: TableColumn<DailySale>[] = [
    {
      // Column 0 is the label column (left-aligned, same look as Catalog's name column).
      key: "product",
      header: t("headers.product"),
      sortKey: "product",
      cellStyle: { color: "#E8EDF5", fontWeight: 500, minWidth: 160 },
      render: (s) => (
        <span
          title={s.productName}
          style={{
            display: "-webkit-box",
            WebkitLineClamp: 2,
            WebkitBoxOrient: "vertical",
            overflow: "hidden",
            wordBreak: "break-word",
          }}
        >
          {s.productName}
        </span>
      ),
    },
    {
      key: "date",
      header: t("headers.date"),
      sortKey: "date",
      cellStyle: { fontFamily: "monospace", color: "#E8EDF5", whiteSpace: "nowrap" },
      render: (s) => s.date,
    },
    {
      key: "barcode",
      header: t("headers.barcode"),
      cellStyle: { fontFamily: "monospace", fontSize: 12, color: "#4B5563" },
      render: (s) => s.barcode ?? "—",
    },
    {
      key: "store",
      header: t("headers.store"),
      sortKey: "store",
      cellStyle: { minWidth: 120 },
      render: (s) => (
        <span
          title={s.storeName}
          style={{ display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical", overflow: "hidden", wordBreak: "break-word" }}
        >
          {s.storeName}
        </span>
      ),
    },
    {
      key: "sold",
      header: t("headers.sold"),
      sortKey: "sold",
      cellStyle: { fontFamily: "monospace", color: "#E8EDF5" },
      render: (s) => s.quantitySold,
    },
    {
      key: "eod",
      header: t("headers.eod"),
      sortKey: "eod",
      render: (s) => s.quantityEndOfDay ?? "—",
    },
    {
      key: "source",
      header: t("headers.source"),
      sortKey: "source",
      cellStyle: { color: "#6B7280", fontSize: 12 },
      render: (s) => sourceLabels[s.source] ?? s.source,
    },
    {
      key: "marks",
      header: t("headers.marks"),
      render: (s) => (
        <>
          {s.isPromoDay && (
            <span style={{
              background: "#7C2D12", color: "#FDBA74", fontSize: 11,
              borderRadius: 6, padding: "2px 8px", marginRight: 6,
            }}>{t("promoTag")}</span>
          )}
          {s.isAnomaly && (
            <span style={{
              background: "#7F1D1D", color: "#FCA5A5", fontSize: 11,
              borderRadius: 6, padding: "2px 8px",
            }}>{t("anomalyTag")}</span>
          )}
        </>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (s) => (
        <ActionMenu
          items={[
            {
              label: s.isAnomaly ? t("actionMenu.includeInAdu") : t("actionMenu.markAnomaly"),
              icon: <AlertTriangle size={13} />,
              onClick: () => onToggleAnomaly(s.id, !s.isAnomaly),
            },
            { separator: true },
            {
              label: t("actionMenu.productAnalytics"),
              icon: <BarChart2 size={13} />,
              onClick: () => router.push(`/inventory/${s.productId}?tab=analytics`),
              disabled: !s.productId,
            },
          ]}
        />
      ),
    },
  ];

  return (
    <Table
      columns={columns}
      rows={sales}
      rowKey={(s) => s.id}
      sortBy={sortBy}
      sortDescending={sortDescending}
      onSort={(k) => onSort?.(k as SalesSortBy)}
      density="compact"
      minWidth={900}
      emptyMessage={t("empty")}
      rowStyle={(s) => (s.isAnomaly ? { opacity: 0.45 } : {})}
    />
  );
}
