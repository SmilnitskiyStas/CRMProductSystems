"use client";

import { useState } from "react";
import { Eye, ShieldCheck, BarChart2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import type { ProductStockDto, BatchStatus, StockSortBy } from "../types";
import { STATUS_COLOR } from "../types";
import { StatusBadge } from "./StatusBadge";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { Table, type TableColumn } from "@/components/ui/Table";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

interface Props {
  items: ProductStockDto[];
  isLoading: boolean;
  selectedIds: Set<string>;
  onSelectId: (id: string, checked: boolean) => void;
  onSelectAll: (checked: boolean) => void;
  onVerify?: (id: string) => void;
  sortBy: StockSortBy;
  sortDescending: boolean;
  onSort: (key: StockSortBy) => void;
}

const CRITICAL_STATUSES: BatchStatus[] = ["critical", "expired"];

function formatDate(dateStr: string): string {
  const [y, m, d] = dateStr.split("-");
  return `${d}.${m}.${y}`;
}

function getDaysColor(days: number): string {
  if (days <= 0) return STATUS_COLOR.expired.text;
  if (days <= 3) return STATUS_COLOR.critical.text;
  if (days <= 7) return STATUS_COLOR.warning.text;
  return "#6B7280";
}

// ── Detail drawer ────────────────────────────────────────────────────────────
function StockDetail({ item }: { item: ProductStockDto }) {
  const t = useTranslations("Dashboard.shelf.stockTable.drawer");
  const tOverdue = useTranslations("Dashboard.shelf.stockTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const fillPct =
    item.quantityInitial > 0
      ? Math.round((item.quantity / item.quantityInitial) * 100)
      : 0;

  return (
    <>
      <DrawerSection title={t("sectionProductBatch")}>
        <DrawerField label={t("productName")} value={item.productName} />
        <DrawerGrid>
          <DrawerField
            label={t("barcode")}
            value={
              <span style={{ fontFamily: "monospace", color: "#4B5563" }}>
                {item.productBarcode ?? "—"}
              </span>
            }
          />
          <DrawerField
            label={t("batch")}
            value={
              <span style={{ fontFamily: "monospace", color: "#9CA3AF" }}>
                {item.batchNumber ?? "—"}
              </span>
            }
          />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title={t("sectionLocation")}>
        <DrawerGrid>
          <DrawerField label={t("store")} value={item.storeName} />
          <DrawerField label={t("zone")} value={item.zoneName ?? "—"} />
          <DrawerField
            label={t("shelf")}
            value={item.shelfNumber != null ? `#${item.shelfNumber}` : "—"}
          />
          <DrawerField label={t("source")} value={item.sourceType ?? "—"} />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title={t("sectionStock")}>
        <DrawerGrid>
          <DrawerField
            label={t("quantity")}
            value={
              <span style={{ fontFamily: "monospace" }}>
                {item.quantity.toLocaleString(intlLocale)}
              </span>
            }
          />
          <DrawerField
            label={t("quantityInitial")}
            value={
              <span style={{ fontFamily: "monospace", color: "#4B5563" }}>
                {item.quantityInitial.toLocaleString(intlLocale)}
              </span>
            }
          />
          <DrawerField
            label={t("expiryDate")}
            value={formatDate(item.expiryDate)}
          />
          <DrawerField
            label={t("daysLeft")}
            value={
              <span
                style={{ fontFamily: "monospace", fontWeight: 700, color: getDaysColor(item.daysLeft) }}
              >
                {item.daysLeft <= 0 ? tOverdue("overdue", { days: Math.abs(item.daysLeft) }) : item.daysLeft}
              </span>
            }
          />
          <DrawerField
            label={t("status")}
            value={<StatusBadge status={item.status} />}
          />
          <DrawerField label={t("used")} value={`${fillPct}%`} />
        </DrawerGrid>

        {/* Fill bar */}
        <div style={{ marginTop: 4 }}>
          <div
            style={{
              height: 6,
              background: "#1F2937",
              borderRadius: 4,
              overflow: "hidden",
            }}
          >
            <div
              style={{
                height: "100%",
                width: `${Math.min(fillPct, 100)}%`,
                background:
                  fillPct > 80 ? "#4ADE80" : fillPct > 40 ? "#FBBF24" : "#F87171",
                borderRadius: 4,
                transition: "width 0.3s",
              }}
            />
          </div>
        </div>
      </DrawerSection>

      <DrawerSection title={t("sectionTimestamps")}>
        <DrawerGrid>
          <DrawerField
            label={t("added")}
            value={new Date(item.addedAt).toLocaleString(intlLocale)}
          />
          <DrawerField
            label={t("lastChecked")}
            value={new Date(item.lastCheckedAt).toLocaleString(intlLocale)}
          />
        </DrawerGrid>
      </DrawerSection>
    </>
  );
}

// ── Table ────────────────────────────────────────────────────────────────────
export function StockTable({
  items,
  isLoading,
  selectedIds,
  onSelectId,
  onSelectAll,
  onVerify,
  sortBy,
  sortDescending,
  onSort,
}: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.shelf.stockTable");
  const tStatus = useTranslations("Dashboard.shelf.status");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [selected, setSelected] = useState<ProductStockDto | null>(null);

  if (isLoading) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
        {t("loading")}
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
        {t("empty")}
      </div>
    );
  }

  const allSelected = items.length > 0 && items.every((i) => selectedIds.has(i.id));
  const someSelected = items.some((i) => selectedIds.has(i.id)) && !allSelected;

  // Checkbox occupies index 0 structurally, so the product-name column (the real "name/label"
  // column) sits at index 1 — an explicit `align: "left"` override is the "genuinely good
  // reason" case the shared Table's docs call out, since the default would otherwise center it.
  const columns: TableColumn<ProductStockDto>[] = [
    {
      key: "select",
      width: 36,
      align: "center",
      header: (
        <input
          type="checkbox"
          checked={allSelected}
          ref={(el) => {
            if (el) el.indeterminate = someSelected;
          }}
          onChange={(e) => onSelectAll(e.target.checked)}
          style={{ cursor: "pointer", accentColor: "#3B82F6" }}
        />
      ),
      render: (item) => (
        <input
          type="checkbox"
          checked={selectedIds.has(item.id)}
          onChange={(e) => onSelectId(item.id, e.target.checked)}
          style={{ cursor: "pointer", accentColor: "#3B82F6" }}
        />
      ),
    },
    {
      key: "name",
      align: "left",
      header: t("headers.name"),
      sortKey: "productname",
      cellStyle: { color: "#E8EDF5", fontWeight: 500, maxWidth: 240 },
      render: (item) => (
        <>
          <div style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }} title={item.productName}>
            {item.productName}
          </div>
          <div style={{ color: "#4B5563", fontSize: 11, marginTop: 1 }}>{item.storeName}</div>
        </>
      ),
    },
    {
      key: "barcode",
      header: t("headers.barcode"),
      cellStyle: { fontFamily: "monospace", fontSize: 11, whiteSpace: "nowrap" },
      render: (item) => item.productBarcode ?? "—",
    },
    {
      key: "zone",
      header: t("headers.zone"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (item) => item.zoneName ?? "—",
    },
    {
      key: "batch",
      header: t("headers.batch"),
      cellStyle: { fontFamily: "monospace", fontSize: 11, whiteSpace: "nowrap" },
      render: (item) => item.batchNumber ?? "—",
    },
    {
      key: "qty",
      header: t("headers.qty"),
      sortKey: "quantity",
      cellStyle: { color: "#E8EDF5", whiteSpace: "nowrap" },
      render: (item) => item.quantity.toLocaleString(intlLocale),
    },
    {
      key: "expiry",
      header: t("headers.expiry"),
      sortKey: "expirydate",
      cellStyle: { whiteSpace: "nowrap" },
      render: (item) => formatDate(item.expiryDate),
    },
    {
      key: "days",
      header: t("headers.days"),
      cellStyle: { fontFamily: "monospace", whiteSpace: "nowrap" },
      render: (item) => (
        <span style={{ color: getDaysColor(item.daysLeft), fontWeight: 600 }}>
          {item.daysLeft <= 0 ? (
            <span style={{ color: STATUS_COLOR.expired.text }}>−{Math.abs(item.daysLeft)}</span>
          ) : (
            item.daysLeft
          )}
        </span>
      ),
    },
    {
      key: "status",
      header: t("headers.status"),
      sortKey: "status",
      cellStyle: { whiteSpace: "nowrap" },
      render: (item) => <StatusBadge status={item.status} />,
    },
    {
      key: "actions",
      header: t("headers.actions"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (item) => (
        <ActionMenu
          items={[
            {
              label: t("actionMenu.viewDetails"),
              icon: <Eye size={13} />,
              onClick: () => setSelected(item),
            },
            { separator: true },
            ...(item.status === "needs_verification" && onVerify
              ? [
                  {
                    label: t("actionMenu.markVerified"),
                    icon: <ShieldCheck size={13} />,
                    variant: "success" as const,
                    onClick: () => onVerify(item.id),
                  },
                ]
              : []),
            {
              label: t("actionMenu.statusLabel", { status: tStatus(item.status) }),
              variant: "warning" as const,
              disabled: true,
            },
            { separator: true },
            {
              label: t("actionMenu.analytics"),
              icon: <BarChart2 size={13} />,
              onClick: () => router.push(`/inventory/${item.productId}?tab=analytics`),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <>
      <Table
        columns={columns}
        rows={items}
        rowKey={(item) => item.id}
        sortBy={sortBy}
        sortDescending={sortDescending}
        onSort={onSort}
        minWidth={900}
        rowStyle={(item) => {
          if (selectedIds.has(item.id)) return { background: "#1D3461" };
          if (CRITICAL_STATUSES.includes(item.status)) return { background: "rgba(239,68,68,0.04)" };
          return {};
        }}
      />

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={selected?.productName ?? ""}
        subtitle={selected ? `${selected.storeName}${selected.zoneName ? ` · ${selected.zoneName}` : ""}` : ""}
        width={540}
      >
        {selected && <StockDetail item={selected} />}
      </DetailDrawer>
    </>
  );
}
