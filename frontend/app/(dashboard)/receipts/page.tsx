"use client";

import { useState } from "react";
import { Eye, ExternalLink } from "lucide-react";
import { useReceipts } from "@/features/receipts/hooks/useReceipts";
import { ReceiptStatusBadge } from "@/features/receipts/components/ReceiptStatusBadge";
import type { ReceiptDto, ReceiptStatus } from "@/features/receipts/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_RECEIVE_STOCK, hasRole } from "@/lib/roles";
import { ActionMenu } from "@/components/ui/ActionMenu";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

const STATUS_TABS: { value: string; label: string }[] = [
  { value: "", label: "Всі" },
  { value: "draft", label: "Чернетки" },
  { value: "in_transit", label: "В дорозі" },
  { value: "received", label: "Прийнято" },
  { value: "cancelled", label: "Скасовано" },
];

function formatDate(s: string | null) {
  if (!s) return "—";
  return new Date(s).toLocaleDateString("uk-UA");
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

// ── Detail drawer content ────────────────────────────────────────────────────
function ReceiptDetail({ r }: { r: ReceiptDto }) {
  return (
    <>
      <DrawerSection title="Загальна інформація">
        <DrawerGrid>
          <DrawerField label="Постачальник" value={r.supplierName ?? "—"} />
          <DrawerField label="Магазин призначення" value={r.destinationStoreName} />
          <DrawerField
            label="Статус"
            value={<ReceiptStatusBadge status={r.status as ReceiptStatus} />}
          />
          <DrawerField label="Очікується" value={formatDate(r.expectedAt)} />
          <DrawerField label="Отримано" value={formatDate(r.receivedAt)} />
          <DrawerField label="Позицій" value={r.items.length} />
          <DrawerField label="Через центральний склад" value={r.viaCentralStore ? "Так" : "Ні"} />
          <DrawerField label="Дата створення" value={formatDate(r.createdAt)} />
        </DrawerGrid>
        {r.notes && <DrawerField label="Нотатки" value={r.notes} />}
        <DrawerField
          label="ID документу"
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {r.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={`Позиції (${r.items.length})`}>
        {r.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Немає позицій</div>
        ) : (
          <div style={{ border: "1px solid #1F2937", borderRadius: 8, overflow: "hidden" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {["Товар", "Партія", "Замовлено", "Отримано", "Термін"].map((h) => (
                    <th
                      key={h}
                      style={{
                        padding: "7px 10px",
                        color: "#4B5563",
                        fontWeight: 600,
                        textTransform: "uppercase",
                        fontSize: 10,
                        letterSpacing: "0.05em",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        background: "#0A0F1A",
                        textAlign: "center",
                      }}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {r.items.map((item) => (
                  <tr key={item.id}>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#E8EDF5",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        fontWeight: 500,
                      }}
                    >
                      {item.productName}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#4B5563",
                        fontFamily: "monospace",
                        fontSize: 11,
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.batchNumber ?? "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.quantityOrdered}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                        color:
                          item.quantityReceived != null &&
                          item.quantityReceived < item.quantityOrdered
                            ? "#FBBF24"
                            : "#4ADE80",
                      }}
                    >
                      {item.quantityReceived ?? "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        borderBottom: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.expiryDate ? formatDate(item.expiryDate) : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </DrawerSection>
    </>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────
export default function ReceiptsPage() {
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const [statusFilter, setStatusFilter] = useState("");
  const { data: receipts = [], isLoading } = useReceipts(
    statusFilter ? { status: statusFilter } : undefined,
    access === true,
  );

  const [selected, setSelected] = useState<ReceiptDto | null>(null);

  if (access === null) return null;
  if (!access) return <AccessDenied title="Прийомка" />;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Прийомка</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          Документи прийомки товарів від постачальників
        </p>
      </div>

      {/* Status tabs */}
      <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", paddingBottom: 0 }}>
        {STATUS_TABS.map((tab) => {
          const active = tab.value === statusFilter;
          return (
            <button
              key={tab.value}
              onClick={() => setStatusFilter(tab.value)}
              style={{
                background: "transparent",
                border: "none",
                borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
                color: active ? "#60A5FA" : "#6B7280",
                fontSize: 13,
                fontWeight: active ? 600 : 400,
                padding: "8px 14px",
                cursor: "pointer",
                marginBottom: -1,
                transition: "color 0.1s",
              }}
            >
              {tab.label}
            </button>
          );
        })}
      </div>

      {/* Table */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "hidden",
        }}
      >
        {isLoading ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            Завантаження…
          </div>
        ) : receipts.length === 0 ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            Немає прийомок
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {["#", "Магазин", "Постачальник", "Очікується", "Статус", "Позицій", "Дії"].map(
                  (h) => (
                    <th key={h} style={thStyle}>
                      {h}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {receipts.map((r) => (
                <tr key={r.id}>
                  <td
                    style={{
                      ...tdStyle,
                      fontFamily: "monospace",
                      fontSize: 11,
                      color: "#4B5563",
                    }}
                  >
                    {r.id.slice(0, 8)}…
                  </td>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {r.destinationStoreName}
                  </td>
                  <td style={tdStyle}>{r.supplierName ?? "—"}</td>
                  <td style={tdStyle}>{formatDate(r.expectedAt)}</td>
                  <td style={tdStyle}>
                    <ReceiptStatusBadge status={r.status as ReceiptStatus} />
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{r.items.length}</td>
                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: "Переглянути",
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(r),
                        },
                        {
                          label: "Відкрити сторінку",
                          icon: <ExternalLink size={13} />,
                          href: `/receipts/${r.id}`,
                        },
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title="Прийомка"
        subtitle={
          selected
            ? `${selected.supplierName ?? "Без постачальника"} → ${selected.destinationStoreName}`
            : ""
        }
      >
        {selected && <ReceiptDetail r={selected} />}
      </DetailDrawer>
    </div>
  );
}
