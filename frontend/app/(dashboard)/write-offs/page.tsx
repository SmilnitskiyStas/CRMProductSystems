"use client";

import { Suspense, useEffect, useState, useMemo, useRef } from "react";
import { useRouter } from "next/navigation";
import { useSearchParams } from "next/navigation";
import {
  Eye, CheckCircle, XCircle, FileDown, BarChart2, Plus,
} from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import {
  useWriteOffs,
  useApproveWriteOff,
  useRejectWriteOff,
} from "@/features/write-offs/hooks/useWriteOffs";
import type { WriteOffDto, WriteOffStatus, WriteOffSortBy } from "@/features/write-offs/types";
import { WRITE_OFF_STATUS_COLOR } from "@/features/write-offs/types";
import { CreateWriteOffForm } from "@/features/write-offs/components/CreateWriteOffForm";
import { useMe } from "@/features/auth/hooks/useAuth";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { useCategories } from "@/features/inventory/hooks/useCategories";
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

const STATUS_TAB_VALUES = ["", "pending_approval", "approved", "draft", "rejected"] as const;

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

function StatusBadge({ status }: { status: WriteOffStatus }) {
  const t = useTranslations("Dashboard.writeOffs.status");
  const c = WRITE_OFF_STATUS_COLOR[status] ?? WRITE_OFF_STATUS_COLOR.draft;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 8px",
        borderRadius: 20,
        background: c.bg,
        color: c.text,
        fontSize: 11,
        fontWeight: 600,
      }}
    >
      {t.has(status) ? t(status) : status}
    </span>
  );
}

// ── Detail drawer content ────────────────────────────────────────────────────
function WriteOffDetail({ w, onViewAnalytics }: { w: WriteOffDto; onViewAnalytics: (productId: string) => void }) {
  const t = useTranslations("Dashboard.writeOffs.drawer");
  const tReason = useTranslations("Dashboard.writeOffs.reason");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <>
      <DrawerSection title={t("section")}>
        <DrawerGrid>
          <DrawerField label={t("store")} value={w.storeName} />
          <DrawerField
            label={t("status")}
            value={<StatusBadge status={w.status as WriteOffStatus} />}
          />
          <DrawerField
            label={t("reason")}
            value={w.reason && tReason.has(w.reason) ? tReason(w.reason) : (w.reason ?? "—")}
          />
          <DrawerField
            label={t("lossAmount")}
            value={
              w.totalLossAmount != null
                ? `${w.totalLossAmount.toLocaleString(intlLocale)} ₴`
                : "—"
            }
            color="#F87171"
          />
          <DrawerField
            label={t("lossAmountPurchase")}
            value={
              w.totalLossAmountPurchase != null
                ? `${w.totalLossAmountPurchase.toLocaleString(intlLocale)} ₴`
                : "—"
            }
            color="#FBBF24"
          />
          {w.totalReimbursementAmount != null && w.totalReimbursementAmount !== 0 && (
            <>
              <DrawerField
                label={t("reimbursementAmount")}
                value={`${w.totalReimbursementAmount.toLocaleString(intlLocale)} ₴`}
                color="#4ADE80"
              />
              <DrawerField
                label={t("netLossAmount")}
                value={
                  w.netLossAmount != null ? `${w.netLossAmount.toLocaleString(intlLocale)} ₴` : "—"
                }
                color="#F87171"
              />
            </>
          )}
          <DrawerField
            label={t("createdAt")}
            value={new Date(w.createdAt).toLocaleDateString(intlLocale)}
          />
          {w.approvedAt && (
            <DrawerField
              label={t("approvedAt")}
              value={new Date(w.approvedAt).toLocaleDateString(intlLocale)}
            />
          )}
        </DrawerGrid>
        <DrawerField
          label={t("documentId")}
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {w.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={t("itemsSection", { count: w.items.length })}>
        {w.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noItems")}</div>
        ) : (
          <div
            style={{
              border: "1px solid #1F2937",
              borderRadius: 8,
              overflow: "hidden",
            }}
          >
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {[
                    t("itemHeaders.product"),
                    t("itemHeaders.batch"),
                    t("itemHeaders.qty"),
                    t("itemHeaders.loss"),
                    t("itemHeaders.lossPurchase"),
                    t("itemHeaders.reimbursement"),
                    t("itemHeaders.actions"),
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
                {w.items.map((item) => (
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
                      {item.isReturnedToSupplier && (
                        <span
                          style={{
                            marginLeft: 6,
                            color: "#4ADE80",
                            fontSize: 10,
                            fontWeight: 600,
                            textTransform: "uppercase",
                            letterSpacing: "0.03em",
                          }}
                        >
                          {t("returnedToSupplierBadge")}
                        </span>
                      )}
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
                      {item.quantity}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#F87171",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.lossAmount != null
                        ? `${item.lossAmount.toLocaleString(intlLocale)} ₴`
                        : "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#FBBF24",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.lossAmountPurchase != null
                        ? `${item.lossAmountPurchase.toLocaleString(intlLocale)} ₴`
                        : "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#4ADE80",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.isReturnedToSupplier && item.reimbursementAmount != null
                        ? `${item.reimbursementAmount.toLocaleString(intlLocale)} ₴`
                        : "—"}
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #1F2937", textAlign: "center" }}>
                      <ActionMenu
                        items={[
                          {
                            label: t("analyticsAction"),
                            icon: <BarChart2 size={13} />,
                            onClick: () => onViewAnalytics(item.productId),
                          },
                        ]}
                      />
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

// ── Page inner (uses useSearchParams) ────────────────────────────────────────
function WriteOffsPageContent() {
  const t = useTranslations("Dashboard.writeOffs");
  const tPage = useTranslations("Dashboard.writeOffs.page");
  const tReason = useTranslations("Dashboard.writeOffs.reason");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const searchParams = useSearchParams();
  const { data: me } = useMe();
  const canCreate = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : false;

  const [statusFilter, setStatusFilter] = useState("");
  const [reasonFilter] = useState(searchParams.get("reason") ?? "");
  const [categoryId, setCategoryId] = useState("");
  const [minLossAmount, setMinLossAmount] = useState<number | undefined>(undefined);
  const [maxLossAmount, setMaxLossAmount] = useState<number | undefined>(undefined);
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

  const [sortBy, setSortBy] = useState<WriteOffSortBy>("createdat");
  const [sortDescending, setSortDescending] = useState(true);
  function handleSort(key: WriteOffSortBy) {
    if (key === sortBy) setSortDescending((d) => !d);
    else {
      setSortBy(key);
      setSortDescending(true);
    }
  }

  const { data, isLoading } = useWriteOffs({
    store_id: primaryStoreId,
    status: statusFilter || undefined,
    category_id: categoryId || undefined,
    min_loss_amount: minLossAmount,
    max_loss_amount: maxLossAmount,
    page,
    pageSize: PAGE_SIZE,
    search: search || undefined,
    sortBy,
    sortDescending,
  });
  const writeOffs = useMemo(() => data?.items ?? [], [data]);
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? Math.ceil(totalCount / PAGE_SIZE);

  // Reset to page 1 whenever a filter changes underneath the current page.
  useEffect(() => {
    setPage(1);
  }, [
    statusFilter,
    reasonFilter,
    categoryId,
    minLossAmount,
    maxLossAmount,
    primaryStoreId,
    search,
    sortBy,
    sortDescending,
  ]);

  const approve = useApproveWriteOff();
  const reject = useRejectWriteOff();

  const [selected, setSelected] = useState<WriteOffDto | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);

  // Client-side filtering for reason (store filtering now happens server-side via
  // the global header selector — see usePrimaryStoreId() above)
  const filteredWriteOffs = useMemo(() => {
    if (!reasonFilter) return writeOffs;
    return writeOffs.filter((w) => w.reason === reasonFilter);
  }, [writeOffs, reasonFilter]);

  const chipStyle: React.CSSProperties = {
    background: "#1D3461",
    border: "1px solid #3B82F6",
    color: "#93C5FD",
    borderRadius: 20,
    padding: "3px 10px",
    fontSize: 12,
    display: "flex",
    alignItems: "center",
    gap: 6,
  };

  const statusTabLabel = (value: string) =>
    value === "" ? tPage("statusTabs.all") : t(`status.${value}`);

  const writeOffColumns: TableColumn<WriteOffDto>[] = [
    {
      key: "store",
      header: tPage("headers.store"),
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (w) => w.storeName,
    },
    {
      key: "reason",
      header: tPage("headers.reason"),
      sortKey: "reason",
      render: (w) => (w.reason ? (tReason.has(w.reason) ? tReason(w.reason) : w.reason) : "—"),
    },
    {
      key: "items",
      header: tPage("headers.items"),
      cellStyle: { fontFamily: "monospace" },
      render: (w) => w.items.length,
    },
    {
      key: "lossAmount",
      header: tPage("headers.lossAmount"),
      sortKey: "netloss",
      cellStyle: { fontFamily: "monospace", color: "#F87171" },
      render: (w) => (w.totalLossAmount != null ? `${w.totalLossAmount.toLocaleString(intlLocale)} ₴` : "—"),
    },
    {
      key: "date",
      header: tPage("headers.date"),
      sortKey: "createdat",
      render: (w) => new Date(w.createdAt).toLocaleDateString(intlLocale),
    },
    {
      key: "status",
      header: tPage("headers.status"),
      sortKey: "status",
      render: (w) => <StatusBadge status={w.status as WriteOffStatus} />,
    },
    {
      key: "actions",
      header: tPage("headers.actions"),
      render: (w) => (
        <ActionMenu
          items={[
            {
              label: tPage("actionMenu.view"),
              icon: <Eye size={13} />,
              onClick: () => setSelected(w),
            },
            { separator: true },
            ...(w.status === "pending_approval"
              ? [
                  {
                    label: tPage("actionMenu.approve"),
                    icon: <CheckCircle size={13} />,
                    variant: "success" as const,
                    disabled: approve.isPending,
                    onClick: () => approve.mutate(w.id),
                  },
                  {
                    label: tPage("actionMenu.reject"),
                    icon: <XCircle size={13} />,
                    variant: "danger" as const,
                    disabled: reject.isPending,
                    onClick: () => reject.mutate(w.id),
                  },
                ]
              : []),
            ...(w.status === "approved" && w.pdfUrl
              ? [
                  {
                    label: tPage("actionMenu.downloadPdf"),
                    icon: <FileDown size={13} />,
                    href: w.pdfUrl,
                  },
                ]
              : []),
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
        {canCreate && (
          <Btn icon={<Plus size={15} />} onClick={() => setShowCreateModal(true)}>
            {tPage("newButton")}
          </Btn>
        )}
      </div>

      {/* Status tabs + search */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 12 }}>
        <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", flex: 1, minWidth: 200 }}>
          {STATUS_TAB_VALUES.map((value) => {
            const active = value === statusFilter;
            const pendingCount =
              value === "pending_approval"
                ? writeOffs.filter((w) => w.status === "pending_approval").length
                : 0;
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
                {pendingCount > 0 && (
                  <span
                    style={{
                      marginLeft: 6,
                      background: "#FBBF24",
                      color: "#000",
                      borderRadius: 10,
                      padding: "1px 6px",
                      fontSize: 10,
                      fontWeight: 700,
                    }}
                  >
                    {pendingCount}
                  </span>
                )}
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

      {/* Category + loss-amount filters */}
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
          min={minLossAmount}
          max={maxLossAmount}
          onChange={(next) => {
            setMinLossAmount(next.min);
            setMaxLossAmount(next.max);
          }}
          placeholder={tPage("lossAmountRangeLabel")}
        />
      </div>

      {/* Active filter chips */}
      {reasonFilter && (
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          <span style={chipStyle}>
            {tPage("filterChips.reason", {
              value: tReason.has(reasonFilter) ? tReason(reasonFilter) : reasonFilter,
            })}
          </span>
        </div>
      )}

      {/* Table */}
      <Table
        columns={writeOffColumns}
        rows={filteredWriteOffs}
        rowKey={(w) => w.id}
        sortBy={sortBy}
        sortDescending={sortDescending}
        onSort={handleSort}
        isLoading={isLoading}
        emptyMessage={isLoading ? tCommon("loading") : tPage("empty")}
        rowStyle={(w) => (w.status === "pending_approval" ? { background: "rgba(251,191,36,0.03)" } : {})}
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
        subtitle={selected ? `${selected.storeName} · ${new Date(selected.createdAt).toLocaleDateString(intlLocale)}` : ""}
      >
        {selected && (
          <WriteOffDetail
            w={selected}
            onViewAnalytics={(productId) => router.push(`/inventory/${productId}?tab=analytics`)}
          />
        )}
      </DetailDrawer>

      {showCreateModal && (
        <Modal title={t("createForm.modalTitle")} onClose={() => setShowCreateModal(false)} width={700}>
          <CreateWriteOffForm
            onSuccess={() => setShowCreateModal(false)}
            onCancel={() => setShowCreateModal(false)}
          />
        </Modal>
      )}
    </div>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────
export default function WriteOffsPage() {
  return (
    <Suspense>
      <WriteOffsPageContent />
    </Suspense>
  );
}
