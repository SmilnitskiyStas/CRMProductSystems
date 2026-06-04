"use client";

import type { AttentionItem } from "../types";

interface Props {
  items: AttentionItem[] | undefined;
  isLoading: boolean;
}

export function QuickActions({ items = [], isLoading }: Props) {
  const criticalItems = items.filter((i) => i.status === "critical" || i.status === "expired").slice(0, 5);

  return (
    <div
      style={{
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 12,
        overflow: "hidden",
        display: "flex",
        flexDirection: "column",
      }}
    >
      <div style={{ padding: "16px 20px", borderBottom: "1px solid #1F2937" }}>
        <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: 0 }}>Швидкі дії</h2>
      </div>

      <div style={{ padding: 16, flex: 1 }}>
        {/* Action buttons */}
        <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 20 }}>
          <ActionButton label="Перевірити критичні" accent="#ef4444" />
          <ActionButton label="Списати прострочені" accent="#6B7280" />
          <ActionButton label="Зробити замовлення" accent="#3B82F6" />
        </div>

        {/* Critical items list */}
        <div style={{ borderTop: "1px solid #1F2937", paddingTop: 16 }}>
          <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.06em", marginBottom: 12 }}>
            Критичні товари
          </div>

          {isLoading ? (
            <div style={{ color: "#374151", fontSize: 13, textAlign: "center", padding: "16px 0" }}>
              Завантаження…
            </div>
          ) : criticalItems.length === 0 ? (
            <div
              style={{
                background: "#0d2818",
                border: "1px solid #166534",
                borderRadius: 8,
                padding: "12px 14px",
                color: "#22c55e",
                fontSize: 13,
                textAlign: "center",
              }}
            >
              Критичних товарів немає
            </div>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              {criticalItems.map((item) => (
                <div
                  key={item.id}
                  style={{
                    background: "#1a0a0a",
                    border: "1px solid #991b1b30",
                    borderRadius: 8,
                    padding: "10px 12px",
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    gap: 8,
                  }}
                >
                  <div>
                    <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, marginBottom: 2 }}>
                      {item.name}
                    </div>
                    <div style={{ color: "#6B7280", fontSize: 11 }}>{item.category}</div>
                  </div>
                  <div
                    style={{
                      color: item.quantity === 0 ? "#9CA3AF" : "#EF4444",
                      fontSize: 14,
                      fontWeight: 700,
                      fontFamily: "monospace",
                      flexShrink: 0,
                    }}
                  >
                    {item.quantity === 0 ? "OUT" : item.quantity}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function ActionButton({ label, accent }: { label: string; accent: string }) {
  return (
    <button
      style={{
        width: "100%",
        padding: "9px 14px",
        background: "transparent",
        border: `1px solid ${accent}40`,
        borderRadius: 8,
        color: accent,
        fontSize: 13,
        fontWeight: 500,
        cursor: "pointer",
        textAlign: "left",
        transition: "background 0.15s",
      }}
      onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.background = `${accent}10`)}
      onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.background = "transparent")}
    >
      {label}
    </button>
  );
}
