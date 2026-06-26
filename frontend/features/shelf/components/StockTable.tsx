"use client";

import { useState } from "react";
import { Eye, ShieldCheck } from "lucide-react";
import { ProductAnalyticsLink } from "@/components/ui/ProductAnalyticsLink";
import type { ProductStockDto, BatchStatus } from "../types";
import { STATUS_COLOR, STATUS_LABEL } from "../types";
import { StatusBadge } from "./StatusBadge";
import { ActionMenu } from "@/components/ui/ActionMenu";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

interface Props {
  items: ProductStockDto[];
  isLoading: boolean;
  selectedIds: Set<string>;
  onSelectId: (id: string, checked: boolean) => void;
  onSelectAll: (checked: boolean) => void;
  onVerify?: (id: string) => void;
}

const CRITICAL_STATUSES: BatchStatus[] = ["critical", "expired"];

function formatDate(dateStr: string): string {
  const [y, m, d] = dateStr.split("-");
  return `${d}.${m}.${y}`;
}

function getDaysColor(days: number): string {
  if (days <= 0) return STATUS_COLOR.expired.text;
  if (days <= 3) return STATUS_COLOR.critical.text;
  if (days <= 7) return STATUS_COLOR.warning.text;
  return "#6B7280";
}

const thStyle: React.CSSProperties = {
  padding: "10px 14px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textAlign: "center",
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  borderBottom: "1px solid #374151",
  borderRight: "1px solid #374151",
  background: "#0A0F1A",
  whiteSpace: "nowrap",
};

const tdStyle: React.CSSProperties = {
  padding: "10px 14px",
  color: "#9CA3AF",
  fontSize: 12,
  borderBottom: "1px solid #1F2937",
  borderRight: "1px solid #1F2937",
  textAlign: "center",
  whiteSpace: "nowrap",
};

// ── Detail drawer ────────────────────────────────────────────────────────────
function StockDetail({ item }: { item: ProductStockDto }) {
  const fillPct =
    item.quantityInitial > 0
      ? Math.round((item.quantity / item.quantityInitial) * 100)
      : 0;

  return (
    <>
      <DrawerSection title="Товар та партія">
        <DrawerField label="Назва товару" value={item.productName} />
        <DrawerGrid>
          <DrawerField
            label="Штрихкод"
            value={
              <span style={{ fontFamily: "monospace", color: "#4B5563" }}>
                {item.productBarcode ?? "—"}
              </span>
            }
          />
          <DrawerField
            label="Партія"
            value={
              <span style={{ fontFamily: "monospace", color: "#9CA3AF" }}>
                {item.batchNumber ?? "—"}
              </span>
            }
          />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title="Розміщення">
        <DrawerGrid>
          <DrawerField label="Магазин" value={item.storeName} />
          <DrawerField label="Зона" value={item.zoneName ?? "—"} />
          <DrawerField
            label="Полиця"
            value={item.shelfNumber != null ? `#${item.shelfNumber}` : "—"}
          />
          <DrawerField label="Джерело" value={item.sourceType ?? "—"} />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title="Залишок та терміни">
        <DrawerGrid>
          <DrawerField
            label="Залишок"
            value={
              <span style={{ fontFamily: "monospace" }}>
                {item.quantity.toLocaleString("uk-UA")}
              </span>
            }
          />
          <DrawerField
            label="Початкова к-сть"
            value={
              <span style={{ fontFamily: "monospace", color: "#4B5563" }}>
                {item.quantityInitial.toLocaleString("uk-UA")}
              </span>
            }
          />
          <DrawerField
            label="Термін придатності"
            value={formatDate(item.expiryDate)}
          />
          <DrawerField
            label="Днів залишилось"
            value={
              <span
                style={{ fontFamily: "monospace", fontWeight: 700, color: getDaysColor(item.daysLeft) }}
              >
                {item.daysLeft <= 0 ? `прострочено (${Math.abs(item.daysLeft)} д.)` : item.daysLeft}
              </span>
            }
          />
          <DrawerField
            label="Статус"
            value={<StatusBadge status={item.status} />}
          />
          <DrawerField label="Використано" value={`${fillPct}%`} />
        </DrawerGrid>

        {/* Fill bar */}
        <div style={{ marginTop: 4 }}>
          <div
            style={{
              height: 6,
              background: "#1F2937",
              borderRadius: 4,
              overflow: "hidden",
            }}
          >
            <div
              style={{
                height: "100%",
                width: `${Math.min(fillPct, 100)}%`,
                background:
                  fillPct > 80 ? "#4ADE80" : fillPct > 40 ? "#FBBF24" : "#F87171",
                borderRadius: 4,
                transition: "width 0.3s",
              }}
            />
          </div>
        </div>
      </DrawerSection>

      <DrawerSection title="Часові мітки">
        <DrawerGrid>
          <DrawerField
            label="Додано"
            value={new Date(item.addedAt).toLocaleString("uk-UA")}
          />
          <DrawerField
            label="Остання перевірка"
            value={new Date(item.lastCheckedAt).toLocaleString("uk-UA")}
          />
        </DrawerGrid>
      </DrawerSection>
    </>
  );
}

// ── Table ────────────────────────────────────────────────────────────────────
export function StockTable({
  items,
  isLoading,
  selectedIds,
  onSelectId,
  onSelectAll,
  onVerify,
}: Props) {
  const [hovered, setHovered] = useState<string | null>(null);
  const [selected, setSelected] = useState<ProductStockDto | null>(null);

  if (isLoading) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
        Завантаження залишків…
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
        Немає партій за вибраними фільтрами
      </div>
    );
  }

  const allSelected = items.length > 0 && items.every((i) => selectedIds.has(i.id));
  const someSelected = items.some((i) => selectedIds.has(i.id)) && !allSelected;

  return (
    <>
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 900 }}>
          <thead>
            <tr>
              <th style={{ ...thStyle, width: 36 }}>
                <input
                  type="checkbox"
                  checked={allSelected}
                  ref={(el) => {
                    if (el) el.indeterminate = someSelected;
                  }}
                  onChange={(e) => onSelectAll(e.target.checked)}
                  style={{ cursor: "pointer", accentColor: "#3B82F6" }}
                />
              </th>
              <th style={thStyle}>Назва</th>
              <th style={thStyle}>Штрихкод</th>
              <th style={thStyle}>Зона</th>
              <th style={thStyle}>Партія</th>
              <th style={thStyle}>К-сть</th>
              <th style={thStyle}>Термін</th>
              <th style={{ ...thStyle, fontFamily: "monospace" }}>Дні</th>
              <th style={thStyle}>Статус</th>
              <th style={{ ...thStyle, borderRight: "none" }}>Дії</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => {
              const isCritical = CRITICAL_STATUSES.includes(item.status);
              const isSelected = selectedIds.has(item.id);
              const isHovered = hovered === item.id;
              const rowBg = isSelected
                ? "#1D3461"
                : isCritical
                ? "rgba(239,68,68,0.04)"
                : isHovered
                ? "#0D1117"
                : "transparent";

              return (
                <tr
                  key={item.id}
                  style={{ background: rowBg, transition: "background 0.1s" }}
                  onMouseEnter={() => setHovered(item.id)}
                  onMouseLeave={() => setHovered(null)}
                >
                  <td style={{ ...tdStyle, width: 36 }}>
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={(e) => onSelectId(item.id, e.target.checked)}
                      style={{ cursor: "pointer", accentColor: "#3B82F6" }}
                    />
                  </td>

                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500, maxWidth: 240 }}>
                    <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                      <div
                        style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", flex: 1 }}
                        title={item.productName}
                      >
                        {item.productName}
                      </div>
                      <ProductAnalyticsLink productId={item.productId} />
                    </div>
                    <div style={{ color: "#4B5563", fontSize: 11, marginTop: 1 }}>
                      {item.storeName}
                    </div>
                  </td>

                  <td style={{ ...tdStyle, fontFamily: "monospace", fontSize: 11 }}>
                    {item.productBarcode ?? "—"}
                  </td>

                  <td style={tdStyle}>{item.zoneName ?? "—"}</td>

                  <td style={{ ...tdStyle, fontFamily: "monospace", fontSize: 11 }}>
                    {item.batchNumber ?? "—"}
                  </td>

                  <td style={{ ...tdStyle, color: "#E8EDF5" }}>
                    {item.quantity.toLocaleString("uk-UA")}
                  </td>

                  <td style={tdStyle}>{formatDate(item.expiryDate)}</td>

                  <td
                    style={{
                      ...tdStyle,
                      fontFamily: "monospace",
                      color: getDaysColor(item.daysLeft),
                      fontWeight: 600,
                    }}
                  >
                    {item.daysLeft <= 0 ? (
                      <span style={{ color: STATUS_COLOR.expired.text }}>
                        −{Math.abs(item.daysLeft)}
                      </span>
                    ) : (
                      item.daysLeft
                    )}
                  </td>

                  <td style={tdStyle}>
                    <StatusBadge status={item.status} />
                  </td>

                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: "Переглянути деталі",
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(item),
                        },
                        { separator: true },
                        ...(item.status === "needs_verification" && onVerify
                          ? [
                              {
                                label: "Позначити перевіреним",
                                icon: <ShieldCheck size={13} />,
                                variant: "success" as const,
                                onClick: () => onVerify(item.id),
                              },
                            ]
                          : []),
                        {
                          label: `Статус: ${STATUS_LABEL[item.status]}`,
                          variant: "warning" as const,
                          disabled: true,
                        },
                      ]}
                    />
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={selected?.productName ?? ""}
        subtitle={selected ? `${selected.storeName}${selected.zoneName ? ` · ${selected.zoneName}` : ""}` : ""}
        width={540}
        actions={selected && <ProductAnalyticsLink productId={selected.productId} />}
      >
        {selected && <StockDetail item={selected} />}
      </DetailDrawer>
    </>
  );
}
