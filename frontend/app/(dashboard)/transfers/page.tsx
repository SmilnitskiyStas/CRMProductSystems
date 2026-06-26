"use client";

import { useState } from "react";
import { ArrowLeftRight, Eye, CheckCircle, XCircle } from "lucide-react";
import { ProductAnalyticsLink } from "@/components/ui/ProductAnalyticsLink";
import {
  useTransfers,
  useConfirmTransfer,
  useCancelTransfer,
} from "@/features/transfers/hooks/useTransfers";
import type { TransferDto, TransferStatus } from "@/features/transfers/types";
import { TRANSFER_STATUS_COLOR, TRANSFER_STATUS_LABEL } from "@/features/transfers/types";
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

const STATUS_TABS = [
  { value: "", label: "Всі" },
  { value: "draft", label: "Чернетки" },
  { value: "in_transit", label: "В дорозі" },
  { value: "received", label: "Отримано" },
];

function StatusBadge({ status }: { status: TransferStatus }) {
  const c = TRANSFER_STATUS_COLOR[status] ?? TRANSFER_STATUS_COLOR.draft;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 8px",
        borderRadius: 20,
        background: c.bg,
        color: c.text,
        fontSize: 11,
        fontWeight: 600,
      }}
    >
      {TRANSFER_STATUS_LABEL[status] ?? status}
    </span>
  );
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
function TransferDetail({ t }: { t: TransferDto }) {
  return (
    <>
      <DrawerSection title="Загальна інформація">
        <DrawerGrid>
          <DrawerField label="Звідки" value={t.fromStoreName} />
          <DrawerField label="Куди" value={t.toStoreName} />
          <DrawerField
            label="Статус"
            value={<StatusBadge status={t.status as TransferStatus} />}
          />
          <DrawerField
            label="Тип переміщення"
            value={t.transferType ?? "—"}
          />
          <DrawerField
            label="Дата створення"
            value={new Date(t.createdAt).toLocaleDateString("uk-UA")}
          />
          <DrawerField label="Позицій" value={t.items.length} />
        </DrawerGrid>
        {t.notes && <DrawerField label="Нотатки" value={t.notes} />}
        <DrawerField
          label="ID документу"
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {t.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={`Позиції (${t.items.length})`}>
        {t.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Немає позицій</div>
        ) : (
          <div style={{ border: "1px solid #1F2937", borderRadius: 8, overflow: "hidden" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {["Товар", "Партія", "К-сть", "Термін", ""].map((h) => (
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
                {t.items.map((item) => (
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
                      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                        <span style={{ flex: 1 }}>{item.productName}</span>
                        <ProductAnalyticsLink productId={item.productId} size={12} />
                      </div>
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
                      {item.quantity}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        borderBottom: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.expiryDate
                        ? new Date(item.expiryDate).toLocaleDateString("uk-UA")
                        : "—"}
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #1F2937", textAlign: "center" }}>
                      <ProductAnalyticsLink productId={item.productId} size={12} />
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
export default function TransfersPage() {
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const [statusFilter, setStatusFilter] = useState("");
  const { data: transfers = [], isLoading } = useTransfers(
    statusFilter ? { status: statusFilter } : undefined,
    access === true,
  );
  const confirm = useConfirmTransfer();
  const cancel = useCancelTransfer();

  const [selected, setSelected] = useState<TransferDto | null>(null);

  if (access === null) return null;
  if (!access) return <AccessDenied title="Переміщення" />;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Переміщення</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          Переміщення товарів між магазинами та складами
        </p>
      </div>

      {/* Status tabs */}
      <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
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
        ) : transfers.length === 0 ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            Немає переміщень
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {["Звідки", "", "Куди", "Позицій", "Дата", "Статус", "Дії"].map((h) => (
                  <th key={h} style={thStyle}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {transfers.map((t) => (
                <tr key={t.id}>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {t.fromStoreName}
                  </td>
                  <td style={{ ...tdStyle, color: "#4B5563" }}>
                    <ArrowLeftRight size={14} />
                  </td>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {t.toStoreName}
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{t.items.length}</td>
                  <td style={tdStyle}>
                    {new Date(t.createdAt).toLocaleDateString("uk-UA")}
                  </td>
                  <td style={tdStyle}>
                    <StatusBadge status={t.status as TransferStatus} />
                  </td>
                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: "Переглянути",
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(t),
                        },
                        { separator: true },
                        ...(t.status === "in_transit"
                          ? [
                              {
                                label: "Підтвердити отримання",
                                icon: <CheckCircle size={13} />,
                                variant: "success" as const,
                                disabled: confirm.isPending,
                                onClick: () => confirm.mutate(t.id),
                              },
                            ]
                          : []),
                        ...(t.status === "draft" || t.status === "in_transit"
                          ? [
                              {
                                label: "Скасувати",
                                icon: <XCircle size={13} />,
                                variant: "danger" as const,
                                disabled: cancel.isPending,
                                onClick: () => cancel.mutate(t.id),
                              },
                            ]
                          : []),
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
        title="Переміщення"
        subtitle={
          selected
            ? `${selected.fromStoreName} → ${selected.toStoreName} · ${new Date(selected.createdAt).toLocaleDateString("uk-UA")}`
            : ""
        }
      >
        {selected && <TransferDetail t={selected} />}
      </DetailDrawer>
    </div>
  );
}
