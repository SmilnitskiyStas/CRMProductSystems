"use client";

import { useState } from "react";
import { Plus, Pencil, Trash2, X } from "lucide-react";
import {
  useServiceCatalog,
  useCreateServiceItem,
  useUpdateServiceItem,
  useDeleteServiceItem,
} from "../hooks/useAutoService";
import { autoServiceApi } from "../api/auto-service-api";
import { useQueryClient } from "@tanstack/react-query";
import { AUTO_SERVICE_KEYS } from "../hooks/useAutoService";
import type {
  ServiceCatalogItemDto,
  CreateServiceCatalogItemRequest,
  UpdateServiceCatalogItemRequest,
} from "../types";

// ─── Service Item Form ────────────────────────────────────────────────────────

interface FormProps {
  item?: ServiceCatalogItemDto;
  onClose: () => void;
}

function ServiceItemForm({ item, onClose }: FormProps) {
  const createItem = useCreateServiceItem();
  const updateItem = useUpdateServiceItem(item?.id ?? "");

  const [name, setName] = useState(item?.name ?? "");
  const [description, setDescription] = useState(item?.description ?? "");
  const [defaultPrice, setDefaultPrice] = useState<number>(item?.defaultPrice ?? 0);
  const [durationMinutes, setDurationMinutes] = useState<number | "">(
    item?.durationMinutes ?? ""
  );
  const [isActive, setIsActive] = useState(item?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!item;
  const isPending = createItem.isPending || updateItem.isPending;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) { setError("Введіть назву"); return; }

    if (isEdit) {
      const body: UpdateServiceCatalogItemRequest = {
        name: name.trim(),
        description: description.trim() || undefined,
        defaultPrice,
        durationMinutes: durationMinutes !== "" ? Number(durationMinutes) : undefined,
        isActive,
      };
      updateItem.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : "Помилка"),
      });
    } else {
      const body: CreateServiceCatalogItemRequest = {
        name: name.trim(),
        description: description.trim() || undefined,
        defaultPrice,
        durationMinutes: durationMinutes !== "" ? Number(durationMinutes) : undefined,
      };
      createItem.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : "Помилка"),
      });
    }
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px",
          width: "100%",
          maxWidth: 460,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {isEdit ? "Редагувати послугу" : "Нова послуга"}
          </h2>
          <button onClick={onClose} style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={labelStyle}>Назва *</label>
            <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder="Заміна масла" style={inputStyle} />
          </div>
          <div>
            <label style={labelStyle}>Опис</label>
            <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={2} style={{ ...inputStyle, resize: "vertical" }} />
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={labelStyle}>Ціна за замовч., грн</label>
              <input
                type="number"
                min={0}
                step={0.01}
                value={defaultPrice}
                onChange={(e) => setDefaultPrice(Number(e.target.value))}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>Тривалість, хв</label>
              <input
                type="number"
                min={1}
                step={1}
                value={durationMinutes}
                onChange={(e) => setDurationMinutes(e.target.value ? Number(e.target.value) : "")}
                placeholder="60"
                style={inputStyle}
              />
            </div>
          </div>
          {isEdit && (
            <label style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                style={{ accentColor: "#3B82F6" }}
              />
              <span style={{ color: "#9CA3AF", fontSize: 13 }}>Активна</span>
            </label>
          )}

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <button type="button" onClick={onClose} style={btnSecondaryStyle}>Скасувати</button>
            <button type="submit" disabled={isPending} style={btnPrimaryStyle}>
              {isPending ? "Збереження…" : isEdit ? "Зберегти" : "Додати послугу"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

// ─── Deactivate button (separate component to use hook correctly) ─────────────

function DeactivateButton({ item }: { item: ServiceCatalogItemDto }) {
  const queryClient = useQueryClient();

  async function handleDeactivate() {
    if (!confirm(`Деактивувати послугу "${item.name}"?`)) return;
    await autoServiceApi.updateServiceItem(item.id, {
      name: item.name,
      description: item.description ?? undefined,
      defaultPrice: item.defaultPrice,
      durationMinutes: item.durationMinutes ?? undefined,
      isActive: false,
    });
    queryClient.invalidateQueries({ queryKey: AUTO_SERVICE_KEYS.serviceCatalog });
  }

  if (!item.isActive) return null;

  return (
    <button
      onClick={handleDeactivate}
      style={{
        padding: "3px 8px",
        borderRadius: 6,
        border: "1px solid #374151",
        background: "transparent",
        color: "#6B7280",
        fontSize: 11,
        cursor: "pointer",
      }}
      title="Деактивувати"
    >
      Вимкнути
    </button>
  );
}

// ─── Main ServiceCatalogTable ─────────────────────────────────────────────────

export function ServiceCatalogTable() {
  const { data: items, isLoading, isError } = useServiceCatalog();
  const deleteItem = useDeleteServiceItem();

  const [showCreate, setShowCreate] = useState(false);
  const [editingItem, setEditingItem] = useState<ServiceCatalogItemDto | null>(null);

  if (isLoading) {
    return <div style={{ padding: "32px", color: "#6B7280", fontSize: 13 }}>Завантаження…</div>;
  }
  if (isError) {
    return <div style={{ padding: "32px", color: "#F87171", fontSize: 13 }}>Не вдалося завантажити каталог.</div>;
  }

  return (
    <>
      {/* Toolbar */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Каталог послуг</h1>
          <p style={{ color: "#4B5563", fontSize: 14, marginTop: 4 }}>
            Послуги, що надаються автосервісом
          </p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            padding: "9px 16px",
            borderRadius: 8,
            border: "none",
            background: "#1D4ED8",
            color: "#E8EDF5",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Plus size={16} /> Послуга
        </button>
      </div>

      {/* Table */}
      <div style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
        {/* Header */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "1fr 2fr 120px 100px 90px 130px",
            padding: "10px 16px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          {["Назва", "Опис", "Ціна, грн", "Тривалість", "Статус", ""].map((h) => (
            <div key={h} style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>
              {h}
            </div>
          ))}
        </div>

        {/* Rows */}
        {!items || items.length === 0 ? (
          <div style={{ padding: "40px 16px", textAlign: "center", color: "#374151", fontSize: 13 }}>
            Каталог порожній. Додайте першу послугу.
          </div>
        ) : (
          items.map((item) => (
            <div key={item.id} style={{ borderBottom: "1px solid #1F2937" }}>
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1fr 2fr 120px 100px 90px 130px",
                  padding: "12px 16px",
                  alignItems: "center",
                }}
              >
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{item.name}</div>
                <div style={{ color: "#6B7280", fontSize: 12 }}>{item.description ?? "—"}</div>
                <div style={{ color: "#E8EDF5", fontSize: 13 }}>{item.defaultPrice.toFixed(2)}</div>
                <div style={{ color: "#9CA3AF", fontSize: 13 }}>
                  {item.durationMinutes ? `${item.durationMinutes} хв` : "—"}
                </div>
                <div>
                  <span
                    style={{
                      display: "inline-block",
                      padding: "2px 8px",
                      borderRadius: 12,
                      fontSize: 11,
                      fontWeight: 600,
                      background: item.isActive ? "#064E3B" : "#1F2937",
                      color: item.isActive ? "#34D399" : "#6B7280",
                    }}
                  >
                    {item.isActive ? "Активна" : "Неактивна"}
                  </span>
                </div>
                <div style={{ display: "flex", gap: 6, justifyContent: "flex-end", alignItems: "center" }}>
                  <DeactivateButton item={item} />
                  <button
                    onClick={() => setEditingItem(item)}
                    style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: "4px 6px" }}
                    title="Редагувати"
                  >
                    <Pencil size={13} />
                  </button>
                  <button
                    onClick={() => {
                      if (confirm(`Видалити послугу "${item.name}"?`)) {
                        deleteItem.mutate(item.id);
                      }
                    }}
                    style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: "4px 6px" }}
                    title="Видалити"
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      {showCreate && <ServiceItemForm onClose={() => setShowCreate(false)} />}
      {editingItem && (
        <ServiceItemForm item={editingItem} onClose={() => setEditingItem(null)} />
      )}
    </>
  );
}

const labelStyle: React.CSSProperties = { color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" };
const inputStyle: React.CSSProperties = {
  width: "100%",
  padding: "9px 12px",
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};
const btnPrimaryStyle: React.CSSProperties = { padding: "9px 20px", borderRadius: 8, border: "none", background: "#1D4ED8", color: "#E8EDF5", fontSize: 13, fontWeight: 600, cursor: "pointer" };
const btnSecondaryStyle: React.CSSProperties = { padding: "9px 16px", borderRadius: 8, border: "1px solid #1F2937", background: "transparent", color: "#6B7280", fontSize: 13, cursor: "pointer" };
