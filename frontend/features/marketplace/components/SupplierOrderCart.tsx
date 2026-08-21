"use client";

// Floating order cart for a supplier's marketplace page (TASK-318).
// Available only with an active cooperation agreement. Cart contents are plain
// component state (UI state) — server state stays in React Query.

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ShoppingCart, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useStores } from "@/features/stores/hooks/useStores";
import { useCreateMarketplaceOrder } from "../hooks/useCooperation";
import type { SupplierItemDto } from "../types";

export interface CartLine {
  item: SupplierItemDto;
  qty: number;
}

interface Props {
  supplierId: string;
  cart: CartLine[];
  onUpdateQty: (supplierItemId: string, qty: number) => void;
  onRemove: (supplierItemId: string) => void;
  onClear: () => void;
}

function money(v: number, locale: string): string {
  return v.toLocaleString(locale, {
    style: "currency",
    currency: "UAH",
    minimumFractionDigits: 2,
  });
}

function clampQty(item: SupplierItemDto, qty: number): number {
  let q = Math.max(1, Math.round(qty));
  if (item.minQty != null && q < item.minQty) q = item.minQty;
  if (item.maxQty != null && q > item.maxQty) q = item.maxQty;
  return q;
}

export function SupplierOrderCart({ supplierId, cart, onUpdateQty, onRemove, onClear }: Props) {
  const t = useTranslations("Dashboard.marketplace.orderCart");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const [modalOpen, setModalOpen] = useState(false);
  const [comment, setComment] = useState("");
  const [storeId, setStoreId] = useState("");
  const createOrder = useCreateMarketplaceOrder(supplierId);
  const { data: stores, isLoading: storesLoading } = useStores();

  if (cart.length === 0) return null;

  const total = cart.reduce((sum, line) => sum + (line.item.price ?? 0) * line.qty, 0);

  function handleSubmit() {
    if (!storeId) return;
    createOrder.mutate(
      {
        items: cart.map((line) => ({ supplierItemId: line.item.id, qty: line.qty })),
        comment: comment.trim() || undefined,
        destinationStoreId: storeId,
      },
      {
        onSuccess: (order) => {
          toast.success(t("toastCreated", { number: order.orderNumber }), {
            action: {
              label: t("toastMyOrdersAction"),
              onClick: () => router.push("/marketplace/orders"),
            },
          });
          setComment("");
          setStoreId("");
          setModalOpen(false);
          onClear();
        },
        // 403 — гейт «тільки active agreement», 400 — валідація позицій/кількостей/магазину
        onError: (err) => toast.error(err.message),
      }
    );
  }

  return (
    <>
      {/* Floating summary */}
      <div
        style={{
          position: "fixed",
          right: 28,
          bottom: 28,
          zIndex: 400,
          background: "#111827",
          border: "1px solid #3B82F6",
          borderRadius: 12,
          padding: "12px 16px",
          display: "flex",
          alignItems: "center",
          gap: 14,
          boxShadow: "0 8px 24px rgba(0,0,0,0.45)",
        }}
      >
        <ShoppingCart size={18} color="#60A5FA" />
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
            {t("cartSummary", { count: cart.length })}
          </div>
          <div style={{ color: "#9CA3AF", fontSize: 12 }}>{money(total, intlLocale)}</div>
        </div>
        <Btn size="sm" onClick={() => setModalOpen(true)}>
          {t("checkout")}
        </Btn>
        <button
          onClick={onClear}
          title={t("clearCartTitle")}
          style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4, display: "flex" }}
        >
          <X size={16} />
        </button>
      </div>

      {/* Order modal */}
      {modalOpen && (
        <div
          style={{
            position: "fixed",
            inset: 0,
            background: "rgba(0,0,0,0.6)",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            zIndex: 999,
            padding: 20,
          }}
          onClick={() => setModalOpen(false)}
        >
          <div
            style={{
              background: "#0F1623",
              border: "1px solid #1F2937",
              borderRadius: 12,
              width: "100%",
              maxWidth: 640,
              maxHeight: "88vh",
              display: "flex",
              flexDirection: "column",
              overflow: "hidden",
            }}
            onClick={(e) => e.stopPropagation()}
          >
            <div
              style={{
                padding: "14px 18px",
                borderBottom: "1px solid #1F2937",
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
              }}
            >
              <span style={{ color: "#E8EDF5", fontWeight: 700, fontSize: 15 }}>
                {t("modalTitle")}
              </span>
              <button
                onClick={() => setModalOpen(false)}
                style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
              >
                <X size={18} />
              </button>
            </div>

            <div style={{ flex: 1, overflowY: "auto", padding: 18 }}>
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    {[t("headerProduct"), t("headerPrice"), t("headerQty"), t("headerTotal"), ""].map((h, i) => (
                      <th
                        key={i}
                        style={{
                          padding: "8px 10px",
                          color: "#4B5563",
                          fontSize: 11,
                          fontWeight: 600,
                          textTransform: "uppercase",
                          letterSpacing: "0.05em",
                          textAlign: i === 0 ? "left" : "right",
                          borderBottom: "1px solid #1F2937",
                        }}
                      >
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {cart.map((line) => (
                    <tr key={line.item.id}>
                      <td style={{ padding: "10px", color: "#E8EDF5", fontSize: 13, borderBottom: "1px solid #1A2235" }}>
                        {line.item.customName ?? line.item.itemName ?? "—"}
                        {line.item.unit && (
                          <span style={{ color: "#4B5563", fontSize: 11 }}> · {line.item.unit}</span>
                        )}
                      </td>
                      <td style={{ padding: "10px", color: "#9CA3AF", fontSize: 13, textAlign: "right", borderBottom: "1px solid #1A2235", whiteSpace: "nowrap" }}>
                        {line.item.price != null ? money(line.item.price, intlLocale) : "—"}
                      </td>
                      <td style={{ padding: "10px", textAlign: "right", borderBottom: "1px solid #1A2235" }}>
                        <input
                          type="number"
                          value={line.qty}
                          min={line.item.minQty ?? 1}
                          max={line.item.maxQty ?? undefined}
                          onChange={(e) =>
                            onUpdateQty(
                              line.item.id,
                              clampQty(line.item, Number(e.target.value) || 1)
                            )
                          }
                          style={{
                            width: 70,
                            background: "#1F2937",
                            border: "1px solid #374151",
                            borderRadius: 6,
                            color: "#E8EDF5",
                            fontSize: 13,
                            padding: "5px 8px",
                            outline: "none",
                            textAlign: "right",
                          }}
                        />
                      </td>
                      <td style={{ padding: "10px", color: "#E8EDF5", fontSize: 13, textAlign: "right", borderBottom: "1px solid #1A2235", whiteSpace: "nowrap" }}>
                        {line.item.price != null ? money(line.item.price * line.qty, intlLocale) : "—"}
                      </td>
                      <td style={{ padding: "10px", textAlign: "right", borderBottom: "1px solid #1A2235" }}>
                        <button
                          onClick={() => onRemove(line.item.id)}
                          title={t("removeFromCartTitle")}
                          style={{ background: "none", border: "none", cursor: "pointer", color: "#F87171", padding: 2, display: "inline-flex" }}
                        >
                          <Trash2 size={15} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <div style={{ marginTop: 16 }}>
                <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 6 }}>
                  {t("destinationStoreLabel")}
                </label>
                {storesLoading ? (
                  <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loadingStores")}</div>
                ) : (
                  <select
                    value={storeId}
                    onChange={(e) => setStoreId(e.target.value)}
                    required
                    style={{
                      width: "100%",
                      boxSizing: "border-box",
                      background: "#1F2937",
                      border: "1px solid #374151",
                      borderRadius: 8,
                      color: "#E8EDF5",
                      fontSize: 13,
                      padding: "9px 12px",
                      outline: "none",
                      appearance: "none",
                      cursor: "pointer",
                    }}
                  >
                    <option value="">{t("destinationStorePlaceholder")}</option>
                    {stores?.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                        {s.address ? ` — ${s.address}` : ""}
                      </option>
                    ))}
                  </select>
                )}
                {!storeId && !storesLoading && (
                  <div style={{ color: "#FBBF24", fontSize: 11, marginTop: 6 }}>
                    {t("destinationStoreRequiredError")}
                  </div>
                )}
              </div>

              <div style={{ marginTop: 16 }}>
                <label style={{ display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 6 }}>
                  {t("commentLabel")}
                </label>
                <textarea
                  value={comment}
                  onChange={(e) => setComment(e.target.value)}
                  placeholder={t("commentPlaceholder")}
                  rows={3}
                  style={{
                    width: "100%",
                    boxSizing: "border-box",
                    background: "#1F2937",
                    border: "1px solid #374151",
                    borderRadius: 8,
                    color: "#E8EDF5",
                    fontSize: 13,
                    padding: "9px 12px",
                    outline: "none",
                    resize: "vertical",
                    fontFamily: "inherit",
                  }}
                />
              </div>
            </div>

            <div
              style={{
                padding: "14px 18px",
                borderTop: "1px solid #1F2937",
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
              }}
            >
              <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700 }}>
                {t("totalLabel", { total: money(total, intlLocale) })}
              </span>
              <div style={{ display: "flex", gap: 10 }}>
                <Btn variant="ghost" onClick={() => setModalOpen(false)}>
                  {t("close")}
                </Btn>
                <Btn
                  variant="success"
                  disabled={createOrder.isPending || !storeId}
                  onClick={handleSubmit}
                >
                  {createOrder.isPending ? t("submitting") : t("confirmOrder")}
                </Btn>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
