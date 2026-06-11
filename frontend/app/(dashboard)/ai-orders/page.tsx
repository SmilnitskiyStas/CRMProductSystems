"use client";

import { useState } from "react";
import { Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { AiOrderReview } from "@/features/ai-orders/components/AiOrderReview";
import { useAiOrder, useAiOrders, useGenerateAiOrder } from "@/features/ai-orders/hooks/useAiOrders";
import { useStores } from "@/features/stores/hooks/useStores";
import { STATUS_META } from "@/features/ai-orders/types";

const selectStyle: React.CSSProperties = {
  background: "#111827", border: "1px solid #1F2937", borderRadius: 8,
  color: "#E8EDF5", fontSize: 13, padding: "7px 10px",
};

export default function AiOrdersPage() {
  const { data: stores = [] } = useStores();
  const [storeId, setStoreId] = useState<string>("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const { data: list = [], isLoading } = useAiOrders(storeId || undefined);
  const { data: selected } = useAiOrder(selectedId);
  const generate = useGenerateAiOrder();

  const effectiveStoreId = storeId || stores[0]?.id || "";

  const handleGenerate = () =>
    generate.mutate(effectiveStoreId, {
      onSuccess: (order) => {
        toast.success(`AI пропозиція готова: ${order.items.length} позицій`);
        setSelectedId(order.id);
      },
      onError: (e) => toast.error(e.message),
    });

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 22 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            AI Замовлення
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            Claude аналізує погоду, події та акції — і коригує математичне замовлення з поясненнями
          </p>
        </div>
        <div style={{ display: "flex", gap: 10 }}>
          <select value={storeId} onChange={(e) => { setStoreId(e.target.value); setSelectedId(null); }} style={selectStyle}>
            <option value="">Всі магазини</option>
            {stores.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
          <Btn icon={<Sparkles size={15} />} disabled={generate.isPending || !effectiveStoreId} onClick={handleGenerate}>
            {generate.isPending ? "Claude аналізує…" : "Згенерувати зараз"}
          </Btn>
        </div>
      </div>

      {/* History list */}
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginBottom: 20 }}>
        {isLoading && <span style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</span>}
        {!isLoading && list.length === 0 && (
          <span style={{ color: "#4B5563", fontSize: 13 }}>
            Пропозицій ще немає — натисніть «Згенерувати зараз» або дочекайтесь щоденної генерації о 05:00.
          </span>
        )}
        {list.map((o) => {
          const meta = STATUS_META[o.status];
          const active = o.id === selectedId;
          return (
            <button
              key={o.id}
              onClick={() => setSelectedId(o.id)}
              style={{
                background: active ? "#1D3461" : "#0D1117",
                border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                borderRadius: 10, padding: "8px 14px", cursor: "pointer", textAlign: "left",
              }}
            >
              <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 600 }}>
                {o.orderDate} · {o.storeName}
              </div>
              <div style={{ fontSize: 11, marginTop: 2 }}>
                <span style={{ color: meta.color }}>{meta.label}</span>
                <span style={{ color: "#4B5563" }}> · {o.itemsCount} поз.</span>
              </div>
            </button>
          );
        })}
      </div>

      {selected && <AiOrderReview order={selected} />}
    </div>
  );
}
