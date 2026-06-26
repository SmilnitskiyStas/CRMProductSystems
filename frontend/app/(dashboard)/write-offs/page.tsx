"use client";

import { useState } from "react";
import {
  Eye, CheckCircle, XCircle, FileDown, ChevronDown,
} from "lucide-react";
import { ProductAnalyticsLink } from "@/components/ui/ProductAnalyticsLink";
import {
  useWriteOffs,
  useApproveWriteOff,
  useRejectWriteOff,
} from "@/features/write-offs/hooks/useWriteOffs";
import type { WriteOffDto, WriteOffStatus } from "@/features/write-offs/types";
import {
  WRITE_OFF_STATUS_COLOR,
  WRITE_OFF_STATUS_LABEL,
  WRITE_OFF_REASON_LABEL,
} from "@/features/write-offs/types";
import { ActionMenu } from "@/components/ui/ActionMenu";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

const STATUS_TABS = [
  { value: "", label: "Всі" },
  { value: "pending_approval", label: "На затвердженні" },
  { value: "approved", label: "Затверджено" },
  { value: "draft", label: "Чернетки" },
  { value: "rejected", label: "Відхилено" },
];

function StatusBadge({ status }: { status: WriteOffStatus }) {
  const c = WRITE_OFF_STATUS_COLOR[status] ?? WRITE_OFF_STATUS_COLOR.draft;
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
      {WRITE_OFF_STATUS_LABEL[status] ?? status}
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
function WriteOffDetail({ w }: { w: WriteOffDto }) {
  return (
    <>
      <DrawerSection title="Загальна інформація">
        <DrawerGrid>
          <DrawerField label="Магазин" value={w.storeName} />
          <DrawerField
            label="Статус"
            value={<StatusBadge status={w.status as WriteOffStatus} />}
          />
          <DrawerField
            label="Причина"
            value={w.reason ? (WRITE_OFF_REASON_LABEL[w.reason] ?? w.reason) : "—"}
          />
          <DrawerField
            label="Сума збитку"
            value={
              w.totalLossAmount != null
                ? `${w.totalLossAmount.toLocaleString("uk-UA")} ₴`
                : "—"
            }
            color="#F87171"
          />
          <DrawerField
            label="Дата створення"
            value={new Date(w.createdAt).toLocaleDateString("uk-UA")}
          />
          {w.approvedAt && (
            <DrawerField
              label="Дата затвердження"
              value={new Date(w.approvedAt).toLocaleDateString("uk-UA")}
            />
          )}
        </DrawerGrid>
        <DrawerField
          label="ID документу"
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {w.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={`Позиції (${w.items.length})`}>
        {w.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Немає позицій</div>
        ) : (
          <div
            style={{
              border: "1px solid #1F2937",
              borderRadius: 8,
              overflow: "hidden",
            }}
          >
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {["Товар", "Партія", "К-сть", "Збиток", ""].map((h) => (
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
                {w.items.map((item) => (
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
                        color: "#F87171",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.lossAmount != null
                        ? `${item.lossAmount.toLocaleString("uk-UA")} ₴`
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
export default function WriteOffsPage() {
  const [statusFilter, setStatusFilter] = useState("");
  const { data: writeOffs = [], isLoading } = useWriteOffs(
    statusFilter ? { status: statusFilter } : undefined,
  );
  const approve = useApproveWriteOff();
  const reject = useRejectWriteOff();

  const [selected, setSelected] = useState<WriteOffDto | null>(null);

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Списання</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          Документи списання товарів — затвердження та звіти
        </p>
      </div>

      {/* Status tabs */}
      <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
        {STATUS_TABS.map((tab) => {
          const active = tab.value === statusFilter;
          const pendingCount =
            tab.value === "pending_approval"
              ? writeOffs.filter((w) => w.status === "pending_approval").length
              : 0;
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
              {pendingCount > 0 && (
                <span
                  style={{
                    marginLeft: 6,
                    background: "#FBBF24",
                    color: "#000",
                    borderRadius: 10,
                    padding: "1px 6px",
                    fontSize: 10,
                    fontWeight: 700,
                  }}
                >
                  {pendingCount}
                </span>
              )}
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
        ) : writeOffs.length === 0 ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            Немає списань
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {["Магазин", "Причина", "Позицій", "Сума збитку", "Дата", "Статус", "Дії"].map(
                  (h) => (
                    <th key={h} style={thStyle}>
                      {h}
                    </th>
                  ),
                )}
              </tr>
            </thead>
            <tbody>
              {writeOffs.map((w) => (
                <tr
                  key={w.id}
                  style={{
                    background:
                      w.status === "pending_approval"
                        ? "rgba(251,191,36,0.03)"
                        : "transparent",
                  }}
                >
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {w.storeName}
                  </td>
                  <td style={tdStyle}>
                    {w.reason ? (WRITE_OFF_REASON_LABEL[w.reason] ?? w.reason) : "—"}
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{w.items.length}</td>
                  <td style={{ ...tdStyle, fontFamily: "monospace", color: "#F87171" }}>
                    {w.totalLossAmount != null
                      ? `${w.totalLossAmount.toLocaleString("uk-UA")} ₴`
                      : "—"}
                  </td>
                  <td style={tdStyle}>{new Date(w.createdAt).toLocaleDateString("uk-UA")}</td>
                  <td style={tdStyle}>
                    <StatusBadge status={w.status as WriteOffStatus} />
                  </td>
                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: "Переглянути",
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(w),
                        },
                        { separator: true },
                        ...(w.status === "pending_approval"
                          ? [
                              {
                                label: "Затвердити",
                                icon: <CheckCircle size={13} />,
                                variant: "success" as const,
                                disabled: approve.isPending,
                                onClick: () => approve.mutate(w.id),
                              },
                              {
                                label: "Відхилити",
                                icon: <XCircle size={13} />,
                                variant: "danger" as const,
                                disabled: reject.isPending,
                                onClick: () => reject.mutate(w.id),
                              },
                            ]
                          : []),
                        ...(w.status === "approved" && w.pdfUrl
                          ? [
                              {
                                label: "Завантажити PDF",
                                icon: <FileDown size={13} />,
                                href: w.pdfUrl,
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
        title="Списання"
        subtitle={selected ? `${selected.storeName} · ${new Date(selected.createdAt).toLocaleDateString("uk-UA")}` : ""}
      >
        {selected && <WriteOffDetail w={selected} />}
      </DetailDrawer>
    </div>
  );
}
