"use client";

import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useAcceptAiOrder, useRejectAiOrder, useUpdateAiOrderItem } from "../hooks/useAiOrders";
import { STATUS_META, type AiOrder, type AiOrderItem } from "../types";
import { ProductAnalyticsLink } from "@/components/ui/ProductAnalyticsLink";

const th: React.CSSProperties = {
  textAlign: "left", color: "#6B7280", fontSize: 11, fontWeight: 600,
  textTransform: "uppercase", letterSpacing: 0.5, padding: "10px 12px",
  borderBottom: "1px solid #1F2937", whiteSpace: "nowrap",
};

const td: React.CSSProperties = {
  color: "#E8EDF5", fontSize: 13, padding: "10px 12px",
  borderBottom: "1px solid #161B22", verticalAlign: "middle",
};

const confidenceColor: Record<string, string> = { high: "#4ADE80", medium: "#FBBF24", low: "#F87171" };

function DeltaBadge({ item }: { item: AiOrderItem }) {
  if (item.quantityBase === 0) return null;
  const pct = Math.round(((item.quantitySuggested - item.quantityBase) / item.quantityBase) * 100);
  if (pct === 0) return <span style={{ color: "#4B5563", fontSize: 11 }}>без змін</span>;
  const up = pct > 0;
  return (
    <span style={{ color: up ? "#4ADE80" : "#F87171", fontSize: 11, fontWeight: 600 }}>
      {up ? "+" : ""}{pct}%
    </span>
  );
}

export function AiOrderReview({ order }: { order: AiOrder }) {
  const updateItem = useUpdateAiOrderItem();
  const accept = useAcceptAiOrder();
  const reject = useRejectAiOrder();

  const finalized = order.status === "accepted" || order.status === "rejected"
    || order.status === "partially_accepted";
  const meta = STATUS_META[order.status];

  const handleQtyBlur = (item: AiOrderItem, value: string) => {
    const qty = Number(value);
    if (Number.isNaN(qty) || qty < 0 || qty === item.quantityFinal) return;
    updateItem.mutate(
      { orderId: order.id, itemId: item.id, quantityFinal: qty, editReason: null },
      {
        onSuccess: () => toast.success("Кількість оновлено"),
        onError: (e) => toast.error(e.message),
      },
    );
  };

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 16 }}>
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>
              ⚡ AI пропозиція — {order.storeName}
            </span>
            <span style={{
              background: meta.bg, color: meta.color, fontSize: 11,
              borderRadius: 6, padding: "2px 10px", fontWeight: 600,
            }}>
              {meta.label}
            </span>
          </div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 4 }}>
            {order.orderDate} · {order.items.length} позицій · {order.aiModel}
            {order.tokensUsed ? ` · ${order.tokensUsed} токенів` : ""}
          </div>
        </div>

        {!finalized && (
          <div style={{ display: "flex", gap: 10 }}>
            <Btn variant="danger" onClick={() =>
              reject.mutate(order.id, {
                onSuccess: () => toast.success("Пропозицію відхилено"),
                onError: (e) => toast.error(e.message),
              })}>
              Відхилити
            </Btn>
            <Btn variant="success" disabled={accept.isPending} onClick={() =>
              accept.mutate(order.id, {
                onSuccess: () => toast.success("Замовлення підтверджено"),
                onError: (e) => toast.error(e.message),
              })}>
              Підтвердити замовлення ({order.items.length})
            </Btn>
          </div>
        )}
      </div>

      {/* Items */}
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={th}>Товар</th>
            <th style={{ ...th, textAlign: "right" }}>Базове</th>
            <th style={{ ...th, textAlign: "right" }}>AI пропонує</th>
            <th style={{ ...th, textAlign: "right" }}>Ваша зміна</th>
            <th style={th}>Причина</th>
          </tr>
        </thead>
        <tbody>
          {order.items.map((item) => (
            <tr key={item.id}>
              <td style={td}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div>{item.productName}</div>
                    {item.barcode && (
                      <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>{item.barcode}</div>
                    )}
                  </div>
                  <ProductAnalyticsLink productId={item.productId} />
                </div>
              </td>
              <td style={{ ...td, textAlign: "right", fontFamily: "monospace", color: "#9CA3AF" }}>
                {item.quantityBase}
              </td>
              <td style={{ ...td, textAlign: "right", fontFamily: "monospace" }}>
                <div style={{ fontWeight: 600 }}>{item.quantitySuggested}</div>
                <DeltaBadge item={item} />
              </td>
              <td style={{ ...td, textAlign: "right" }}>
                <input
                  type="number" min={0} step={1}
                  defaultValue={item.quantityFinal}
                  disabled={finalized}
                  onBlur={(e) => handleQtyBlur(item, e.target.value)}
                  style={{
                    width: 80, textAlign: "right", fontFamily: "monospace",
                    background: "#111827", border: `1px solid ${item.wasEdited ? "#3B82F6" : "#1F2937"}`,
                    borderRadius: 8, color: "#E8EDF5", fontSize: 13, padding: "6px 10px",
                  }}
                />
                {item.wasEdited && (
                  <div style={{ color: "#3B82F6", fontSize: 10, marginTop: 2 }}>✏️ змінено</div>
                )}
              </td>
              <td style={{ ...td, maxWidth: 320 }}>
                {item.reasoning ? (
                  <div style={{ color: "#9CA3AF", fontSize: 12, lineHeight: 1.5 }}>
                    {item.reasoning}
                    {item.confidence && (
                      <span style={{ color: confidenceColor[item.confidence], fontSize: 10, marginLeft: 8 }}>
                        ● {item.confidence}
                      </span>
                    )}
                  </div>
                ) : (
                  <span style={{ color: "#4B5563", fontSize: 12 }}>математична формула</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
