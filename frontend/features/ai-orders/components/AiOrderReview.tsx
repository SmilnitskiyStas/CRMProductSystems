"use client";

import { useRouter } from "next/navigation";
import { BarChart2 } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useAcceptAiOrder, useRejectAiOrder, useUpdateAiOrderItem } from "../hooks/useAiOrders";
import { STATUS_META, type AiOrder, type AiOrderItem } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";

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

function DeltaBadge({ item, t }: { item: AiOrderItem; t: ReturnType<typeof useTranslations> }) {
  if (item.quantityBase === 0) return null;
  const pct = Math.round(((item.quantitySuggested - item.quantityBase) / item.quantityBase) * 100);
  if (pct === 0) return <span style={{ color: "#4B5563", fontSize: 11 }}>{t("noChange")}</span>;
  const up = pct > 0;
  return (
    <span style={{ color: up ? "#4ADE80" : "#F87171", fontSize: 11, fontWeight: 600 }}>
      {up ? "+" : ""}{pct}%
    </span>
  );
}

export function AiOrderReview({ order }: { order: AiOrder }) {
  const router = useRouter();
  const t = useTranslations("Dashboard.aiOrders.review");
  const tStatus = useTranslations("Dashboard.aiOrders.status");
  const tConfidence = useTranslations("Dashboard.aiOrders.review.confidence");
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
        onSuccess: () => toast.success(t("toastQuantityUpdated")),
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
              ⚡ {t("title", { store: order.storeName })}
            </span>
            <span style={{
              background: meta.bg, color: meta.color, fontSize: 11,
              borderRadius: 6, padding: "2px 10px", fontWeight: 600,
            }}>
              {tStatus.has(order.status) ? tStatus(order.status) : order.status}
            </span>
          </div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 4 }}>
            {order.orderDate} · {order.items.length} {t("itemsSuffix")} · {order.aiModel}
            {order.tokensUsed ? ` · ${order.tokensUsed} ${t("tokensSuffix")}` : ""}
          </div>
        </div>

        {!finalized && (
          <div style={{ display: "flex", gap: 10 }}>
            <Btn variant="danger" onClick={() =>
              reject.mutate(order.id, {
                onSuccess: () => toast.success(t("toastRejected")),
                onError: (e) => toast.error(e.message),
              })}>
              {t("reject")}
            </Btn>
            <Btn variant="success" disabled={accept.isPending} onClick={() =>
              accept.mutate(order.id, {
                onSuccess: () => toast.success(t("toastAccepted")),
                onError: (e) => toast.error(e.message),
              })}>
              {t("accept", { count: order.items.length })}
            </Btn>
          </div>
        )}
      </div>

      {/* Items */}
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr>
            <th style={th}>{t("headers.product")}</th>
            <th style={{ ...th, textAlign: "right" }}>{t("headers.base")}</th>
            <th style={{ ...th, textAlign: "right" }}>{t("headers.aiSuggests")}</th>
            <th style={{ ...th, textAlign: "right" }}>{t("headers.yourChange")}</th>
            <th style={th}>{t("headers.reason")}</th>
            <th style={th}>{t("headers.actions")}</th>
          </tr>
        </thead>
        <tbody>
          {order.items.map((item) => (
            <tr key={item.id}>
              <td style={td}>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div>{item.productName}</div>
                  {item.barcode && (
                    <div style={{ color: "#4B5563", fontSize: 11, fontFamily: "monospace" }}>{item.barcode}</div>
                  )}
                </div>
              </td>
              <td style={{ ...td, textAlign: "right", fontFamily: "monospace", color: "#9CA3AF" }}>
                {item.quantityBase}
              </td>
              <td style={{ ...td, textAlign: "right", fontFamily: "monospace" }}>
                <div style={{ fontWeight: 600 }}>{item.quantitySuggested}</div>
                <DeltaBadge item={item} t={t} />
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
                  <div style={{ color: "#3B82F6", fontSize: 10, marginTop: 2 }}>{t("edited")}</div>
                )}
              </td>
              <td style={{ ...td, maxWidth: 320 }}>
                {item.reasoning ? (
                  <div style={{ color: "#9CA3AF", fontSize: 12, lineHeight: 1.5 }}>
                    {item.reasoning}
                    {item.confidence && (
                      <span style={{ color: confidenceColor[item.confidence], fontSize: 10, marginLeft: 8 }}>
                        ● {tConfidence(item.confidence)}
                      </span>
                    )}
                  </div>
                ) : (
                  <span style={{ color: "#4B5563", fontSize: 12 }}>{t("formula")}</span>
                )}
              </td>
              <td style={td}>
                <ActionMenu
                  items={[
                    {
                      label: t("actionMenu.productAnalytics"),
                      icon: <BarChart2 size={13} />,
                      onClick: () => router.push(`/inventory/${item.productId}?tab=analytics`),
                    },
                  ]}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
