"use client";

import { Suspense, useState, useMemo } from "react";
import { useSearchParams } from "next/navigation";
import { Plus } from "lucide-react";
import { useStock, useVerifyStock } from "@/features/shelf/hooks/useStock";
import { useStores } from "@/features/stores/hooks/useStores";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { StockTable } from "@/features/shelf/components/StockTable";
import { StockFilters } from "@/features/shelf/components/StockFilters";
import { AddBatchForm } from "@/features/shelf/components/AddBatchForm";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";

interface Filters {
  status: string;
  search: string;
  store_id: string;
  zone_id: string;
}

function StockPageContent() {
  const searchParams = useSearchParams();
  const [filters, setFilters] = useState<Filters>({
    status: searchParams.get("status") ?? "",
    search: "",
    store_id: searchParams.get("store_id") ?? "",
    zone_id: searchParams.get("zone_id") ?? "",
  });
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [showAddModal, setShowAddModal] = useState(false);

  const { data: batches = [], isLoading } = useStock({
    status: filters.status || undefined,
    store_id: filters.store_id || undefined,
    zone_id: filters.zone_id || undefined,
  });
  const { data: stores = [] } = useStores();
  const { data: products = [] } = useCatalogProducts();
  const verify = useVerifyStock();

  const filtered = useMemo(() => {
    if (!filters.search) return batches;
    const q = filters.search.toLowerCase();
    return batches.filter(
      (b) =>
        b.productName.toLowerCase().includes(q) ||
        (b.productBarcode ?? "").toLowerCase().includes(q) ||
        (b.batchNumber ?? "").toLowerCase().includes(q),
    );
  }, [batches, filters.search]);

  function handleSelectId(id: string, checked: boolean) {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function handleSelectAll(checked: boolean) {
    if (checked) setSelectedIds(new Set(filtered.map((b) => b.id)));
    else setSelectedIds(new Set());
  }

  const storeOptions = stores.map((s) => ({
    id: s.id,
    name: s.name,
    zones: s.zones.map((z) => ({ id: z.id, name: z.name })),
  }));

  const productOptions = products.map((p) => ({
    id: p.id,
    name: p.name,
    barcode: p.barcode ?? null,
  }));

  const countLabel =
    filtered.length !== batches.length
      ? `${filtered.length} з ${batches.length}`
      : String(filtered.length);

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
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Залишки</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            Партії товарів — терміни придатності та кількості
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={() => setShowAddModal(true)}>
          Додати партію
        </Btn>
      </div>

      {/* Filters */}
      <StockFilters
        filters={{ status: filters.status, search: filters.search }}
        onChange={(partial) => setFilters((prev) => ({ ...prev, ...partial, store_id: prev.store_id, zone_id: prev.zone_id }))}
      />

      {/* Active store/zone filter chips */}
      {(filters.store_id || filters.zone_id) && (
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          {filters.store_id && (
            <span style={chipStyle}>
              Магазин: {filters.store_id}
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
              Зона: {filters.zone_id}
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
            Обрано: {selectedIds.size}
          </span>
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
            Зняти вибір
          </button>
        </div>
      )}

      {/* Table card */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "hidden",
        }}
      >
        {/* Table header bar */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "14px 16px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <span style={{ color: "#4B5563", fontSize: 12 }}>
            Партій: <span style={{ color: "#9CA3AF", fontWeight: 600 }}>{countLabel}</span>
          </span>
        </div>

        <StockTable
          items={filtered}
          isLoading={isLoading}
          selectedIds={selectedIds}
          onSelectId={handleSelectId}
          onSelectAll={handleSelectAll}
          onVerify={(id) => verify.mutate(id)}
        />
      </div>

      {/* Add batch modal */}
      {showAddModal && (
        <Modal title="Нова партія" onClose={() => setShowAddModal(false)}>
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
