"use client";

import { useEffect, useRef, useState } from "react";
import { Eye, ExternalLink, Plus } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useReceipts } from "@/features/receipts/hooks/useReceipts";
import { ReceiptStatusBadge } from "@/features/receipts/components/ReceiptStatusBadge";
import { CreateReceiptForm } from "@/features/receipts/components/CreateReceiptForm";
import type { ReceiptDto, ReceiptStatus, ReceiptSortBy } from "@/features/receipts/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { useCategories } from "@/features/inventory/hooks/useCategories";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_RECEIVE_STOCK, hasRole } from "@/lib/roles";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { RangeFilter } from "@/components/ui/RangeFilter";
import { Table, type TableColumn } from "@/components/ui/Table";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

const STATUS_TAB_VALUES = ["", "draft", "in_transit", "received", "cancelled"] as const;

// Matches the search input's existing inline style below — shared here for the new
// category filter select added alongside it.
const filterInputStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 12px",
  outline: "none",
};

function formatDate(s: string | null, intlLocale: string) {
  if (!s) return "—";
  return new Date(s).toLocaleDateString(intlLocale);
}

// ── Detail drawer content ────────────────────────────────────────────────────
function ReceiptDetail({ r }: { r: ReceiptDto }) {
  const t = useTranslations("Dashboard.receipts.drawer");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <>
      <DrawerSection title={t("section")}>
        <DrawerGrid>
          <DrawerField label={t("supplier")} value={r.supplierName ?? "—"} />
          <DrawerField label={t("destinationStore")} value={r.destinationStoreName} />
          <DrawerField
            label={t("status")}
            value={<ReceiptStatusBadge status={r.status as ReceiptStatus} />}
          />
          <DrawerField label={t("expected")} value={formatDate(r.expectedAt, intlLocale)} />
          <DrawerField label={t("received")} value={formatDate(r.receivedAt, intlLocale)} />
          <DrawerField label={t("items")} value={r.items.length} />
          <DrawerField label={t("viaCentral")} value={r.viaCentralStore ? t("yes") : t("no")} />
          <DrawerField label={t("createdAt")} value={formatDate(r.createdAt, intlLocale)} />
        </DrawerGrid>
        {r.notes && <DrawerField label={t("notes")} value={r.notes} />}
        <DrawerField
          label={t("documentId")}
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {r.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={t("itemsSection", { count: r.items.length })}>
        {r.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noItems")}</div>
        ) : (
          <div style={{ border: "1px solid #1F2937", borderRadius: 8, overflow: "hidden" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {[
                    t("itemHeaders.product"),
                    t("itemHeaders.batch"),
                    t("itemHeaders.ordered"),
                    t("itemHeaders.received"),
                    t("itemHeaders.expiry"),
                  ].map((h) => (
                    <th
                      key={h}
                      style={{
                        padding: "7px 10px",
                        color: "#4B5563",
                        fontWeight: 600,
                        textTransform: "uppercase",
                        fontSize: 10,
                        letterSpacing: "0.05em",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        background: "#0A0F1A",
                        textAlign: "center",
                      }}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {r.items.map((item) => (
                  <tr key={item.id}>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#E8EDF5",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        fontWeight: 500,
                      }}
                    >
                      {item.productName}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#4B5563",
                        fontFamily: "monospace",
                        fontSize: 11,
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.batchNumber ?? "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.quantityOrdered}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                        color:
                          item.quantityReceived != null &&
                          item.quantityReceived < item.quantityOrdered
                            ? "#FBBF24"
                            : "#4ADE80",
                      }}
                    >
                      {item.quantityReceived ?? "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        borderBottom: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.expiryDate ? formatDate(item.expiryDate, intlLocale) : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </DrawerSection>
    </>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────
export default function ReceiptsPage() {
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const t = useTranslations("Dashboard.receipts");
  const tPage = useTranslations("Dashboard.receipts.page");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [statusFilter, setStatusFilter] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [minItems, setMinItems] = useState<number | undefined>(undefined);
  const [maxItems, setMaxItems] = useState<number | undefined>(undefined);
  const { data: categories = [] } = useCategories();
  const primaryStoreId = usePrimaryStoreId();
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 50;

  // Debounced search (300ms) — mirrors features/customers/components/CustomerTable.tsx's
  // handleSearchInput pattern, inlined here since this page has no dedicated filter component.
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  function handleSearchInput(v: string) {
    setSearchInput(v);
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    searchTimerRef.current = setTimeout(() => setSearch(v), 300);
  }

  const [sortBy, setSortBy] = useState<ReceiptSortBy>("createdat");
  const [sortDescending, setSortDescending] = useState(true);
  function handleSort(key: ReceiptSortBy) {
    if (key === sortBy) setSortDescending((d) => !d);
    else {
      setSortBy(key);
      setSortDescending(true);
    }
  }

  const { data, isLoading } = useReceipts(
    {
      store_id: primaryStoreId,
      status: statusFilter || undefined,
      category_id: categoryId || undefined,
      min_items: minItems,
      max_items: maxItems,
      page,
      pageSize: PAGE_SIZE,
      search: search || undefined,
      sortBy,
      sortDescending,
    },
    access === true,
  );
  const receipts = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? Math.ceil(totalCount / PAGE_SIZE);

  // Reset to page 1 whenever a filter changes underneath the current page.
  useEffect(() => {
    setPage(1);
  }, [statusFilter, categoryId, minItems, maxItems, primaryStoreId, search, sortBy, sortDescending]);

  const [selected, setSelected] = useState<ReceiptDto | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  const statusTabLabel = (value: string) =>
    value === "" ? tPage("statusTabs.all") : t(`status.${value}`);

  const receiptColumns: TableColumn<ReceiptDto>[] = [
    {
      key: "id",
      header: tPage("headers.id"),
      cellStyle: { fontFamily: "monospace", fontSize: 11, color: "#4B5563" },
      render: (r) => `${r.id.slice(0, 8)}…`,
    },
    {
      key: "store",
      header: tPage("headers.store"),
      sortKey: "destination",
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (r) => r.destinationStoreName,
    },
    {
      key: "supplier",
      header: tPage("headers.supplier"),
      sortKey: "supplier",
      render: (r) => r.supplierName ?? "—",
    },
    {
      key: "expected",
      header: tPage("headers.expected"),
      sortKey: "expectedat",
      render: (r) => formatDate(r.expectedAt, intlLocale),
    },
    {
      key: "status",
      header: tPage("headers.status"),
      sortKey: "status",
      render: (r) => <ReceiptStatusBadge status={r.status as ReceiptStatus} />,
    },
    {
      key: "items",
      header: tPage("headers.items"),
      cellStyle: { fontFamily: "monospace" },
      render: (r) => r.items.length,
    },
    {
      key: "actions",
      header: tPage("headers.actions"),
      render: (r) => (
        <ActionMenu
          items={[
            {
              label: tPage("actionMenu.view"),
              icon: <Eye size={13} />,
              onClick: () => setSelected(r),
            },
            {
              label: tPage("actionMenu.openPage"),
              icon: <ExternalLink size={13} />,
              href: `/receipts/${r.id}`,
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {tPage("subtitle")}
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={() => setShowCreateModal(true)}>
          {tPage("newButton")}
        </Btn>
      </div>

      {/* Status tabs + search */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
        <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", paddingBottom: 0, flex: 1, minWidth: 200 }}>
          {STATUS_TAB_VALUES.map((value) => {
            const active = value === statusFilter;
            return (
              <button
                key={value}
                onClick={() => setStatusFilter(value)}
                style={{
                  background: "transparent",
                  border: "none",
                  borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
                  color: active ? "#60A5FA" : "#6B7280",
                  fontSize: 13,
                  fontWeight: active ? 600 : 400,
                  padding: "8px 14px",
                  cursor: "pointer",
                  marginBottom: -1,
                  transition: "color 0.1s",
                }}
              >
                {statusTabLabel(value)}
              </button>
            );
          })}
        </div>
        <input
          type="text"
          value={searchInput}
          onChange={(e) => handleSearchInput(e.target.value)}
          placeholder={tPage("searchPlaceholder")}
          style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 8,
            color: "#E8EDF5",
            fontSize: 13,
            padding: "7px 12px",
            outline: "none",
            width: 260,
          }}
        />
      </div>

      {/* Category + item-count filters */}
      <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
        <select
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          style={{ ...filterInputStyle, cursor: "pointer" }}
        >
          <option value="">{tPage("allCategories")}</option>
          {categories.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>

        <RangeFilter
          min={minItems}
          max={maxItems}
          onChange={(next) => {
            setMinItems(next.min);
            setMaxItems(next.max);
          }}
          placeholder={tPage("itemsRangeLabel")}
        />
      </div>

      {/* Table */}
      <Table
        columns={receiptColumns}
        rows={receipts}
        rowKey={(r) => r.id}
        sortBy={sortBy}
        sortDescending={sortDescending}
        onSort={handleSort}
        isLoading={isLoading}
        emptyMessage={isLoading ? tCommon("loading") : tPage("empty")}
        page={page}
        totalPages={totalPages}
        totalCount={totalCount}
        onPageChange={setPage}
      />

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={t("title")}
        subtitle={
          selected
            ? `${selected.supplierName ?? t("drawer.noSupplier")} → ${selected.destinationStoreName}`
            : ""
        }
      >
        {selected && <ReceiptDetail r={selected} />}
      </DetailDrawer>

      {showCreateModal && (
        <Modal title={t("createForm.modalTitle")} onClose={() => setShowCreateModal(false)} width={720}>
          <CreateReceiptForm
            onSuccess={() => setShowCreateModal(false)}
            onCancel={() => setShowCreateModal(false)}
          />
        </Modal>
      )}
    </div>
  );
}
