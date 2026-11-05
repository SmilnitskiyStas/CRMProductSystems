"use client";

import { Suspense, useEffect, useState, useMemo, useRef } from "react";
import { useSearchParams } from "next/navigation";
import { Plus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import { useStock, useVerifyStock } from "@/features/shelf/hooks/useStock";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { useStores } from "@/features/stores/hooks/useStores";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { StockTable } from "@/features/shelf/components/StockTable";
import { StockFilters } from "@/features/shelf/components/StockFilters";
import { AddBatchForm } from "@/features/shelf/components/AddBatchForm";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { Pagination } from "@/components/ui/Pagination";
import { useMe } from "@/features/auth/hooks/useAuth";
import { CAN_RECEIVE_STOCK, AT_LEAST_STORE_MANAGER, hasRole } from "@/lib/roles";
import { canWriteOffBatch, getWriteOffBatches, shareWriteOffStore } from "@/features/shelf/lib/stockWriteOff";
import { StockWriteOffDialog } from "@/features/shelf/components/StockWriteOffDialog";
import type { ProductStockDto } from "@/features/shelf/types";
import type { StockSortBy } from "@/features/shelf/types";

interface Filters {
  status: string;
  search: string;
  store_id: string;
  zone_id: string;
  category_id: string;
  min_quantity?: number;
  max_quantity?: number;
}

function StockPageContent() {
  const t = useTranslations("Dashboard.stock");
  const searchParams = useSearchParams();
  const primaryStoreId = usePrimaryStoreId();
  const { data: me } = useMe();
  const canCreateWriteOff = hasRole(me?.role, CAN_RECEIVE_STOCK);
  const canApproveWriteOff = hasRole(me?.role, AT_LEAST_STORE_MANAGER);
  const [filters, setFilters] = useState<Filters>({
    status: searchParams.get("status") ?? "",
    search: "",
    store_id: searchParams.get("store_id") ?? "",
    zone_id: searchParams.get("zone_id") ?? "",
    category_id: "",
    min_quantity: undefined,
    max_quantity: undefined,
  });
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [showAddModal, setShowAddModal] = useState(false);
  const [writeOffBatches, setWriteOffBatches] = useState<ProductStockDto[] | null>(null);
  const [writeOffCompleted, setWriteOffCompleted] = useState(false);

  // If the user hasn't explicitly picked a store filter, fall back to the store
  // selected in the header StoreSelector so stock changes when the user switches stores.
  const effectiveStoreId = filters.store_id || primaryStoreId || undefined;

  // Debounced search (300ms) — `filters.search` stays the immediately-typed value bound to
  // StockFilters' input for instant UI feedback; `debouncedSearch` is what actually reaches the
  // query. Search is now server-side (GET /api/stock's `search` param) — see the removed
  // post-fetch `.filter()` below, which only ever matched within the current page and silently
  // missed hits on other pages (known correctness bug, fixed by this change).
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    searchTimerRef.current = setTimeout(() => setDebouncedSearch(filters.search), 300);
    return () => {
      if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    };
  }, [filters.search]);

  // expirydate ascending (soonest-expiring first) matches StockRepository's existing default
  // OrderBy(s => s.ExpiryDate) — FEFO-first — so the default sort direction here is ascending,
  // not the descending-by-default convention used elsewhere for a "createdat" default.
  const [sortBy, setSortBy] = useState<StockSortBy>("expirydate");
  const [sortDescending, setSortDescending] = useState(false);
  function handleSort(key: StockSortBy) {
    if (key === sortBy) setSortDescending((d) => !d);
    else {
      setSortBy(key);
      setSortDescending(true);
    }
  }

  const [page, setPage] = useState(1);
  const PAGE_SIZE = 50;
  const { data, isLoading, isPlaceholderData } = useStock({
    status: filters.status || undefined,
    store_id: effectiveStoreId,
    zone_id: filters.zone_id || undefined,
    category_id: filters.category_id || undefined,
    min_quantity: filters.min_quantity,
    max_quantity: filters.max_quantity,
    page,
    pageSize: PAGE_SIZE,
    search: debouncedSearch || undefined,
    sortBy,
    sortDescending,
  });
  const batches = useMemo(() => data?.items ?? [], [data]);
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? Math.ceil(totalCount / PAGE_SIZE);

  // Reset to page 1 whenever a filter changes underneath the current page.
  useEffect(() => {
    setPage(1);
    setSelectedIds(new Set());
  }, [
    filters.status,
    effectiveStoreId,
    filters.zone_id,
    filters.category_id,
    filters.min_quantity,
    filters.max_quantity,
    debouncedSearch,
    sortBy,
    sortDescending,
  ]);

  const { data: stores = [] } = useStores();
  const { data: products = [] } = useCatalogProducts();
  const verify = useVerifyStock();

  useEffect(() => { setSelectedIds(new Set()); }, [page]);

  function handleSelectId(id: string, checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function handleSelectAll(checked: boolean) {
    if (checked) setSelectedIds(new Set(batches.filter(canWriteOffBatch).map((b) => b.id)));
    else setSelectedIds(new Set());
  }

  const storeOptions = stores.map((s) => ({
    id: s.id,
    name: s.name,
    zones: s.zones.map((z) => ({ id: z.id, name: z.name })),
  }));

  const storeNameById = new Map(storeOptions.map((s) => [s.id, s.name]));
  const zoneNameById = new Map(storeOptions.flatMap((s) => s.zones.map((z) => [z.id, z.name] as const)));

  const productOptions = products.map((p) => ({
    id: p.id,
    name: p.name,
    barcode: p.barcode ?? null,
  }));

  const countLabel = String(batches.length);
  const selectedBatches = isPlaceholderData ? [] : getWriteOffBatches(batches, selectedIds);
  const canWriteOffSelection = shareWriteOffStore(selectedBatches);
  function openWriteOff(batchesToWriteOff: ProductStockDto[]) {
    if (!canCreateWriteOff || !shareWriteOffStore(batchesToWriteOff)) return;
    setWriteOffCompleted(false);
    setWriteOffBatches(batchesToWriteOff);
  }

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

  const chipBtnStyle: React.CSSProperties = {
    background: "none",
    border: "none",
    color: "#93C5FD",
    cursor: "pointer",
    fontSize: 14,
    padding: 0,
    lineHeight: 1,
  };

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {t("subtitle")}
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={() => setShowAddModal(true)}>
          {t("addButton")}
        </Btn>
      </div>

      {/* Filters */}
      <StockFilters
        filters={{
          status: filters.status,
          search: filters.search,
          category_id: filters.category_id,
          min_quantity: filters.min_quantity,
          max_quantity: filters.max_quantity,
        }}
        onChange={(partial) => setFilters((prev) => ({ ...prev, ...partial, store_id: prev.store_id, zone_id: prev.zone_id }))}
      />

      {/* Active store/zone filter chips */}
      {(filters.store_id || filters.zone_id) && (
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          {filters.store_id && (
            <span style={chipStyle}>
              {t("storeChip", { value: storeNameById.get(filters.store_id) ?? filters.store_id })}
              <button
                onClick={() => setFilters((p) => ({ ...p, store_id: "" }))}
                style={chipBtnStyle}
              >
                ×
              </button>
            </span>
          )}
          {filters.zone_id && (
            <span style={chipStyle}>
              {t("zoneChip", { value: zoneNameById.get(filters.zone_id) ?? filters.zone_id })}
              <button
                onClick={() => setFilters((p) => ({ ...p, zone_id: "" }))}
                style={chipBtnStyle}
              >
                ×
              </button>
            </span>
          )}
        </div>
      )}

      {/* Bulk action bar */}
      {selectedIds.size > 0 && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            background: "#1D3461",
            border: "1px solid #3B82F6",
            borderRadius: 8,
            padding: "10px 16px",
          }}
        >
          <span style={{ color: "#93C5FD", fontSize: 13, fontWeight: 600 }}>
            {t("selectedCount", { count: selectedIds.size })}
          </span>
          {canCreateWriteOff && <Btn size="sm" variant="danger" icon={<Trash2 size={13} />} disabled={!canWriteOffSelection} onClick={() => openWriteOff(selectedBatches)}>{t("writeOff.selected")}</Btn>}
          {canCreateWriteOff && selectedBatches.length > 0 && !canWriteOffSelection && <span style={{ color: "#FBBF24", fontSize: 12 }}>{t("writeOff.singleStore")}</span>}
          <button
            onClick={() => setSelectedIds(new Set())}
            style={{
              background: "transparent",
              border: "none",
              color: "#6B7280",
              fontSize: 12,
              cursor: "pointer",
              marginLeft: "auto",
            }}
          >
            {t("clearSelection")}
          </button>
        </div>
      )}

      {/* Batches count label — StockTable now renders its own card shell (shared Table
          component), so this no longer wraps it in a second bordered box like it used to. */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <span style={{ color: "#4B5563", fontSize: 12 }}>
          {t("batchesLabel")} <span style={{ color: "#9CA3AF", fontWeight: 600 }}>{countLabel}</span>
        </span>
      </div>

      <p style={{ color: "#8A94A8", fontSize: 12, margin: 0 }}>
        {t("batchExplanation", { products: new Set(batches.map(batch => batch.productId)).size })}
      </p>
      {writeOffCompleted && <p role="status" style={{ color: "#4ADE80", margin: 0 }}>{t("writeOff.completed")}</p>}

      <StockTable
        items={batches}
        isLoading={isLoading || isPlaceholderData}
        selectedIds={selectedIds}
        onSelectId={handleSelectId}
        onSelectAll={handleSelectAll}
        onVerify={(id) => verify.mutate(id)}
        onWriteOff={canCreateWriteOff ? batch => openWriteOff([batch]) : undefined}
        sortBy={sortBy}
        sortDescending={sortDescending}
        onSort={handleSort}
      />

      {!isLoading && batches.length > 0 && (
        <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPageChange={setPage} />
      )}

      {writeOffBatches && (
        <StockWriteOffDialog
          batches={writeOffBatches}
          canApprove={canApproveWriteOff}
          onClose={() => setWriteOffBatches(null)}
          onCompleted={() => { setWriteOffBatches(null); setSelectedIds(new Set()); setWriteOffCompleted(true); }}
        />
      )}

      {/* Add batch modal */}
      {showAddModal && (
        <Modal title={t("modalTitle")} onClose={() => setShowAddModal(false)}>
          <AddBatchForm
            stores={storeOptions}
            products={productOptions}
            onSuccess={() => setShowAddModal(false)}
            onCancel={() => setShowAddModal(false)}
          />
        </Modal>
      )}
    </div>
  );
}

export default function StockPage() {
  return (
    <Suspense>
      <StockPageContent />
    </Suspense>
  );
}
