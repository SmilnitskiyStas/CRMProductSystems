"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Pencil, Trash2, Plus, ImageOff } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useCabinetItems, useDeleteCabinetItem } from "../hooks/useSupplierCabinet";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";
import { CabinetItemModal } from "./CabinetItemModal";
import { SupplierItemDetailDialog } from "@/features/marketplace/components/SupplierItemDetailDialog";
import type { CabinetItem } from "../types";
import type { SupplierItemDto } from "@/features/marketplace/types";

export function CabinetItemsTable() {
  const t = useTranslations("Dashboard.supplierCabinet.itemsTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data, isLoading, isError, error } = useCabinetItems();
  const { data: categories = [] } = useItemCategories();
  const deleteItem = useDeleteCabinetItem();

  const [modal, setModal] = useState<{ open: boolean; item?: CabinetItem }>({ open: false });
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [detailItem, setDetailItem] = useState<SupplierItemDto | null>(null);

  if (isLoading) {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {[...Array(5)].map((_, i) => (
          <div key={i} style={{ height: 44, background: "#111827", borderRadius: 8 }} />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div style={{ color: "#F87171", fontSize: 13 }}>
        {t("errorLoad")}{" "}
        {error instanceof Error ? error.message : ""}
      </div>
    );
  }

  function handleDelete(id: string) {
    setDeleteError(null);
    deleteItem.mutate(id, {
      onSettled: () => setConfirmDeleteId(null),
      onError: (err) =>
        setDeleteError(err instanceof Error ? err.message : t("deleteErrorDefault")),
    });
  }

  const columns: TableColumn<CabinetItem>[] = [
    {
      key: "image",
      header: "",
      width: 40,
      render: (item) => {
        const mainImage = item.images.find((i) => i.kind === "main") ?? item.images[0];
        return (
          <div
            style={{
              width: 32,
              height: 32,
              borderRadius: 6,
              background: "#0D1117",
              border: "1px solid #1F2937",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              overflow: "hidden",
            }}
          >
            {mainImage ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={mainImage.url}
                alt=""
                style={{ width: "100%", height: "100%", objectFit: "cover" }}
              />
            ) : (
              <ImageOff size={14} color="#4B5563" />
            )}
          </div>
        );
      },
    },
    {
      // Displaced from column 0 by the leading image thumbnail — kept left-aligned
      // as the row's real label column (same exception StockTable.tsx documents
      // for its checkbox+name pair).
      key: "name",
      header: t("headerName"),
      align: "left",
      render: (item) => (
        <>
          {item.customName ?? item.itemName ?? "—"}
          {item.category && (
            <span
              style={{
                marginLeft: 8,
                display: "inline-block",
                padding: "2px 8px",
                borderRadius: 4,
                fontSize: 11,
                fontWeight: 600,
                background: "#1E293B",
                color: "#93C5FD",
              }}
            >
              {categories.find((c) => c.key === item.category)?.labelUa ?? item.category}
            </span>
          )}
        </>
      ),
    },
    {
      key: "price",
      header: t("headerPrice"),
      render: (item) =>
        item.price != null
          ? item.price.toLocaleString(intlLocale, {
              style: "currency",
              currency: "UAH",
              minimumFractionDigits: 2,
            })
          : "—",
    },
    {
      key: "minQty",
      header: t("headerMinQty"),
      render: (item) => item.minQty ?? "—",
    },
    {
      key: "unit",
      header: t("headerUnit"),
      cellStyle: { color: "#9CA3AF" },
      render: (item) => item.unit ?? "—",
    },
    {
      key: "availability",
      header: t("headerAvailability"),
      render: (item) => (
        <span
          style={{
            display: "inline-block",
            padding: "2px 8px",
            borderRadius: 4,
            fontSize: 11,
            fontWeight: 600,
            background: item.isAvailable ? "#052e16" : "#1c1917",
            color: item.isAvailable ? "#4ADE80" : "#6B7280",
          }}
        >
          {item.isAvailable ? t("available") : t("unavailable")}
        </span>
      ),
    },
    {
      key: "actions",
      header: t("headerActions"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (item) => (
        <>
          <button
            title={t("detailsTitle")}
            onClick={() => setDetailItem(item as unknown as SupplierItemDto)}
            style={{
              background: "transparent",
              border: "1px solid #374151",
              borderRadius: 7,
              color: "#9CA3AF",
              fontSize: 11,
              fontWeight: 600,
              padding: "4px 8px",
              cursor: "pointer",
              marginRight: 6,
            }}
          >
            {t("detailsButton")}
          </button>
          <button
            title={t("editTitle")}
            onClick={() => setModal({ open: true, item })}
            style={{
              background: "transparent",
              border: "none",
              color: "#6B7280",
              cursor: "pointer",
              padding: 4,
              marginRight: 6,
            }}
          >
            <Pencil size={15} />
          </button>
          {confirmDeleteId === item.id ? (
            <>
              <button
                onClick={() => handleDelete(item.id)}
                disabled={deleteItem.isPending}
                style={{
                  background: "#2d0a0a",
                  border: "1px solid #7F1D1D",
                  borderRadius: 6,
                  color: "#F87171",
                  cursor: "pointer",
                  padding: "2px 8px",
                  fontSize: 11,
                  fontWeight: 600,
                  marginRight: 4,
                }}
              >
                {deleteItem.isPending ? t("deletePending") : t("confirmYes")}
              </button>
              <button
                onClick={() => setConfirmDeleteId(null)}
                style={{
                  background: "transparent",
                  border: "1px solid #374151",
                  borderRadius: 6,
                  color: "#9CA3AF",
                  cursor: "pointer",
                  padding: "2px 8px",
                  fontSize: 11,
                }}
              >
                {t("confirmNo")}
              </button>
            </>
          ) : (
            <button
              title={t("deleteTitle")}
              onClick={() => setConfirmDeleteId(item.id)}
              style={{
                background: "transparent",
                border: "none",
                color: "#6B7280",
                cursor: "pointer",
                padding: 4,
              }}
            >
              <Trash2 size={15} />
            </button>
          )}
        </>
      ),
    },
  ];

  return (
    <div>
      <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 16 }}>
        <Btn onClick={() => setModal({ open: true })} icon={<Plus size={14} />}>
          {t("addButton")}
        </Btn>
      </div>

      {deleteError && (
        <div style={{ color: "#F87171", fontSize: 13, marginBottom: 12 }}>{deleteError}</div>
      )}

      <Table
        columns={columns}
        rows={data ?? []}
        rowKey={(item) => item.id}
        emptyMessage={t("emptyCatalog")}
      />

      {modal.open && (
        <CabinetItemModal item={modal.item} onClose={() => setModal({ open: false })} />
      )}

      <SupplierItemDetailDialog item={detailItem} onClose={() => setDetailItem(null)} />
    </div>
  );
}
