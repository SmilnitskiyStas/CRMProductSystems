"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Loader2, Plus, Upload } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Pagination } from "@/components/ui/Pagination";
import { SalesTable } from "@/features/sales/components/SalesTable";
import { SaleEntryForm } from "@/features/sales/components/SaleEntryForm";
import { CsvImportDialog } from "@/features/sales/components/CsvImportDialog";
import {
  useDailySales,
  useImportCsv,
  useMarkAnomaly,
  useUpsertSale,
} from "@/features/sales/hooks/useSales";
import { useProducts } from "@/features/inventory/hooks/useProducts";
import { usePrimaryStoreId, useStoreContext } from "@/lib/useStoreContext";
import type { SalesSortBy, UpsertDailySalePayload } from "@/features/sales/types";

const PAGE_SIZE = 50;

function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

const selectStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
};

export default function SalesPage() {
  const t = useTranslations("Dashboard.sales.page");
  const tCommon = useTranslations("Common");
  // Read-list filter (TASK-611) uses the full multi-store selection; primaryStoreId is kept for
  // the entry form / CSV import below, which stay single-store writes per TASK-610's backend
  // contract (upsert/import endpoints were not widened).
  const primaryStoreId = usePrimaryStoreId();
  const selectedStoreIds = useStoreContext((s) => s.selectedStoreIds);
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(daysAgo(0));
  const [entryOpen, setEntryOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);

  const { data: products = [] } = useProducts();
  const [page, setPage] = useState(1);
  // Default matches the server: newest day first.
  const [sortBy, setSortBy] = useState<SalesSortBy>("date");
  const [sortDescending, setSortDescending] = useState(true);
  const filters = useMemo(
    () => ({ storeIds: selectedStoreIds, from, to, page, pageSize: PAGE_SIZE, sortBy, sortDescending }),
    [selectedStoreIds, from, to, page, sortBy, sortDescending],
  );
  const { data, isLoading, isFetching, isError } = useDailySales(filters);
  const sales = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  // Paging: scroll the page back to the very top.
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) { firstRender.current = false; return; }
    window.scrollTo(0, 0);
  }, [page]);

  // Switching stores also restarts from the first page.
  useEffect(() => { setPage(1); }, [selectedStoreIds]);

  // Same toggle convention as the Catalog: the same column flips direction, a new column starts
  // descending (date is the default, newest first). Sorting restarts from page 1.
  const handleSort = (key: SalesSortBy) => {
    if (key === sortBy) setSortDescending((d) => !d);
    else { setSortBy(key); setSortDescending(true); }
    setPage(1);
  };

  // A changed filter starts from the first page — otherwise page 7 of the old range may not exist.
  const changeFrom = (v: string) => { setFrom(v); setPage(1); };
  const changeTo = (v: string) => { setTo(v); setPage(1); };

  const upsert = useUpsertSale();
  const importCsv = useImportCsv();
  const markAnomaly = useMarkAnomaly();

  const handleUpsert = (payload: UpsertDailySalePayload) => {
    upsert.mutate(payload, {
      onSuccess: () => {
        toast.success(t("toastSaved"));
        setEntryOpen(false);
      },
      onError: (err) => toast.error(err.message),
    });
  };

  const handleImport = (file: File) => {
    if (!primaryStoreId) return;
    importCsv.mutate(
      { storeId: primaryStoreId, file },
      {
        onSuccess: (r) =>
          r.errors.length === 0
            ? toast.success(t("toastImported", { count: r.created + r.updated }))
            : toast.warning(t("toastImportedWithSkips", { count: r.skipped })),
        onError: (err) => toast.error(err.message),
      },
    );
  };

  const handleToggleAnomaly = (id: string, isAnomaly: boolean) => {
    markAnomaly.mutate(
      { id, isAnomaly },
      {
        onSuccess: () =>
          toast.success(isAnomaly ? t("toastExcludedFromAdu") : t("toastIncludedInAdu")),
        onError: (err) => toast.error(err.message),
      },
    );
  };

  if (isError) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 13 }}>
        {t("errorLoading")}
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 22 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {t("subtitle")}
          </p>
        </div>
        <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
          {!primaryStoreId && (
            <span style={{ color: "#6B7280", fontSize: 12 }}>{t("selectStoreHint")}</span>
          )}
          <Btn
            variant="ghost"
            icon={<Upload size={15} />}
            onClick={() => setImportOpen(true)}
            disabled={!primaryStoreId}
          >
            {t("importCsv")}
          </Btn>
          <Btn icon={<Plus size={15} />} onClick={() => setEntryOpen(true)} disabled={!primaryStoreId}>
            {t("addSale")}
          </Btn>
        </div>
      </div>

      {/* Filters */}
      <div style={{ display: "flex", gap: 10, marginBottom: 18, alignItems: "center" }}>
        <input type="date" value={from} max={to} onChange={(e) => changeFrom(e.target.value)} style={selectStyle} />
        <span style={{ color: "#4B5563" }}>—</span>
        <input type="date" value={to} min={from} max={daysAgo(0)} onChange={(e) => changeTo(e.target.value)} style={selectStyle} />
        <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
          {isFetching && (
            <Loader2 size={14} style={{ animation: "spin 1s linear infinite", marginRight: 6, verticalAlign: "-2px" }} />
          )}
          {isLoading ? tCommon("loading") : t("recordsCount", { count: totalCount })}
        </span>
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>

      <div
        style={{
          position: "relative",
          opacity: isFetching ? 0.55 : 1,
          transition: "opacity 0.15s",
          pointerEvents: isFetching ? "none" : undefined,
        }}
      >
        {isFetching && (
          <div style={{ position: "absolute", top: 60, left: 0, right: 0, display: "flex", justifyContent: "center", zIndex: 2 }}>
            <Loader2 size={30} color="#3B82F6" style={{ animation: "spin 1s linear infinite" }} />
          </div>
        )}
        <SalesTable
          sales={sales}
          onToggleAnomaly={handleToggleAnomaly}
          sortBy={sortBy}
          sortDescending={sortDescending}
          onSort={handleSort}
        />
      </div>

      <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPageChange={setPage} />

      {entryOpen && primaryStoreId && (
        <SaleEntryForm
          storeId={primaryStoreId}
          products={products}
          isPending={upsert.isPending}
          error={upsert.error?.message ?? null}
          onClose={() => setEntryOpen(false)}
          onSubmit={handleUpsert}
        />
      )}

      {importOpen && primaryStoreId && (
        <CsvImportDialog
          isPending={importCsv.isPending}
          result={importCsv.data ?? null}
          error={importCsv.error?.message ?? null}
          onClose={() => {
            setImportOpen(false);
            importCsv.reset();
          }}
          onImport={handleImport}
        />
      )}
    </div>
  );
}
