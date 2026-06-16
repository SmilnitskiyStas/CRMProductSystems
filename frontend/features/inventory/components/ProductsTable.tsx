"use client";

import { useState } from "react";
import { Eye, Pencil, Trash2, Tag, Thermometer } from "lucide-react";
import type { Product } from "../types";
import { ITEM_TYPE_LABELS } from "./ProductForm";
import { ActionMenu } from "@/components/ui/ActionMenu";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

interface Props {
  products: Product[];
  onEdit: (product: Product) => void;
  onDelete: (id: string) => void;
  isDeleting: boolean;
}

const tdStyle: React.CSSProperties = {
  padding: "10px 16px",
  color: "#9CA3AF",
  fontSize: 13,
  borderBottom: "1px solid #1F2937",
  borderRight: "1px solid #1F2937",
  textAlign: "center",
};

const thStyle: React.CSSProperties = {
  padding: "10px 16px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  borderBottom: "1px solid #374151",
  borderRight: "1px solid #374151",
  background: "#0A0F1A",
  textAlign: "center",
};

// ── Confirm delete dialog ────────────────────────────────────────────────────
function DeleteDialog({
  product,
  onConfirm,
  onCancel,
  isDeleting,
}: {
  product: Product;
  onConfirm: () => void;
  onCancel: () => void;
  isDeleting: boolean;
}) {
  return (
    <>
      <div
        onClick={onCancel}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 400,
          backdropFilter: "blur(2px)",
        }}
      />
      <div
        style={{
          position: "fixed",
          top: "50%", left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(420px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 401,
          padding: "24px 28px",
        }}
      >
        <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: "0 0 8px" }}>
          Видалити товар?
        </h3>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 20px" }}>
          <span style={{ color: "#9CA3AF" }}>{product.name}</span> буде видалено з каталогу
          назавжди. Цю дію не можна скасувати.
        </p>
        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <button
            onClick={onCancel}
            style={{
              padding: "8px 16px",
              background: "transparent",
              border: "1px solid #374151",
              borderRadius: 8,
              color: "#9CA3AF",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            Скасувати
          </button>
          <button
            onClick={onConfirm}
            disabled={isDeleting}
            style={{
              padding: "8px 16px",
              background: "#2d0a0a",
              border: "1px solid #7F1D1D",
              borderRadius: 8,
              color: "#F87171",
              fontSize: 13,
              fontWeight: 600,
              cursor: isDeleting ? "not-allowed" : "pointer",
              opacity: isDeleting ? 0.6 : 1,
            }}
          >
            {isDeleting ? "Видалення…" : "Видалити"}
          </button>
        </div>
      </div>
    </>
  );
}

// ── Detail drawer content ────────────────────────────────────────────────────
function ProductDetail({ p }: { p: Product }) {
  return (
    <>
      <DrawerSection title="Основна інформація">
        <DrawerField label="Назва" value={p.name} />
        <DrawerGrid>
          <DrawerField
            label="Штрихкод"
            value={
              <span style={{ fontFamily: "monospace", color: "#9CA3AF" }}>
                {p.barcode ?? "—"}
              </span>
            }
          />
          <DrawerField label="Одиниця" value={p.unit} />
          <DrawerField label="Категорія" value={p.categoryName ?? "—"} />
          <DrawerField label="Сегмент" value={p.segmentName ?? "—"} />
          <DrawerField label="Тип управління" value={p.managementType} />
          <DrawerField label="Тип товару" value={ITEM_TYPE_LABELS[p.itemType] ?? p.itemType} />
          <DrawerField
            label="Статус"
            value={
              <span
                style={{
                  display: "inline-block",
                  padding: "2px 8px",
                  borderRadius: 20,
                  background: p.isActive ? "#052e16" : "#111827",
                  color: p.isActive ? "#4ADE80" : "#6B7280",
                  fontSize: 11,
                  fontWeight: 600,
                }}
              >
                {p.isActive ? "Активний" : "Неактивний"}
              </span>
            }
          />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title="Ціни та ПДВ">
        <DrawerGrid>
          <DrawerField
            label="Закупівельна ціна"
            value={
              p.pricePurchase != null
                ? `${p.pricePurchase.toLocaleString("uk-UA")} ₴`
                : "—"
            }
          />
          <DrawerField
            label="Роздрібна ціна"
            value={
              p.priceRetail != null
                ? `${p.priceRetail.toLocaleString("uk-UA")} ₴`
                : "—"
            }
            color="#4ADE80"
          />
          <DrawerField label="Ставка ПДВ" value={`${p.vatRate}%`} />
          <DrawerField
            label="Постачальник за замовч."
            value={p.defaultSupplierName ?? "—"}
          />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title="Залишки та буфери">
        <DrawerGrid>
          <DrawerField
            label="Мін. залишок"
            value={
              <span style={{ fontFamily: "monospace" }}>{p.minStock}</span>
            }
          />
          <DrawerField
            label="Макс. залишок"
            value={
              <span style={{ fontFamily: "monospace" }}>{p.maxStock}</span>
            }
          />
          <DrawerField
            label="Буфер безпеки"
            value={
              <span style={{ fontFamily: "monospace" }}>{p.safetyBuffer}</span>
            }
          />
          <DrawerField
            label="Термін зберігання"
            value={p.shelfLifeDays != null ? `${p.shelfLifeDays} дн.` : "—"}
          />
        </DrawerGrid>
      </DrawerSection>

      {(p.storageTempMin != null || p.storageTempMax != null) && (
        <DrawerSection title="Умови зберігання">
          <DrawerGrid>
            <DrawerField
              label="Темп. мін."
              value={p.storageTempMin != null ? `${p.storageTempMin}°C` : "—"}
            />
            <DrawerField
              label="Темп. макс."
              value={p.storageTempMax != null ? `${p.storageTempMax}°C` : "—"}
            />
          </DrawerGrid>
        </DrawerSection>
      )}

      <DrawerSection title="Системна інформація">
        <DrawerField
          label="ID"
          value={
            <span style={{ fontFamily: "monospace", fontSize: 11, color: "#4B5563" }}>
              {p.id}
            </span>
          }
        />
        <DrawerField
          label="Дата створення"
          value={new Date(p.createdAt).toLocaleDateString("uk-UA")}
        />
      </DrawerSection>
    </>
  );
}

// ── Table ────────────────────────────────────────────────────────────────────
export function ProductsTable({ products, onEdit, onDelete, isDeleting }: Props) {
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Product | null>(null);

  const pendingProduct = products.find((p) => p.id === pendingDeleteId) ?? null;

  return (
    <>
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "auto",
        }}
      >
        <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 800 }}>
          <thead>
            <tr>
              {[
                "Штрихкод", "Назва", "Категорія", "Тип товару", "Одиниця",
                "Закупівля", "Роздриб", "Мін.", "Макс.",
                "Статус", "Дії",
              ].map((h) => (
                <th key={h} style={h === "Дії" ? { ...thStyle, borderRight: "none" } : thStyle}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {products.length === 0 ? (
              <tr>
                <td
                  colSpan={11}
                  style={{
                    padding: "40px 0",
                    textAlign: "center",
                    color: "#4B5563",
                    fontSize: 13,
                  }}
                >
                  Товарів ще немає. Додайте перший товар.
                </td>
              </tr>
            ) : (
              products.map((product) => (
                <tr
                  key={product.id}
                  style={{ transition: "background 0.1s" }}
                  onMouseEnter={(e) => {
                    (e.currentTarget as HTMLElement).style.background = "#0A1628";
                  }}
                  onMouseLeave={(e) => {
                    (e.currentTarget as HTMLElement).style.background = "transparent";
                  }}
                >
                  <td
                    style={{
                      ...tdStyle,
                      fontFamily: "monospace",
                      fontSize: 12,
                      color: "#4B5563",
                    }}
                  >
                    {product.barcode ?? "—"}
                  </td>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {product.name}
                  </td>
                  <td style={tdStyle}>{product.categoryName ?? "—"}</td>
                  <td style={tdStyle}>{ITEM_TYPE_LABELS[product.itemType] ?? product.itemType ?? "—"}</td>
                  <td style={tdStyle}>{product.unit}</td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>
                    {product.pricePurchase != null
                      ? product.pricePurchase.toLocaleString("uk-UA") + " ₴"
                      : "—"}
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>
                    {product.priceRetail != null
                      ? product.priceRetail.toLocaleString("uk-UA") + " ₴"
                      : "—"}
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{product.minStock}</td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{product.maxStock}</td>
                  <td style={tdStyle}>
                    <span
                      style={{
                        display: "inline-block",
                        padding: "3px 8px",
                        borderRadius: 20,
                        background: product.isActive ? "#052e16" : "#111827",
                        color: product.isActive ? "#4ADE80" : "#6B7280",
                        fontSize: 11,
                        fontWeight: 600,
                      }}
                    >
                      {product.isActive ? "Активний" : "Неактивний"}
                    </span>
                  </td>
                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: "Переглянути",
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(product),
                        },
                        { separator: true },
                        {
                          label: "Редагувати",
                          icon: <Pencil size={13} />,
                          onClick: () => onEdit(product),
                        },
                        {
                          label: "Видалити",
                          icon: <Trash2 size={13} />,
                          variant: "danger",
                          onClick: () => setPendingDeleteId(product.id),
                        },
                      ]}
                    />
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Delete confirm */}
      {pendingProduct && (
        <DeleteDialog
          product={pendingProduct}
          isDeleting={isDeleting}
          onConfirm={() => {
            onDelete(pendingProduct.id);
            setPendingDeleteId(null);
          }}
          onCancel={() => setPendingDeleteId(null)}
        />
      )}

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={selected?.name ?? ""}
        subtitle={selected ? `${selected.categoryName ?? "Без категорії"} · ${selected.unit}` : ""}
        width={560}
      >
        {selected && <ProductDetail p={selected} />}
      </DetailDrawer>
    </>
  );
}
