"use client";

import { useState, useEffect } from "react";
import { useRouter } from "next/navigation";
import { X, AlertTriangle, Trash2, ShoppingCart, CheckCircle, ChevronRight, ChevronRight as Arrow, Loader2, ExternalLink, Package } from "lucide-react";
import { useCreateWriteOff } from "@/features/write-offs/hooks/useWriteOffs";
import { useVerifyStock, useStockById } from "@/features/shelf/hooks/useStock";
import { useGenerateOrder } from "@/features/orders/hooks/useOrders";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useStores } from "@/features/stores/hooks/useStores";
import { DetailDrawer, DrawerField, DrawerSection, DrawerGrid } from "@/components/ui/DetailDrawer";
import type { AttentionItem } from "../types";

interface Props {
  items: AttentionItem[] | undefined;
  isLoading: boolean;
}

// ── Modal shell ───────────────────────────────────────────────────────────────

function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 2000,
        display: "flex", alignItems: "center", justifyContent: "center",
        background: "rgba(0,0,0,0.7)",
      }}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 14,
          width: 480,
          maxWidth: "calc(100vw - 32px)",
          maxHeight: "80vh",
          display: "flex",
          flexDirection: "column",
          boxShadow: "0 24px 64px rgba(0,0,0,0.7)",
        }}
      >
        <div style={{
          display: "flex", alignItems: "center", justifyContent: "space-between",
          padding: "16px 20px", borderBottom: "1px solid #1F2937", flexShrink: 0,
        }}>
          <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{title}</span>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4, borderRadius: 6 }}
          >
            <X size={16} />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

// ── 1. Critical items modal ───────────────────────────────────────────────────

function CriticalModal({ items, onClose }: { items: AttentionItem[]; onClose: () => void }) {
  const router = useRouter();
  const STATUS_LABEL: Record<string, string> = {
    expired: "Прострочено",
    critical: "Критично",
    warning: "Попередження",
  };
  const STATUS_COLOR: Record<string, string> = {
    expired: "#EF4444",
    critical: "#F97316",
    warning: "#F59E0B",
  };

  return (
    <Modal title="Критичні товари" onClose={onClose}>
      <div style={{ flex: 1, overflowY: "auto", padding: "16px 20px" }}>
        {items.length === 0 ? (
          <div style={{ textAlign: "center", padding: "32px 0", color: "#4ADE80", fontSize: 14 }}>
            Критичних товарів немає — все в нормі ✓
          </div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {items.map((item) => {
              const color = STATUS_COLOR[item.status] ?? "#6B7280";
              return (
                <div
                  key={item.id}
                  style={{
                    background: "#0D1117",
                    border: `1px solid ${color}25`,
                    borderRadius: 10,
                    padding: "10px 14px",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    gap: 12,
                  }}
                >
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, marginBottom: 2 }}>
                      {item.name}
                    </div>
                    <div style={{ color: "#6B7280", fontSize: 11 }}>
                      {item.zone !== "—" ? item.zone : "Зона не вказана"} · {item.sku}
                    </div>
                  </div>
                  <div style={{ display: "flex", alignItems: "center", gap: 10, flexShrink: 0 }}>
                    <span
                      style={{
                        fontSize: 10, padding: "2px 8px", borderRadius: 99,
                        background: `${color}15`, color, border: `1px solid ${color}30`,
                      }}
                    >
                      {STATUS_LABEL[item.status] ?? item.status}
                    </span>
                    <span style={{ color, fontSize: 15, fontWeight: 700, fontFamily: "monospace", minWidth: 28, textAlign: "right" }}>
                      {item.quantity === 0 ? "OUT" : item.quantity}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
      <div style={{ borderTop: "1px solid #1F2937", padding: "12px 20px", flexShrink: 0, display: "flex", gap: 8 }}>
        <button
          onClick={() => { router.push("/stock"); onClose(); }}
          style={{
            flex: 1, display: "flex", alignItems: "center", justifyContent: "center", gap: 6,
            background: "#1D3461", border: "1px solid #3B82F6", borderRadius: 8,
            padding: "9px 0", color: "#93C5FD", fontSize: 13, fontWeight: 600, cursor: "pointer",
          }}
        >
          Відкрити Залишки
          <ChevronRight size={14} />
        </button>
      </div>
    </Modal>
  );
}

// ── 2. Write-off modal ────────────────────────────────────────────────────────

function WriteOffDrawer({ isOpen, items, storeId, onClose }: {
  isOpen: boolean;
  items: AttentionItem[];
  storeId: string;
  onClose: () => void;
}) {
  const router = useRouter();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [done, setDone] = useState(false);
  const createWriteOff = useCreateWriteOff();

  useEffect(() => {
    if (isOpen) {
      setSelected(new Set(items.map((i) => i.id)));
      setDone(false);
      createWriteOff.reset();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  async function handleSubmit() {
    const chosenItems = items.filter((i) => selected.has(i.id));
    if (chosenItems.length === 0) return;
    try {
      await createWriteOff.mutateAsync({
        storeId,
        reason: "expired",
        notes: "Автоматичне списання прострочених товарів з дашборду",
        items: chosenItems.map((i) => ({
          productStockId: i.id,
          productId: i.productId,
          quantity: i.quantity > 0 ? i.quantity : 1,
        })),
      });
      setDone(true);
    } catch {
      // помилка відображається через createWriteOff.isError
    }
  }

  const selectedCount = selected.size;

  return (
    <DetailDrawer
      isOpen={isOpen}
      onClose={onClose}
      title={done ? "Списання створено" : "Списати прострочені"}
      subtitle={done ? "Чернетка очікує затвердження" : `${items.length} товарів для списання`}
      width={480}
    >
      {done ? (
        <div style={{ textAlign: "center", padding: "32px 0" }}>
          <div style={{
            width: 56, height: 56, borderRadius: "50%",
            background: "rgba(74,222,128,0.1)", border: "1px solid #166534",
            display: "flex", alignItems: "center", justifyContent: "center",
            margin: "0 auto 16px",
          }}>
            <CheckCircle size={26} color="#4ADE80" />
          </div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, marginBottom: 8 }}>
            Чернетку успішно створено
          </div>
          <div style={{ color: "#6B7280", fontSize: 13, marginBottom: 24 }}>
            Перейдіть до сторінки Списання для затвердження документа.
          </div>
          <button
            onClick={() => { router.push("/write-offs"); onClose(); }}
            style={{
              display: "inline-flex", alignItems: "center", gap: 6,
              background: "#1D3461", border: "1px solid #3B82F6", borderRadius: 8,
              padding: "9px 20px", color: "#93C5FD", fontSize: 13, fontWeight: 600, cursor: "pointer",
            }}
          >
            Перейти до Списань <ChevronRight size={14} />
          </button>
        </div>
      ) : items.length === 0 ? (
        <div style={{ textAlign: "center", padding: "32px 0", color: "#4ADE80", fontSize: 14 }}>
          Прострочених товарів немає ✓
        </div>
      ) : (
        <>
          <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 16px", lineHeight: 1.6 }}>
            Виберіть товари для включення до чернетки списання. Буде створено документ зі статусом «Чернетка».
          </p>
          <div style={{ display: "flex", flexDirection: "column", gap: 6, marginBottom: 20 }}>
            {items.map((item) => {
              const checked = selected.has(item.id);
              return (
                <label key={item.id} style={{
                  display: "flex", alignItems: "center", gap: 12,
                  background: checked ? "#1a0a0a" : "#0D1117",
                  border: `1px solid ${checked ? "#991b1b40" : "#1F2937"}`,
                  borderRadius: 10, padding: "10px 14px", cursor: "pointer",
                  transition: "border-color 0.15s, background 0.15s",
                }}>
                  <input
                    type="checkbox" checked={checked} onChange={() => toggle(item.id)}
                    style={{ width: 15, height: 15, accentColor: "#EF4444", flexShrink: 0 }}
                  />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{item.name}</div>
                    <div style={{ color: "#6B7280", fontSize: 11 }}>
                      {item.zone !== "—" ? item.zone : ""} · к-сть: {item.quantity}
                    </div>
                  </div>
                  <span style={{
                    fontSize: 10, padding: "2px 7px", borderRadius: 99, flexShrink: 0,
                    background: "#EF444415", color: "#EF4444", border: "1px solid #EF444430",
                  }}>
                    Прострочено
                  </span>
                </label>
              );
            })}
          </div>

          {createWriteOff.isError && (
            <div style={{ color: "#EF4444", fontSize: 12, marginBottom: 12 }}>
              Помилка: {createWriteOff.error?.message ?? "Не вдалося створити списання"}
            </div>
          )}

          <button
            onClick={handleSubmit}
            disabled={selectedCount === 0 || createWriteOff.isPending}
            style={{
              width: "100%", display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
              background: selectedCount > 0 ? "#1a0a0a" : "#111827",
              border: `1px solid ${selectedCount > 0 ? "#EF4444" : "#1F2937"}`,
              borderRadius: 8, padding: "10px 0",
              color: selectedCount > 0 ? "#EF4444" : "#374151",
              fontSize: 13, fontWeight: 600,
              cursor: selectedCount > 0 && !createWriteOff.isPending ? "pointer" : "not-allowed",
            }}
          >
            {createWriteOff.isPending
              ? <><Loader2 size={14} style={{ animation: "spin 1s linear infinite" }} /> Створення…</>
              : <><Trash2 size={14} /> Створити чернетку ({selectedCount})</>
            }
          </button>
          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
        </>
      )}
    </DetailDrawer>
  );
}

// ── 3. Order drawer ───────────────────────────────────────────────────────────

type OrderState = "idle" | "loading" | "done" | "error";

function OrderDrawer({ isOpen, storeId, onClose }: { isOpen: boolean; storeId: string; onClose: () => void }) {
  const router = useRouter();
  const [state, setState] = useState<OrderState>("idle");
  const [result, setResult] = useState<{ linesToOrder: number; productsEvaluated: number; buffersCalculated: number } | null>(null);
  const [errorMsg, setErrorMsg] = useState("");
  const generateOrder = useGenerateOrder();

  useEffect(() => {
    if (!isOpen) {
      setState("idle");
      setResult(null);
      setErrorMsg("");
    }
  }, [isOpen]);

  async function handleGenerate() {
    setState("loading");
    try {
      const res = await generateOrder.mutateAsync(storeId);
      setResult({
        linesToOrder: res.order.linesToOrder,
        productsEvaluated: res.order.productsEvaluated,
        buffersCalculated: res.buffers.buffersCalculated,
      });
      setState("done");
    } catch (e: unknown) {
      setErrorMsg(e instanceof Error ? e.message : "Невідома помилка");
      setState("error");
    }
  }

  const subtitle = state === "loading" ? "Виконується розрахунок…"
    : state === "done" ? "Замовлення розраховано"
    : "TOC / DDMRP методологія";

  return (
    <DetailDrawer
      isOpen={isOpen}
      onClose={onClose}
      title="Сформувати замовлення"
      subtitle={subtitle}
      width={480}
    >
      {state === "idle" && (
        <>
          <p style={{ color: "#9CA3AF", fontSize: 13, lineHeight: 1.7, margin: "0 0 20px" }}>
            Система перерахує ADU, оновить буфери та розрахує оптимальне замовлення за методологією TOC/DDMRP.
          </p>
          <DrawerSection title="Кроки розрахунку">
            {[
              "Перерахунок середнього щоденного споживання (ADU)",
              "Оновлення буферів (зелений / жовтий / червоний)",
              "Розрахунок позицій замовлення",
            ].map((step, i) => (
              <div key={i} style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10 }}>
                <div style={{
                  width: 22, height: 22, borderRadius: "50%", background: "#1D3461",
                  border: "1px solid #3B82F640", display: "flex", alignItems: "center",
                  justifyContent: "center", color: "#60A5FA", fontSize: 11, fontWeight: 700, flexShrink: 0,
                }}>
                  {i + 1}
                </div>
                <span style={{ color: "#9CA3AF", fontSize: 13 }}>{step}</span>
              </div>
            ))}
          </DrawerSection>
          <button
            onClick={handleGenerate}
            style={{
              width: "100%", display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
              background: "#1D3461", border: "1px solid #3B82F6", borderRadius: 8,
              padding: "10px 0", color: "#93C5FD", fontSize: 13, fontWeight: 600, cursor: "pointer", marginTop: 8,
            }}
          >
            <ShoppingCart size={14} /> Розрахувати замовлення
          </button>
        </>
      )}

      {state === "loading" && (
        <div style={{ textAlign: "center", padding: "48px 0" }}>
          <Loader2 size={36} color="#3B82F6"
            style={{ animation: "spin 1s linear infinite", margin: "0 auto 16px", display: "block" }} />
          <div style={{ color: "#9CA3AF", fontSize: 13 }}>Виконується розрахунок…</div>
          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
        </div>
      )}

      {state === "done" && result && (
        <div style={{ textAlign: "center" }}>
          <div style={{
            width: 56, height: 56, borderRadius: "50%",
            background: "rgba(59,130,246,0.1)", border: "1px solid #1D3461",
            display: "flex", alignItems: "center", justifyContent: "center",
            margin: "0 auto 20px",
          }}>
            <CheckCircle size={26} color="#3B82F6" />
          </div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, marginBottom: 20 }}>
            Замовлення розраховано
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 10, marginBottom: 24 }}>
            {[
              { label: "Оцінено товарів", value: result.productsEvaluated },
              { label: "Оновлено буферів", value: result.buffersCalculated },
              { label: "Позицій до замовлення", value: result.linesToOrder, accent: "#3B82F6" },
            ].map(({ label, value, accent }) => (
              <div key={label} style={{
                background: "#0D1117", border: "1px solid #1F2937",
                borderRadius: 10, padding: "12px 8px", textAlign: "center",
              }}>
                <div style={{ color: accent ?? "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
                  {value}
                </div>
                <div style={{ color: "#6B7280", fontSize: 10, marginTop: 4 }}>{label}</div>
              </div>
            ))}
          </div>
          <button
            onClick={() => { router.push("/orders"); onClose(); }}
            style={{
              display: "inline-flex", alignItems: "center", gap: 6,
              background: "#1D3461", border: "1px solid #3B82F6", borderRadius: 8,
              padding: "9px 20px", color: "#93C5FD", fontSize: 13, fontWeight: 600, cursor: "pointer",
            }}
          >
            Переглянути замовлення <ChevronRight size={14} />
          </button>
        </div>
      )}

      {state === "error" && (
        <div style={{ textAlign: "center", padding: "24px 0" }}>
          <div style={{ color: "#EF4444", fontSize: 14, marginBottom: 8 }}>Помилка розрахунку</div>
          <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 20 }}>{errorMsg}</div>
          <button
            onClick={() => setState("idle")}
            style={{
              background: "transparent", border: "1px solid #374151", borderRadius: 8,
              padding: "8px 16px", color: "#9CA3AF", fontSize: 13, cursor: "pointer",
            }}
          >
            Спробувати ще раз
          </button>
        </div>
      )}
    </DetailDrawer>
  );
}

// ── 4. Item detail drawer ─────────────────────────────────────────────────────

const STATUS_LABEL: Record<string, string> = {
  expired: "Прострочено",
  critical: "Критично",
  warning: "Попередження",
  safe: "Норма",
};
const STATUS_COLOR: Record<string, string> = {
  expired: "#EF4444",
  critical: "#F97316",
  warning: "#F59E0B",
  safe: "#22C55E",
};

function ItemDetailDrawer({
  item, storeId, onClose, onWriteOff,
}: {
  item: AttentionItem | null;
  storeId: string;
  onClose: () => void;
  onWriteOff: (items: AttentionItem[]) => void;
}) {
  const router = useRouter();
  const { data: batch, isLoading } = useStockById(item?.id ?? null);
  const verifyStock = useVerifyStock();
  const [verified, setVerified] = useState(false);

  const color = STATUS_COLOR[item?.status ?? ""] ?? "#6B7280";
  const label = STATUS_LABEL[item?.status ?? ""] ?? (item?.status ?? "");

  const expiryStr = batch
    ? new Date(batch.expiryDate as unknown as string).toLocaleDateString("uk-UA", { day: "2-digit", month: "2-digit", year: "numeric" })
    : "—";
  const addedStr = batch
    ? new Date(batch.addedAt as unknown as string).toLocaleDateString("uk-UA", { day: "2-digit", month: "2-digit", year: "numeric" })
    : "—";
  const daysLeftStr = batch
    ? (batch.daysLeft <= 0 ? "Прострочено" : `${batch.daysLeft} дн.`)
    : "—";

  async function handleVerify() {
    if (!item) return;
    await verifyStock.mutateAsync(item.id);
    setVerified(true);
  }

  return (
    <DetailDrawer
      isOpen={!!item}
      onClose={onClose}
      title={item?.name ?? ""}
      subtitle={label}
      width={460}
    >
      {isLoading ? (
        <div style={{ display: "flex", alignItems: "center", justifyContent: "center", padding: 40 }}>
          <Loader2 size={28} color="#374151" style={{ animation: "spin 1s linear infinite" }} />
          <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
        </div>
      ) : (
        <>
          {/* Status badge */}
          <div style={{ marginBottom: 20 }}>
            <div style={{
              display: "inline-flex", alignItems: "center", gap: 8,
              background: `${color}10`, border: `1px solid ${color}30`,
              borderRadius: 8, padding: "8px 14px",
            }}>
              <Package size={15} color={color} />
              <span style={{ color, fontSize: 13, fontWeight: 600 }}>{label}</span>
            </div>
          </div>

          <DrawerSection title="Залишки">
            <DrawerGrid>
              <DrawerField
                label="Поточна кількість"
                value={item?.quantity === 0 ? "OUT" : String(item?.quantity ?? "—")}
                color={item?.quantity === 0 ? "#EF4444" : undefined}
              />
              <DrawerField
                label="Початкова кількість"
                value={String(batch?.quantityInitial ?? "—")}
              />
            </DrawerGrid>
          </DrawerSection>

          <DrawerSection title="Термін придатності">
            <DrawerGrid>
              <DrawerField
                label="Термін до"
                value={expiryStr}
                color={color}
              />
              <DrawerField
                label="Залишилось"
                value={daysLeftStr}
                color={color}
              />
            </DrawerGrid>
          </DrawerSection>

          <DrawerSection title="Розташування та партія">
            <DrawerGrid>
              <DrawerField label="Зона" value={batch?.zoneName ?? item?.zone ?? "—"} />
              <DrawerField
                label="Полиця №"
                value={batch?.shelfNumber != null ? String(batch.shelfNumber) : "—"}
              />
              <DrawerField label="Партія №" value={batch?.batchNumber ?? "—"} />
              <DrawerField label="Штрихкод" value={batch?.productBarcode ?? item?.sku ?? "—"} />
            </DrawerGrid>
            <DrawerField label="Дата надходження" value={addedStr} />
          </DrawerSection>

          {verified && (
            <div style={{
              display: "flex", alignItems: "center", gap: 8,
              background: "rgba(74,222,128,0.08)", border: "1px solid #166534",
              borderRadius: 8, padding: "10px 14px", marginBottom: 16,
            }}>
              <CheckCircle size={15} color="#4ADE80" />
              <span style={{ color: "#4ADE80", fontSize: 13 }}>Верифіковано успішно</span>
            </div>
          )}

          {/* Actions */}
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {!verified && (
              <button
                onClick={handleVerify}
                disabled={verifyStock.isPending}
                style={{
                  width: "100%", display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                  background: "rgba(74,222,128,0.08)", border: "1px solid #166534",
                  borderRadius: 8, padding: "10px 0",
                  color: "#4ADE80", fontSize: 13, fontWeight: 600,
                  cursor: verifyStock.isPending ? "not-allowed" : "pointer",
                  opacity: verifyStock.isPending ? 0.6 : 1,
                }}
              >
                {verifyStock.isPending
                  ? <><Loader2 size={14} style={{ animation: "spin 1s linear infinite" }} /> Верифікація…</>
                  : <><CheckCircle size={14} /> Верифікувати партію</>}
              </button>
            )}

            {item?.status === "expired" && storeId && (
              <button
                onClick={() => { if (item) onWriteOff([item]); onClose(); }}
                style={{
                  width: "100%", display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                  background: "rgba(239,68,68,0.08)", border: "1px solid #991B1B",
                  borderRadius: 8, padding: "10px 0",
                  color: "#EF4444", fontSize: 13, fontWeight: 600, cursor: "pointer",
                }}
              >
                <Trash2 size={14} /> Створити списання
              </button>
            )}

            <button
              onClick={() => { router.push(`/stock?status=${item?.status}`); onClose(); }}
              style={{
                width: "100%", display: "flex", alignItems: "center", justifyContent: "center", gap: 6,
                background: "transparent", border: "1px solid #1F2937",
                borderRadius: 8, padding: "10px 0",
                color: "#6B7280", fontSize: 13, cursor: "pointer",
              }}
            >
              Переглянути всі залишки
              <ExternalLink size={13} />
            </button>
          </div>
        </>
      )}
    </DetailDrawer>
  );
}

// ── Main QuickActions ─────────────────────────────────────────────────────────

type ActiveModal = "critical" | "writeoff" | "order" | null;

export function QuickActions({ items = [], isLoading }: Props) {
  const { data: user } = useMe();
  const { data: stores = [] } = useStores();
  const [modal, setModal] = useState<ActiveModal>(null);
  const [writeOffItems, setWriteOffItems] = useState<AttentionItem[]>([]);
  const [selectedItem, setSelectedItem] = useState<AttentionItem | null>(null);

  const criticalItems = items.filter((i) => i.status === "critical" || i.status === "expired");
  const expiredItems = items.filter((i) => i.status === "expired");
  const topCritical = criticalItems.slice(0, 5);
  const storeId = user?.storeId ?? stores[0]?.id ?? "";

  function openWriteOff(forItems: AttentionItem[]) {
    setWriteOffItems(forItems);
    setModal("writeoff");
  }

  return (
    <>
      <div style={{
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 12,
        overflow: "hidden",
        display: "flex",
        flexDirection: "column",
      }}>
        <div style={{ padding: "16px 20px", borderBottom: "1px solid #1F2937" }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: 0 }}>Швидкі дії</h2>
        </div>

        <div style={{ padding: 16, flex: 1 }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 20 }}>
            <ActionButton
              label="Перевірити критичні"
              accent="#ef4444"
              badge={criticalItems.length > 0 ? criticalItems.length : undefined}
              icon={<AlertTriangle size={14} />}
              onClick={() => setModal("critical")}
            />
            <ActionButton
              label="Списати прострочені"
              accent="#6B7280"
              badge={expiredItems.length > 0 ? expiredItems.length : undefined}
              icon={<Trash2 size={14} />}
              onClick={() => openWriteOff(expiredItems)}
              disabled={!storeId}
            />
            <ActionButton
              label="Зробити замовлення"
              accent="#3B82F6"
              icon={<ShoppingCart size={14} />}
              onClick={() => setModal("order")}
              disabled={!storeId}
            />
          </div>

          <div style={{ borderTop: "1px solid #1F2937", paddingTop: 16 }}>
            <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.06em", marginBottom: 12 }}>
              Критичні товари
            </div>

            {isLoading ? (
              <div style={{ color: "#374151", fontSize: 13, textAlign: "center", padding: "16px 0" }}>
                Завантаження…
              </div>
            ) : topCritical.length === 0 ? (
              <div style={{
                background: "#0d2818", border: "1px solid #166534", borderRadius: 8,
                padding: "12px 14px", color: "#22c55e", fontSize: 13, textAlign: "center",
              }}>
                Критичних товарів немає
              </div>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {topCritical.map((item) => {
                  const color = STATUS_COLOR[item.status] ?? "#EF4444";
                  return (
                    <button
                      key={item.id}
                      onClick={() => setSelectedItem(item)}
                      style={{
                        width: "100%",
                        background: "#1a0a0a",
                        border: `1px solid ${color}25`,
                        borderRadius: 8,
                        padding: "10px 12px",
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        gap: 8,
                        cursor: "pointer",
                        textAlign: "left",
                        transition: "background 0.15s, border-color 0.15s",
                      }}
                      onMouseEnter={(e) => {
                        (e.currentTarget as HTMLElement).style.background = "#220d0d";
                        (e.currentTarget as HTMLElement).style.borderColor = `${color}50`;
                      }}
                      onMouseLeave={(e) => {
                        (e.currentTarget as HTMLElement).style.background = "#1a0a0a";
                        (e.currentTarget as HTMLElement).style.borderColor = `${color}25`;
                      }}
                    >
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, marginBottom: 2, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                          {item.name}
                        </div>
                        <div style={{ color: "#6B7280", fontSize: 11 }}>
                          {item.zone !== "—" ? item.zone : item.category}
                        </div>
                      </div>
                      <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
                        <span style={{ color, fontSize: 13, fontWeight: 700, fontFamily: "monospace" }}>
                          {item.quantity === 0 ? "OUT" : item.quantity}
                        </span>
                        <Arrow size={13} color="#374151" />
                      </div>
                    </button>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      </div>

      <ItemDetailDrawer
        item={selectedItem}
        storeId={storeId}
        onClose={() => setSelectedItem(null)}
        onWriteOff={openWriteOff}
      />
      {modal === "critical" && (
        <CriticalModal items={criticalItems} onClose={() => setModal(null)} />
      )}
      <WriteOffDrawer
        isOpen={modal === "writeoff"}
        items={writeOffItems}
        storeId={storeId}
        onClose={() => setModal(null)}
      />
      <OrderDrawer
        isOpen={modal === "order"}
        storeId={storeId}
        onClose={() => setModal(null)}
      />
    </>
  );
}

function ActionButton({
  label, accent, icon, badge, onClick, disabled,
}: {
  label: string;
  accent: string;
  icon?: React.ReactNode;
  badge?: number;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      style={{
        width: "100%", padding: "9px 14px",
        background: "transparent",
        border: `1px solid ${disabled ? "#1F2937" : `${accent}40`}`,
        borderRadius: 8,
        color: disabled ? "#374151" : accent,
        fontSize: 13, fontWeight: 500,
        cursor: disabled ? "not-allowed" : "pointer",
        textAlign: "left",
        display: "flex", alignItems: "center", gap: 8,
        transition: "background 0.15s",
        opacity: disabled ? 0.5 : 1,
      }}
      onMouseEnter={(e) => { if (!disabled) (e.currentTarget as HTMLElement).style.background = `${accent}10`; }}
      onMouseLeave={(e) => { if (!disabled) (e.currentTarget as HTMLElement).style.background = "transparent"; }}
    >
      {icon && <span style={{ flexShrink: 0 }}>{icon}</span>}
      <span style={{ flex: 1 }}>{label}</span>
      {badge !== undefined && (
        <span style={{
          background: `${accent}20`, color: accent,
          borderRadius: 99, padding: "1px 7px",
          fontSize: 11, fontWeight: 700, flexShrink: 0,
        }}>
          {badge}
        </span>
      )}
    </button>
  );
}
