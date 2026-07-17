"use client";

import { useState } from "react";
import { Calculator } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { OrderLinesTable } from "@/features/orders/components/OrderLinesTable";
import { useGenerateOrder } from "@/features/orders/hooks/useOrders";
import { useStores } from "@/features/stores/hooks/useStores";

const selectStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
};

const statCard: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "12px 16px",
  minWidth: 140,
};

export default function OrdersPage() {
  const t = useTranslations("Dashboard.orders.page");
  const { data: stores = [] } = useStores();
  const [storeId, setStoreId] = useState<string>("");
  const generate = useGenerateOrder();

  const effectiveStoreId = storeId || stores[0]?.id || "";
  const result = generate.data;

  const handleGenerate = () => {
    if (!effectiveStoreId) return;
    generate.mutate(effectiveStoreId, {
      onSuccess: (r) =>
        toast.success(
          t("toastResult", {
            adu: r.adu.withEffectiveAdu,
            buffers: r.buffers.buffersCalculated,
            toOrder: r.order.linesToOrder,
          }),
        ),
      onError: (err) => toast.error(err.message),
    });
  };

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
          <select value={storeId} onChange={(e) => setStoreId(e.target.value)} style={selectStyle}>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
          <Btn
            icon={<Calculator size={15} />}
            onClick={handleGenerate}
            disabled={generate.isPending || !effectiveStoreId}
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
              {result.order.linesToOrder}
              <span style={{ color: "#4B5563", fontSize: 12, fontWeight: 400 }}>
                {" "}/ {result.order.productsEvaluated}
              </span>
            </div>
          </div>
        </div>
      )}

      {result ? (
        <OrderLinesTable lines={result.order.lines} />
      ) : (
        <div style={{ color: "#4B5563", fontSize: 14, padding: "64px 0", textAlign: "center" }}>
          {t("emptyTitle")}<br />
          {t("emptySubtitle")}
        </div>
      )}
    </div>
  );
}
