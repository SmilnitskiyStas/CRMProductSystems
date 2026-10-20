"use client";

import { useMemo, useState } from "react";
import { Calculator, CheckCircle2 } from "lucide-react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { OrderLinesTable } from "@/features/orders/components/OrderLinesTable";
import { useGenerateOrder } from "@/features/orders/hooks/useOrders";
import { useConfirmOrder } from "@/features/orders/hooks/useConfirmOrder";
import { ordersApi } from "@/features/orders/api/orders";
import { useSuppliers } from "@/features/suppliers/hooks/useSuppliers";
import type { OrderCalcResult, OrderLine } from "@/features/orders/types";
import { usePrimaryStoreId } from "@/lib/useStoreContext";

const PAGE_SIZE = 50;

const statCard: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "12px 16px",
  minWidth: 140,
};

export default function OrdersPage() {
  const t = useTranslations("Dashboard.orders.page");
  const locale = useLocale();
  const intl = locale === "en" ? "en-US" : "uk-UA";
  const primaryStoreId = usePrimaryStoreId();
  const generate = useGenerateOrder();
  const confirm = useConfirmOrder(primaryStoreId);
  const { data: suppliers } = useSuppliers();

  // The calculation lives here (not only in the mutation) so it can be refreshed after confirming.
  const [order, setOrder] = useState<OrderCalcResult | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [page, setPage] = useState(1);
  const [onlyToOrder, setOnlyToOrder] = useState(true);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const result = generate.data;

  // useMemo (not `?? []` inline): a fresh [] every render would invalidate every memo below.
  const allLines = useMemo(() => order?.lines ?? [], [order]);
  const visibleLines = useMemo(
    () => (onlyToOrder ? allLines.filter((l) => l.quantityToOrder > 0) : allLines),
    [allLines, onlyToOrder],
  );
  const totalPages = Math.max(1, Math.ceil(visibleLines.length / PAGE_SIZE));
  const safePage = Math.min(page, totalPages);
  const pageLines = visibleLines.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE);

  const orderableAll = useMemo(() => allLines.filter((l) => l.quantityToOrder > 0), [allLines]);
  const selectedLines = useMemo(
    () => orderableAll.filter((l) => selected.has(l.productId)),
    [orderableAll, selected],
  );

  const supplierName = (id: string | null) =>
    id ? suppliers?.find((s) => s.id === id)?.name ?? t("confirmDialog.unknownSupplier") : t("confirmDialog.noSupplier");

  // Summary shown in the confirmation dialog: lines grouped the same way the receipts are created.
  const summary = useMemo(() => {
    const groups = new Map<string, { name: string; positions: number; qty: number; amount: number }>();
    for (const l of selectedLines) {
      const key = l.supplierId ?? "";
      const g = groups.get(key) ?? { name: supplierName(l.supplierId), positions: 0, qty: 0, amount: 0 };
      g.positions += 1;
      g.qty += l.quantityToOrder;
      g.amount += l.quantityToOrder * (l.pricePurchase ?? 0);
      groups.set(key, g);
    }
    return Array.from(groups.values());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedLines, suppliers]);

  const handleGenerate = () => {
    if (!primaryStoreId) return;
    generate.mutate(primaryStoreId, {
      onSuccess: (r) => {
        setOrder(r.order);
        setSelected(new Set());
        setPage(1);
        toast.success(
          t("toastResult", {
            adu: r.adu.withEffectiveAdu,
            buffers: r.buffers.buffersCalculated,
            toOrder: r.order.linesToOrder,
          }),
        );
      },
      onError: (err) => toast.error(err.message),
    });
  };

  const toggle = (productId: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(productId)) next.delete(productId);
      else next.add(productId);
      return next;
    });

  const togglePage = (select: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev);
      for (const l of pageLines) {
        if (l.quantityToOrder <= 0) continue;
        if (select) next.add(l.productId);
        else next.delete(l.productId);
      }
      return next;
    });

  const handleConfirm = () => {
    confirm.mutate(selectedLines as OrderLine[], {
      onSuccess: async (r) => {
        setConfirmOpen(false);
        setSelected(new Set());
        toast.success(t("toastConfirmed", { positions: r.positions, receipts: r.receipts }));
        // Re-run only the order calculation: the created receipts now count as "in transit", so the
        // ordered lines drop out and nothing gets ordered twice.
        if (primaryStoreId) {
          try {
            setOrder(await ordersApi.calculate(primaryStoreId));
          } catch {
            /* the confirmed order is already saved; a stale table is harmless */
          }
        }
      },
      onError: (err) => toast.error(t("toastConfirmFailed", { message: err.message })),
    });
  };

  const totalAmount = summary.reduce((s, g) => s + g.amount, 0);
  const totalQty = summary.reduce((s, g) => s + g.qty, 0);

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 22 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {t("subtitle")}
          </p>
        </div>
        <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
          {!primaryStoreId && (
            <span style={{ color: "#6B7280", fontSize: 12 }}>{t("selectStoreHint")}</span>
          )}
          <Btn
            icon={<Calculator size={15} />}
            onClick={handleGenerate}
            disabled={generate.isPending || !primaryStoreId}
          >
            {generate.isPending ? t("generating") : t("generate")}
          </Btn>
        </div>
      </div>

      {/* Pipeline stats after generation */}
      {result && (
        <div style={{ display: "flex", gap: 12, marginBottom: 20 }}>
          <div style={statCard}>
            <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase" }}>{t("statAduCalculated")}</div>
            <div style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700 }}>
              {result.adu.withEffectiveAdu}
              <span style={{ color: "#4B5563", fontSize: 12, fontWeight: 400 }}>
                {" "}/ {result.adu.productsProcessed}
              </span>
            </div>
          </div>
          <div style={statCard}>
            <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase" }}>{t("statBuffersUpdated")}</div>
            <div style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700 }}>
              {result.buffers.buffersCalculated}
            </div>
          </div>
          <div style={statCard}>
            <div style={{ color: "#6B7280", fontSize: 11, textTransform: "uppercase" }}>{t("statPositionsToOrder")}</div>
            <div style={{ color: "#93C5FD", fontSize: 20, fontWeight: 700 }}>
              {order?.linesToOrder ?? result.order.linesToOrder}
              <span style={{ color: "#4B5563", fontSize: 12, fontWeight: 400 }}>
                {" "}/ {order?.productsEvaluated ?? result.order.productsEvaluated}
              </span>
            </div>
          </div>
        </div>
      )}

      {order ? (
        <>
          {/* Selection toolbar */}
          <div style={{
            display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap", marginBottom: 14,
            background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "10px 14px",
          }}>
            <label style={{ display: "flex", alignItems: "center", gap: 6, color: "#9CA3AF", fontSize: 13, cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={onlyToOrder}
                onChange={(e) => { setOnlyToOrder(e.target.checked); setPage(1); }}
                style={{ accentColor: "#3B82F6" }}
              />
              {t("onlyToOrder")}
            </label>

            <button
              onClick={() => setSelected(new Set(orderableAll.map((l) => l.productId)))}
              disabled={orderableAll.length === 0}
              style={linkBtn}
            >
              {t("selectAll", { count: orderableAll.length.toLocaleString(intl) })}
            </button>
            <button onClick={() => setSelected(new Set())} disabled={selected.size === 0} style={linkBtn}>
              {t("clearSelection")}
            </button>

            <span style={{ marginLeft: "auto", color: "#6B7280", fontSize: 13 }}>
              {t("selectedCount", { count: selectedLines.length.toLocaleString(intl) })}
            </span>
            <Btn
              icon={<CheckCircle2 size={15} />}
              onClick={() => setConfirmOpen(true)}
              disabled={selectedLines.length === 0 || confirm.isPending || !primaryStoreId}
            >
              {t("confirmOrder", { count: selectedLines.length.toLocaleString(intl) })}
            </Btn>
          </div>

          <OrderLinesTable
            lines={pageLines}
            selectedIds={selected}
            onToggle={toggle}
            onTogglePage={togglePage}
            page={safePage}
            totalPages={totalPages}
            totalCount={visibleLines.length}
            onPageChange={(p) => { setPage(p); window.scrollTo(0, 0); }}
          />
        </>
      ) : (
        <div style={{ color: "#4B5563", fontSize: 14, padding: "64px 0", textAlign: "center" }}>
          {t("emptyTitle")}<br />
          {t("emptySubtitle")}
        </div>
      )}

      {/* Confirmation dialog */}
      {confirmOpen && (
        <div
          role="dialog"
          aria-modal="true"
          style={{
            position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)", zIndex: 300,
            display: "flex", alignItems: "center", justifyContent: "center", padding: 16,
          }}
          onClick={() => !confirm.isPending && setConfirmOpen(false)}
        >
          <div
            onClick={(e) => e.stopPropagation()}
            style={{
              background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12,
              width: "min(560px, 100%)", maxHeight: "86vh", overflowY: "auto", padding: 22,
            }}
          >
            <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: "0 0 6px" }}>
              {t("confirmDialog.title")}
            </h2>
            <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 16px" }}>
              {t("confirmDialog.body", { positions: selectedLines.length, suppliers: summary.length })}
            </p>

            <div style={{ display: "flex", flexDirection: "column", gap: 6, marginBottom: 16 }}>
              {summary.map((g) => (
                <div key={g.name} style={{
                  display: "flex", justifyContent: "space-between", gap: 12, background: "#0A0F1A",
                  border: "1px solid #1F2937", borderRadius: 8, padding: "8px 12px", fontSize: 13,
                }}>
                  <span style={{ color: "#E8EDF5" }}>{g.name}</span>
                  <span style={{ color: "#9CA3AF", fontFamily: "monospace" }}>
                    {t("confirmDialog.groupLine", {
                      positions: g.positions,
                      qty: g.qty.toLocaleString(intl, { maximumFractionDigits: 2 }),
                    })}
                    {g.amount > 0 && ` · ${g.amount.toLocaleString(intl, { maximumFractionDigits: 0 })} ₴`}
                  </span>
                </div>
              ))}
            </div>

            <div style={{ display: "flex", justifyContent: "space-between", color: "#9CA3AF", fontSize: 13, marginBottom: 18 }}>
              <span>{t("confirmDialog.total")}</span>
              <span style={{ color: "#E8EDF5", fontFamily: "monospace", fontWeight: 700 }}>
                {totalQty.toLocaleString(intl, { maximumFractionDigits: 2 })}
                {totalAmount > 0 && ` · ${totalAmount.toLocaleString(intl, { maximumFractionDigits: 0 })} ₴`}
              </span>
            </div>

            <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
              <Btn variant="ghost" onClick={() => setConfirmOpen(false)} disabled={confirm.isPending}>
                {t("confirmDialog.cancel")}
              </Btn>
              <Btn icon={<CheckCircle2 size={15} />} onClick={handleConfirm} disabled={confirm.isPending}>
                {confirm.isPending ? t("confirmDialog.confirming") : t("confirmDialog.confirm")}
              </Btn>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

const linkBtn: React.CSSProperties = {
  background: "transparent",
  border: "none",
  color: "#60A5FA",
  fontSize: 13,
  cursor: "pointer",
  padding: 0,
};
