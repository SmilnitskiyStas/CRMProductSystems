"use client";

import { useMemo, useState } from "react";
import { Plus } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useStock } from "../hooks/useStock";
import { useStores } from "@/features/stores/hooks/useStores";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { StatusBadge } from "./StatusBadge";
import { AddBatchForm } from "./AddBatchForm";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { BatchStatus } from "../types";

interface Props {
  productId: string;
  productName: string;
  unit: string;
}

interface ExpiryGroup {
  expiryDate: string;
  quantity: number;
  daysLeft: number;
  status: BatchStatus;
  stores: Map<string, number>;
}

/** Item-page section: stock of this item grouped by expiry date (FEFO order). */
export function ItemBatchesSection({ productId, productName, unit }: Props) {
  const t = useTranslations("Dashboard.inventory.productPage");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const storeId = usePrimaryStoreId();
  const { data, isLoading } = useStock({ product_id: productId, store_id: storeId, pageSize: 200 });
  const { data: stores = [] } = useStores();
  const [showAdd, setShowAdd] = useState(false);

  const groups = useMemo(() => {
    const map = new Map<string, ExpiryGroup>();
    for (const b of data?.items ?? []) {
      if (b.quantity <= 0) continue;
      const g = map.get(b.expiryDate) ?? {
        expiryDate: b.expiryDate, quantity: 0, daysLeft: b.daysLeft,
        status: b.status, stores: new Map<string, number>(),
      };
      g.quantity += b.quantity;
      g.stores.set(b.storeName, (g.stores.get(b.storeName) ?? 0) + b.quantity);
      map.set(b.expiryDate, g);
    }
    return [...map.values()].sort((a, b) => a.expiryDate.localeCompare(b.expiryDate));
  }, [data]);

  const total = groups.reduce((s, g) => s + g.quantity, 0);
  const multiStore = new Set(groups.flatMap((g) => [...g.stores.keys()])).size > 1;
  const fmtQty = (n: number) => `${n.toLocaleString(intlLocale)} ${unit}`;

  const columns: TableColumn<ExpiryGroup>[] = [
    {
      key: "expiry",
      header: t("batchesExpiry"),
      cellStyle: { fontFamily: "monospace", color: "#E8EDF5" },
      render: (g) => new Date(`${g.expiryDate}T00:00:00`).toLocaleDateString(intlLocale),
    },
    { key: "quantity", header: t("batchesQuantity"), cellStyle: { fontFamily: "monospace", fontWeight: 600, color: "#E8EDF5" }, render: (g) => fmtQty(g.quantity) },
    { key: "daysLeft", header: t("batchesDaysLeft"), cellStyle: { fontFamily: "monospace", color: "#E8EDF5" }, render: (g) => g.daysLeft },
    { key: "status", header: t("batchesStatus"), render: (g) => <StatusBadge status={g.status} /> },
    ...(multiStore
      ? [{
          key: "stores",
          header: t("batchesStores"),
          cellStyle: { fontSize: 12 },
          render: (g: ExpiryGroup) => [...g.stores].map(([name, qty]) => `${name}: ${qty}`).join(", "),
        }]
      : []),
  ];

  const storeOptions = stores.map((s) => ({
    id: s.id, name: s.name, zones: s.zones.map((z) => ({ id: z.id, name: z.name })),
  }));

  return (
    <>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 12 }}>
        <div>
          <span style={{ color: "#4B5563", fontSize: 11 }}>{t("batchesTotal")}: </span>
          <span style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
            {fmtQty(total)}
          </span>
        </div>
        <Btn icon={<Plus size={14} />} onClick={() => setShowAdd(true)}>{t("batchesAdd")}</Btn>
      </div>

      {isLoading ? null : groups.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 12 }}>{t("batchesEmpty")}</div>
      ) : (
        <Table
          columns={columns}
          rows={groups}
          rowKey={(g) => g.expiryDate}
          density="compact"
        />
      )}

      {showAdd && (
        <Modal title={t("batchesAddTitle")} onClose={() => setShowAdd(false)}>
          <AddBatchForm
            stores={storeOptions}
            products={[{ id: productId, name: productName, barcode: null }]}
            productId={productId}
            onSuccess={() => setShowAdd(false)}
            onCancel={() => setShowAdd(false)}
          />
        </Modal>
      )}
    </>
  );
}
