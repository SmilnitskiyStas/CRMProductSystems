"use client";

import { useRef, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import type { Customer } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { StatusPill } from "@/components/ui/StatusPill";
import { filterInputStyle } from "@/components/ui/fieldStyles";

function formatDate(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleDateString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function TagBadge({ tag }: { tag: string }) {
  return (
    <StatusPill bg="#0a1628" text="#60A5FA" style={{ border: "1px solid #1D4ED8", fontWeight: 500 }}>
      {tag}
    </StatusPill>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface Props {
  customers: Customer[];
  totalCount: number;
  page: number;
  totalPages: number;
  search: string;
  isLoading: boolean;
  onSearchChange: (v: string) => void;
  onPageChange: (p: number) => void;
  onRowClick: (c: Customer) => void;
  onEdit: (c: Customer) => void;
  onDelete: (c: Customer) => void;
}

export function CustomerTable({
  customers,
  totalCount,
  page,
  totalPages,
  search,
  isLoading,
  onSearchChange,
  onPageChange,
  onRowClick,
  onEdit,
  onDelete,
}: Props) {
  const t = useTranslations("Dashboard.customers.table");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const uah = new Intl.NumberFormat(intlLocale, { style: "currency", currency: "UAH" });

  const columns: TableColumn<Customer>[] = [
    {
      key: "name",
      header: t("headerName"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500, maxWidth: 220, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
      render: (c) => c.name,
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (c) => (
        <span style={{ color: c.phone ? "#9CA3AF" : "#374151", fontSize: 12 }}>
          {c.phone ?? "—"}
        </span>
      ),
    },
    {
      key: "email",
      header: t("headerEmail"),
      cellStyle: { maxWidth: 200, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" },
      render: (c) => (
        <span style={{ color: c.email ? "#9CA3AF" : "#374151", fontSize: 12 }}>
          {c.email ?? "—"}
        </span>
      ),
    },
    {
      key: "tags",
      header: t("headerTags"),
      render: (c) => (
        <div style={{ display: "inline-flex", flexWrap: "wrap", gap: 4 }}>
          {c.tags.length > 0
            ? c.tags.slice(0, 3).map((tag) => <TagBadge key={tag} tag={tag} />)
            : <span style={{ color: "#374151", fontSize: 12 }}>—</span>
          }
          {c.tags.length > 3 && (
            <span style={{ color: "#4B5563", fontSize: 11 }}>+{c.tags.length - 3}</span>
          )}
        </div>
      ),
    },
    {
      key: "orders",
      header: t("headerOrders"),
      render: (c) => <span style={{ color: "#9CA3AF", fontSize: 13 }}>{c.totalOrders}</span>,
    },
    {
      key: "spent",
      header: t("headerSpent"),
      render: (c) => (
        <span style={{ color: "#4ADE80", fontSize: 13, fontWeight: 500 }}>
          {uah.format(c.totalSpent)}
        </span>
      ),
    },
    {
      key: "createdAt",
      header: t("headerCreatedAt"),
      render: (c) => (
        <span style={{ color: "#4B5563", fontSize: 12 }}>{formatDate(c.createdAt, intlLocale)}</span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (c) => (
        <div onClick={(e) => e.stopPropagation()}>
          <ActionMenu
            items={[
              { label: t("edit"), onClick: () => onEdit(c) },
              { label: t("delete"), onClick: () => onDelete(c), variant: "danger" },
            ]}
          />
        </div>
      ),
    },
  ];

  // Debounced search
  const [localSearch, setLocalSearch] = useState(search);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  function handleSearchInput(v: string) {
    setLocalSearch(v);
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => {
      onSearchChange(v);
      onPageChange(1);
    }, 300);
  }

  return (
    <div>
      {/* Search bar */}
      <div style={{ marginBottom: 16 }}>
        <input
          value={localSearch}
          onChange={(e) => handleSearchInput(e.target.value)}
          placeholder={t("searchPlaceholder")}
          style={{ ...filterInputStyle, width: "100%", maxWidth: 400 }}
        />
      </div>

      {/* Table */}
      <Table
        columns={columns}
        rows={customers}
        rowKey={(c) => c.id}
        onRowClick={onRowClick}
        isLoading={isLoading}
        minWidth={900}
        emptyMessage={isLoading ? t("loading") : (search ? t("emptySearch") : t("emptyNone"))}
        page={page}
        totalPages={totalPages}
        totalCount={totalCount}
        onPageChange={onPageChange}
      />
    </div>
  );
}
