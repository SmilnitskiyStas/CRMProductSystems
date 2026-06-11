"use client";

import { useMemo, useState } from "react";
import { Plus, Upload } from "lucide-react";
import { toast } from "sonner";
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
import { useStores } from "@/features/stores/hooks/useStores";
import { useProducts } from "@/features/inventory/hooks/useProducts";
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
  const [storeId, setStoreId] = useState<string>("");
  const [from, setFrom] = useState(daysAgo(30));
  const [to, setTo] = useState(daysAgo(0));
  const [entryOpen, setEntryOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);

  const { data: stores = [] } = useStores();
  const { data: products = [] } = useProducts();
  const filters = useMemo(
    () => ({ storeId: storeId || undefined, from, to }),
    [storeId, from, to],
  );
  const { data: sales = [], isLoading, isError } = useDailySales(filters);

  const upsert = useUpsertSale();
  const importCsv = useImportCsv();
  const markAnomaly = useMarkAnomaly();

  const defaultStoreId = storeId || stores[0]?.id || "";

  const handleUpsert = (payload: UpsertDailySalePayload) => {
    upsert.mutate(payload, {
      onSuccess: () => {
        toast.success("Продажі збережено");
        setEntryOpen(false);
      },
      onError: (err) => toast.error(err.message),
    });
  };

  const handleImport = (importStoreId: string, file: File) => {
    importCsv.mutate(
      { storeId: importStoreId, file },
      {
        onSuccess: (r) =>
          r.errors.length === 0
            ? toast.success(`Імпортовано: ${r.created + r.updated}`)
            : toast.warning(`Імпортовано з пропусками: ${r.skipped}`),
        onError: (err) => toast.error(err.message),
      },
    );
  };

  const handleToggleAnomaly = (id: string, isAnomaly: boolean) => {
    markAnomaly.mutate(
      { id, isAnomaly },
      {
        onSuccess: () =>
          toast.success(isAnomaly ? "Виключено з розрахунку ADU" : "Повернуто до розрахунку ADU"),
        onError: (err) => toast.error(err.message),
      },
    );
  };

  if (isError) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 13 }}>
        Помилка завантаження продажів. Перевірте підключення до API.
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 22 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Продажі
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            Історія щоденних продажів — джерело даних для ADU та автозамовлень
          </p>
        </div>
        <div style={{ display: "flex", gap: 10 }}>
          <Btn variant="ghost" icon={<Upload size={15} />} onClick={() => setImportOpen(true)}>
            Імпорт CSV
          </Btn>
          <Btn icon={<Plus size={15} />} onClick={() => setEntryOpen(true)}>
            Внести продажі
          </Btn>
        </div>
      </div>

      {/* Filters */}
      <div style={{ display: "flex", gap: 10, marginBottom: 18, alignItems: "center" }}>
        <select value={storeId} onChange={(e) => setStoreId(e.target.value)} style={selectStyle}>
          <option value="">Всі магазини</option>
          {stores.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
        <input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} style={selectStyle} />
        <span style={{ color: "#4B5563" }}>—</span>
        <input type="date" value={to} min={from} max={daysAgo(0)} onChange={(e) => setTo(e.target.value)} style={selectStyle} />
        <span style={{ color: "#4B5563", fontSize: 12, marginLeft: "auto" }}>
          {isLoading ? "Завантаження…" : `записів: ${sales.length}`}
        </span>
      </div>

      <SalesTable sales={sales} onToggleAnomaly={handleToggleAnomaly} />

      {entryOpen && (
        <SaleEntryForm
          stores={stores}
          products={products}
          defaultStoreId={defaultStoreId}
          isPending={upsert.isPending}
          error={upsert.error?.message ?? null}
          onClose={() => setEntryOpen(false)}
          onSubmit={handleUpsert}
        />
      )}

      {importOpen && (
        <CsvImportDialog
          stores={stores}
          defaultStoreId={defaultStoreId}
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
