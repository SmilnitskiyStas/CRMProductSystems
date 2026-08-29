"use client";

// Floating order cart for a supplier's marketplace page (TASK-318).
// Available only with an active cooperation agreement. Cart contents are plain
// component state (UI state) — server state stays in React Query.

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ShoppingCart, Trash2, X, ImageOff } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useStores } from "@/features/stores/hooks/useStores";
import { useCreateMarketplaceOrder, useCheckOrderConflicts } from "../hooks/useCooperation";
import type { SupplierItemDto, BarcodeConflict, CreateMarketplaceOrderItem } from "../types";

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

/** Per-conflict resolution chosen by the user (TASK-597). */
interface ConflictResolution {
  catalogAction: "link" | "create_new";
  linkedItemId?: string;
}

export function SupplierOrderCart({ supplierId, cart, onUpdateQty, onRemove, onClear }: Props) {
  const t = useTranslations("Dashboard.marketplace.orderCart");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const [modalOpen, setModalOpen] = useState(false);
  const [comment, setComment] = useState("");
  const [storeId, setStoreId] = useState("");
  // Checkout is a two-step flow: "cart" (review + qty/store/comment, today's flow) → an
  // optional "conflicts" step only entered when the pre-flight check finds a barcode that
  // already belongs to a different existing catalog item (TASK-597).
  const [step, setStep] = useState<"cart" | "conflicts">("cart");
  const [conflicts, setConflicts] = useState<BarcodeConflict[]>([]);
  const [resolutions, setResolutions] = useState<Record<string, ConflictResolution>>({});
  const createOrder = useCreateMarketplaceOrder(supplierId);
  const checkConflicts = useCheckOrderConflicts(supplierId);
  const { data: stores, isLoading: storesLoading } = useStores();

  if (cart.length === 0) return null;

  const total = cart.reduce((sum, line) => sum + (line.item.price ?? 0) * line.qty, 0);
  const allConflictsResolved = conflicts.every((c) => resolutions[c.supplierItemId] != null);

  const cartColumns: TableColumn<CartLine>[] = [
    {
      key: "product",
      header: t("headerProduct"),
      render: (line) => (
        <>
          {line.item.customName ?? line.item.itemName ?? "—"}
          {line.item.unit && (
            <span style={{ color: "#4B5563", fontSize: 11 }}> · {line.item.unit}</span>
          )}
        </>
      ),
    },
    {
      key: "price",
      header: t("headerPrice"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (line) => (line.item.price != null ? money(line.item.price, intlLocale) : "—"),
    },
    {
      key: "qty",
      header: t("headerQty"),
      render: (line) => (
        <input
          type="number"
          value={line.qty}
          min={line.item.minQty ?? 1}
          max={line.item.maxQty ?? undefined}
          onChange={(e) =>
            onUpdateQty(line.item.id, clampQty(line.item, Number(e.target.value) || 1))
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
      ),
    },
    {
      key: "total",
      header: t("headerTotal"),
      cellStyle: { whiteSpace: "nowrap" },
      render: (line) => (line.item.price != null ? money(line.item.price * line.qty, intlLocale) : "—"),
    },
    {
      key: "remove",
      header: "",
      render: (line) => (
        <button
          onClick={() => onRemove(line.item.id)}
          title={t("removeFromCartTitle")}
          style={{ background: "none", border: "none", cursor: "pointer", color: "#F87171", padding: 2, display: "inline-flex" }}
        >
          <Trash2 size={15} />
        </button>
      ),
    },
  ];

  function closeModal() {
    setModalOpen(false);
    setStep("cart");
    setConflicts([]);
    setResolutions({});
  }

  function backToCart() {
    setStep("cart");
    setConflicts([]);
    setResolutions({});
  }

  function submitOrder() {
    const items: CreateMarketplaceOrderItem[] = cart.map((line) => {
      const resolution = resolutions[line.item.id];
      return resolution
        ? {
            supplierItemId: line.item.id,
            qty: line.qty,
            catalogAction: resolution.catalogAction,
            linkedItemId: resolution.linkedItemId,
          }
        : { supplierItemId: line.item.id, qty: line.qty };
    });
    createOrder.mutate(
      { items, comment: comment.trim() || undefined, destinationStoreId: storeId },
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
          closeModal();
          onClear();
        },
        // 403 — гейт «тільки active agreement», 400 — валідація позицій/кількостей/магазину
        // (в т.ч. нерозвʼязаний конфлікт штрихкоду — не мало б статись, бо перевіряємо заздалегідь)
        onError: (err) => toast.error(err.message),
      }
    );
  }

  function handleSubmit() {
    if (!storeId) return;
    checkConflicts.mutate(
      cart.map((line) => ({ supplierItemId: line.item.id, qty: line.qty })),
      {
        onSuccess: (found) => {
          if (found.length === 0) {
            submitOrder();
          } else {
            setConflicts(found);
            setResolutions({});
            setStep("conflicts");
          }
        },
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
          onClick={closeModal}
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
                {step === "cart" ? t("modalTitle") : t("conflictStepTitle")}
              </span>
              <button
                onClick={closeModal}
                style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
              >
                <X size={18} />
              </button>
            </div>

            <div style={{ flex: 1, overflowY: "auto", padding: 18 }}>
              {step === "conflicts" ? (
              <div>
                <div style={{ color: "#9CA3AF", fontSize: 13, marginBottom: 16 }}>
                  {t("conflictIntro")}
                </div>
                {conflicts.map((conflict) => {
                  const line = cart.find((l) => l.item.id === conflict.supplierItemId);
                  const chosen = resolutions[conflict.supplierItemId];
                  return (
                    <div
                      key={conflict.supplierItemId}
                      style={{
                        border: "1px solid #1F2937",
                        borderRadius: 10,
                        padding: 14,
                        marginBottom: 12,
                      }}
                    >
                      <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 600, marginBottom: 4 }}>
                        {t("conflictOrderedLabel")}
                      </div>
                      <div style={{ color: "#E8EDF5", fontSize: 13, marginBottom: 12 }}>
                        {line?.item.customName ?? line?.item.itemName ?? "—"}
                      </div>

                      <div style={{ color: "#4B5563", fontSize: 12, fontWeight: 600, marginBottom: 6 }}>
                        {t("conflictExistingLabel")}
                      </div>
                      <div style={{ display: "flex", gap: 12, marginBottom: 12 }}>
                        <div
                          style={{
                            width: 56,
                            height: 56,
                            flexShrink: 0,
                            background: "#111827",
                            border: "1px solid #1F2937",
                            borderRadius: 8,
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "center",
                            overflow: "hidden",
                          }}
                        >
                          {conflict.existingItem.imageUrl ? (
                            // eslint-disable-next-line @next/next/no-img-element
                            <img
                              src={conflict.existingItem.imageUrl}
                              alt={conflict.existingItem.name}
                              style={{ width: "100%", height: "100%", objectFit: "cover" }}
                            />
                          ) : (
                            <ImageOff size={18} color="#4B5563" />
                          )}
                        </div>
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 4 }}>
                            {conflict.existingItem.name}
                          </div>
                          <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>
                            {conflict.existingItem.barcodes.length > 0
                              ? conflict.existingItem.barcodes.join(", ")
                              : t("conflictNoBarcodes")}
                          </div>
                        </div>
                      </div>

                      <div style={{ display: "flex", gap: 8 }}>
                        <button
                          onClick={() =>
                            setResolutions((r) => ({
                              ...r,
                              [conflict.supplierItemId]: {
                                catalogAction: "link",
                                linkedItemId: conflict.existingItem.id,
                              },
                            }))
                          }
                          style={{
                            flex: 1,
                            padding: "8px 10px",
                            borderRadius: 8,
                            fontSize: 12,
                            fontWeight: 600,
                            cursor: "pointer",
                            border: chosen?.catalogAction === "link" ? "1px solid #22C55E" : "1px solid #374151",
                            background: chosen?.catalogAction === "link" ? "#052e16" : "#1F2937",
                            color: chosen?.catalogAction === "link" ? "#4ADE80" : "#9CA3AF",
                          }}
                        >
                          {t("conflictLinkAction")}
                        </button>
                        <button
                          onClick={() =>
                            setResolutions((r) => ({
                              ...r,
                              [conflict.supplierItemId]: { catalogAction: "create_new" },
                            }))
                          }
                          style={{
                            flex: 1,
                            padding: "8px 10px",
                            borderRadius: 8,
                            fontSize: 12,
                            fontWeight: 600,
                            cursor: "pointer",
                            border: chosen?.catalogAction === "create_new" ? "1px solid #3B82F6" : "1px solid #374151",
                            background: chosen?.catalogAction === "create_new" ? "#0c1e3d" : "#1F2937",
                            color: chosen?.catalogAction === "create_new" ? "#60A5FA" : "#9CA3AF",
                          }}
                        >
                          {t("conflictCreateNewAction")}
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
              ) : (
              <>
              <Table columns={cartColumns} rows={cart} rowKey={(line) => line.item.id} />

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
              </>
              )}
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
                {step === "cart" ? (
                  <>
                    <Btn variant="ghost" onClick={closeModal}>
                      {t("close")}
                    </Btn>
                    <Btn
                      variant="success"
                      disabled={createOrder.isPending || checkConflicts.isPending || !storeId}
                      onClick={handleSubmit}
                    >
                      {checkConflicts.isPending
                        ? t("checkingConflicts")
                        : createOrder.isPending
                          ? t("submitting")
                          : t("confirmOrder")}
                    </Btn>
                  </>
                ) : (
                  <>
                    <Btn variant="ghost" onClick={backToCart}>
                      {t("back")}
                    </Btn>
                    <Btn
                      variant="success"
                      disabled={!allConflictsResolved || createOrder.isPending}
                      onClick={submitOrder}
                    >
                      {createOrder.isPending ? t("submitting") : t("confirmOrder")}
                    </Btn>
                  </>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
