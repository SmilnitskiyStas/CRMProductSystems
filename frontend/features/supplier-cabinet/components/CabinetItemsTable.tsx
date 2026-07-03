"use client";

import { useState } from "react";
import { Pencil, Trash2, Plus } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useCabinetItems, useDeleteCabinetItem } from "../hooks/useSupplierCabinet";
import { useItemCategories } from "@/features/marketplace/hooks/useMarketplace";
import { CabinetItemModal } from "./CabinetItemModal";
import type { CabinetItem } from "../types";

const HEADER_CELL: React.CSSProperties = {
  padding: "10px 14px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  textAlign: "left",
  borderBottom: "1px solid #1F2937",
};

const CELL: React.CSSProperties = {
  padding: "12px 14px",
  color: "#E8EDF5",
  fontSize: 13,
  borderBottom: "1px solid #1A2235",
};

export function CabinetItemsTable() {
  const { data, isLoading, isError, error } = useCabinetItems();
  const { data: categories = [] } = useItemCategories();
  const deleteItem = useDeleteCabinetItem();

  const [modal, setModal] = useState<{ open: boolean; item?: CabinetItem }>({ open: false });
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

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
        Не вдалося завантажити каталог.{" "}
        {error instanceof Error ? error.message : ""}
      </div>
    );
  }

  function handleDelete(id: string) {
    setDeleteError(null);
    deleteItem.mutate(id, {
      onSettled: () => setConfirmDeleteId(null),
      onError: (err) =>
        setDeleteError(err instanceof Error ? err.message : "Помилка видалення."),
    });
  }

  return (
    <div>
      <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 16 }}>
        <Btn onClick={() => setModal({ open: true })} icon={<Plus size={14} />}>
          Додати товар
        </Btn>
      </div>

      {deleteError && (
        <div style={{ color: "#F87171", fontSize: 13, marginBottom: 12 }}>{deleteError}</div>
      )}

      {!data || data.length === 0 ? (
        <div
          style={{
            textAlign: "center",
            padding: "48px 0",
            color: "#4B5563",
            fontSize: 14,
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 12,
          }}
        >
          Каталог порожній — додайте перший товар.
        </div>
      ) : (
        <div
          style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 12,
            overflowX: "auto",
          }}
        >
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                <th style={HEADER_CELL}>Назва</th>
                <th style={{ ...HEADER_CELL, textAlign: "right" }}>Ціна</th>
                <th style={{ ...HEADER_CELL, textAlign: "right" }}>Мін. замовл.</th>
                <th style={HEADER_CELL}>Од. вим.</th>
                <th style={{ ...HEADER_CELL, textAlign: "center" }}>Наявність</th>
                <th style={{ ...HEADER_CELL, textAlign: "right" }}>Дії</th>
              </tr>
            </thead>
            <tbody>
              {data.map((item) => (
                <tr key={item.id}>
                  <td style={CELL}>
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
                  </td>
                  <td style={{ ...CELL, textAlign: "right" }}>
                    {item.price != null
                      ? item.price.toLocaleString("uk-UA", {
                          style: "currency",
                          currency: "UAH",
                          minimumFractionDigits: 2,
                        })
                      : "—"}
                  </td>
                  <td style={{ ...CELL, textAlign: "right" }}>{item.minQty ?? "—"}</td>
                  <td style={{ ...CELL, color: "#9CA3AF" }}>{item.unit ?? "—"}</td>
                  <td style={{ ...CELL, textAlign: "center" }}>
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
                      {item.isAvailable ? "В наявності" : "Відсутній"}
                    </span>
                  </td>
                  <td style={{ ...CELL, textAlign: "right", whiteSpace: "nowrap" }}>
                    <button
                      title="Редагувати"
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
                          {deleteItem.isPending ? "…" : "Так"}
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
                          Ні
                        </button>
                      </>
                    ) : (
                      <button
                        title="Видалити"
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
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal.open && (
        <CabinetItemModal item={modal.item} onClose={() => setModal({ open: false })} />
      )}
    </div>
  );
}
