"use client";

import { useMemo, useState } from "react";
import { Plus, Upload } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
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
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import type { UpsertDailySalePayload } from "@/features/sales/types";

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
  const primaryStoreId = usePrimaryStoreId();
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(daysAgo(0));
  const [entryOpen, setEntryOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);

  const { data: products = [] } = useProducts();
  const filters = useMemo(
    () => ({ storeId: primaryStoreId, from, to }),
    [primaryStoreId, from, to],
  );
  const { data: sales = [], isLoading, isError } = useDailySales(filters);

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
        <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} style={selectStyle} />
        <span style={{ color: "#4B5563" }}>—</span>
        <input type="date" value={to} min={from} max={daysAgo(0)} onChange={(e) => setTo(e.target.value)} style={selectStyle} />
        <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
          {isLoading ? tCommon("loading") : t("recordsCount", { count: sales.length })}
        </span>
      </div>

      <SalesTable sales={sales} onToggleAnomaly={handleToggleAnomaly} />

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
