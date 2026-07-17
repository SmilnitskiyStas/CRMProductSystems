"use client";

import { useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { ArrowLeft, Check, Package, Truck, Building2, Calendar, Hash, CheckCircle2 } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import {
  useReceiptDetail,
  useUpdateReceiptItems,
  useReceiveReceipt,
  useCancelReceipt,
} from "@/features/receipts/hooks/useReceipts";
import { ReceiptStatusBadge } from "@/features/receipts/components/ReceiptStatusBadge";
import type { ReceiptStatus, UpdateItemPayload } from "@/features/receipts/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_RECEIVE_STOCK, hasRole } from "@/lib/roles";

// ── Styles ────────────────────────────────────────────────────────────────────

const thStyle: React.CSSProperties = {
  padding: "10px 14px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  textAlign: "center",
  borderBottom: "1px solid #374151",
  borderRight: "1px solid #374151",
  background: "#0A0F1A",
  whiteSpace: "nowrap",
};

const tdBase: React.CSSProperties = {
  padding: "10px 14px",
  borderBottom: "1px solid #1F2937",
  borderRight: "1px solid #1F2937",
  textAlign: "center",
  verticalAlign: "middle",
};

const inputStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "5px 8px",
  outline: "none",
  width: "100%",
  boxSizing: "border-box",
  textAlign: "center",
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatDate(s: string | null | undefined): string {
  if (!s) return "";
  if (s.includes("T")) return s.slice(0, 10);
  return s;
}

function displayDate(s: string | null | undefined, intlLocale: string): string {
  if (!s) return "—";
  try {
    return new Date(s).toLocaleDateString(intlLocale);
  } catch {
    return s;
  }
}

// ── Stat card ─────────────────────────────────────────────────────────────────
function StatCard({
  icon,
  label,
  value,
  sub,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
  sub?: string;
}) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "14px 18px",
        display: "flex",
        alignItems: "flex-start",
        gap: 12,
        flex: "1 1 180px",
        minWidth: 0,
      }}
    >
      <div
        style={{
          color: "#4B5563",
          display: "flex",
          alignItems: "center",
          paddingTop: 2,
          flexShrink: 0,
        }}
      >
        {icon}
      </div>
      <div style={{ minWidth: 0 }}>
        <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 4 }}>{label}</div>
        <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, wordBreak: "break-word" }}>
          {value}
        </div>
        {sub && <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>{sub}</div>}
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────
export default function ReceiptDetailPage() {
  const t = useTranslations("Dashboard.receipts");
  const tDetail = useTranslations("Dashboard.receipts.detail");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const { id } = useParams<{ id: string }>();
  const router = useRouter();

  const { data: receipt, isLoading } = useReceiptDetail(access === true ? id : null);
  const updateItems = useUpdateReceiptItems(id);
  const receive = useReceiveReceipt();
  const cancel = useCancelReceipt();

  const [edits, setEdits] = useState<
    Record<
      string,
      {
        quantityReceived?: string;
        expiryDate?: string;
        batchNumber?: string;
        confirmed: boolean;
      }
    >
  >({});

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  if (isLoading) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
        {tCommon("loading")}
      </div>
    );
  }
  if (!receipt) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#F87171", fontSize: 13 }}>
        {tDetail("notFound")}
      </div>
    );
  }

  const isEditable = receipt.status === "draft" || receipt.status === "in_transit";

  function getEdit(itemId: string) {
    return (
      edits[itemId] ?? {
        confirmed: receipt!.items.find((i) => i.id === itemId)?.isProcessed ?? false,
      }
    );
  }

  function updateEdit(
    itemId: string,
    patch: Partial<{
      quantityReceived: string;
      expiryDate: string;
      batchNumber: string;
      confirmed: boolean;
    }>,
  ) {
    setEdits((prev) => ({ ...prev, [itemId]: { ...getEdit(itemId), ...patch } }));
  }

  const processedCount = receipt.items.filter(
    (i) => getEdit(i.id).confirmed || i.isProcessed,
  ).length;
  const allProcessed = processedCount === receipt.items.length;

  async function handleSave() {
    const payload: UpdateItemPayload[] = Object.entries(edits)
      .filter(([, e]) => e.confirmed || e.quantityReceived || e.expiryDate || e.batchNumber)
      .map(([itemId, e]) => ({
        itemId,
        quantityReceived: e.quantityReceived ? parseFloat(e.quantityReceived) : undefined,
        expiryDate: e.expiryDate || undefined,
        batchNumber: e.batchNumber || undefined,
      }));
    if (payload.length > 0) {
      await updateItems.mutateAsync(payload);
    }
  }

  async function handleReceive() {
    await handleSave();
    await receive.mutateAsync(id);
    router.push("/receipts");
  }

  return (
    <div style={{ padding: "24px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* ── Header ─────────────────────────────────────────────────────── */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <button
          onClick={() => router.push("/receipts")}
          style={{
            background: "transparent",
            border: "none",
            color: "#6B7280",
            cursor: "pointer",
            display: "flex",
            alignItems: "center",
            gap: 4,
            fontSize: 13,
            padding: 0,
            flexShrink: 0,
          }}
        >
          <ArrowLeft size={15} /> {tCommon("back")}
        </button>

        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
            <h1 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: 0 }}>
              {t("title")}
            </h1>
            <ReceiptStatusBadge status={receipt.status as ReceiptStatus} />
          </div>
          <p style={{ color: "#4B5563", fontSize: 12, marginTop: 3, marginBottom: 0 }}>
            {receipt.destinationStoreName}
            {receipt.supplierName && ` · ${receipt.supplierName}`}
            {receipt.expectedAt &&
              ` · ${t("drawer.expected")}: ${new Date(receipt.expectedAt).toLocaleDateString(intlLocale)}`}
          </p>
        </div>

        {isEditable && (
          <div style={{ display: "flex", gap: 8, flexShrink: 0 }}>
            <button
              onClick={() => cancel.mutate(id)}
              disabled={cancel.isPending}
              style={{
                background: "transparent",
                border: "1px solid #374151",
                borderRadius: 8,
                color: "#6B7280",
                fontSize: 13,
                padding: "8px 14px",
                cursor: "pointer",
              }}
            >
              {tCommon("cancel")}
            </button>
            <button
              onClick={handleReceive}
              disabled={!allProcessed || receive.isPending}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 6,
                background: allProcessed ? "#059669" : "#1F2937",
                border: "none",
                borderRadius: 8,
                color: allProcessed ? "#fff" : "#4B5563",
                fontSize: 13,
                fontWeight: 600,
                padding: "8px 16px",
                cursor: allProcessed ? "pointer" : "not-allowed",
                transition: "background 0.2s",
                opacity: receive.isPending ? 0.6 : 1,
              }}
            >
              <Check size={14} />
              {tDetail("confirmReceive")}
            </button>
          </div>
        )}
      </div>

      {/* ── Info cards ─────────────────────────────────────────────────── */}
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        <StatCard
          icon={<Building2 size={16} />}
          label={t("drawer.destinationStore")}
          value={receipt.destinationStoreName}
        />
        <StatCard
          icon={<Truck size={16} />}
          label={t("drawer.supplier")}
          value={receipt.supplierName ?? "—"}
        />
        <StatCard
          icon={<Calendar size={16} />}
          label={t("drawer.expected")}
          value={displayDate(receipt.expectedAt, intlLocale)}
          sub={receipt.receivedAt ? `${t("drawer.received")}: ${displayDate(receipt.receivedAt, intlLocale)}` : undefined}
        />
        <StatCard
          icon={<Package size={16} />}
          label={t("drawer.items")}
          value={tDetail("itemsCount", { count: receipt.items.length })}
          sub={receipt.viaCentralStore ? t("drawer.viaCentral") : undefined}
        />
      </div>

      {/* ── Progress bar ───────────────────────────────────────────────── */}
      {isEditable && receipt.items.length > 0 && (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "14px 18px",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
            <span style={{ color: "#9CA3AF", fontSize: 13, fontWeight: 500 }}>
              {tDetail("progress")}
            </span>
            <span style={{ color: allProcessed ? "#4ADE80" : "#6B7280", fontSize: 13, fontWeight: 600 }}>
              {processedCount} / {receipt.items.length}
            </span>
          </div>
          <div style={{ height: 6, background: "#1F2937", borderRadius: 3 }}>
            <div
              style={{
                height: "100%",
                borderRadius: 3,
                background: allProcessed ? "#059669" : "#3B82F6",
                width: `${(processedCount / Math.max(receipt.items.length, 1)) * 100}%`,
                transition: "width 0.3s",
              }}
            />
          </div>
        </div>
      )}

      {/* ── Items table ────────────────────────────────────────────────── */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "hidden",
        }}
      >
        <table style={{ width: "100%", borderCollapse: "collapse", minWidth: 700 }}>
          <thead>
            <tr>
              <th style={{ ...thStyle, textAlign: "left", minWidth: 220 }}>{tDetail("headers.product")}</th>
              <th style={thStyle}>{tDetail("headers.barcode")}</th>
              <th style={thStyle}>{tDetail("headers.ordered")}</th>
              <th style={thStyle}>{tDetail("headers.received")}</th>
              <th style={thStyle}>{tDetail("headers.expiry")}</th>
              <th style={thStyle}>{tDetail("headers.batch")}</th>
              <th style={thStyle}>{tDetail("headers.price")}</th>
              <th style={{ ...thStyle, borderRight: "none" }}>{tDetail("headers.status")}</th>
            </tr>
          </thead>
          <tbody>
            {receipt.items.map((item) => {
              const edit = getEdit(item.id);
              const processed = edit.confirmed || item.isProcessed;
              const qtyReceived =
                edit.quantityReceived !== undefined
                  ? parseFloat(edit.quantityReceived)
                  : item.quantityReceived;
              const hasDiscrepancy =
                qtyReceived != null && qtyReceived < item.quantityOrdered;

              return (
                <tr
                  key={item.id}
                  style={{
                    background: processed
                      ? "rgba(5,150,105,0.04)"
                      : "transparent",
                    transition: "background 0.1s",
                  }}
                >
                  {/* Товар */}
                  <td
                    style={{
                      ...tdBase,
                      textAlign: "left",
                    }}
                  >
                    <div
                      style={{
                        color: "#E8EDF5",
                        fontSize: 13,
                        fontWeight: 500,
                        lineHeight: 1.4,
                      }}
                    >
                      {item.productName}
                    </div>
                    {item.discrepancyNotes && (
                      <div
                        style={{
                          color: "#FBBF24",
                          fontSize: 11,
                          marginTop: 2,
                        }}
                      >
                        ⚠ {item.discrepancyNotes}
                      </div>
                    )}
                  </td>

                  {/* Штрихкод */}
                  <td
                    style={{
                      ...tdBase,
                      fontFamily: "monospace",
                      fontSize: 11,
                      color: "#4B5563",
                    }}
                  >
                    {item.productBarcode ?? "—"}
                  </td>

                  {/* Замовлено */}
                  <td
                    style={{
                      ...tdBase,
                      fontFamily: "monospace",
                      color: "#9CA3AF",
                    }}
                  >
                    {item.quantityOrdered}
                  </td>

                  {/* Отримано */}
                  <td style={{ ...tdBase, width: 110 }}>
                    {isEditable && !item.isProcessed ? (
                      <input
                        type="number"
                        min="0"
                        step="any"
                        placeholder={String(item.quantityOrdered)}
                        value={edit.quantityReceived ?? ""}
                        onChange={(e) =>
                          updateEdit(item.id, { quantityReceived: e.target.value })
                        }
                        style={inputStyle}
                      />
                    ) : (
                      <span
                        style={{
                          fontFamily: "monospace",
                          color: hasDiscrepancy ? "#FBBF24" : "#4ADE80",
                          fontWeight: 600,
                        }}
                      >
                        {item.quantityReceived ?? item.quantityOrdered}
                      </span>
                    )}
                  </td>

                  {/* Термін */}
                  <td style={{ ...tdBase, width: 140 }}>
                    {isEditable && !item.isProcessed ? (
                      <input
                        type="date"
                        value={edit.expiryDate ?? formatDate(item.expiryDate)}
                        onChange={(e) =>
                          updateEdit(item.id, { expiryDate: e.target.value })
                        }
                        style={inputStyle}
                      />
                    ) : (
                      <span style={{ color: "#9CA3AF", fontSize: 13 }}>
                        {displayDate(item.expiryDate, intlLocale)}
                      </span>
                    )}
                  </td>

                  {/* Партія */}
                  <td style={{ ...tdBase, width: 130 }}>
                    {isEditable && !item.isProcessed ? (
                      <input
                        type="text"
                        placeholder={tDetail("batchNumberPlaceholder")}
                        value={edit.batchNumber ?? (item.batchNumber ?? "")}
                        onChange={(e) =>
                          updateEdit(item.id, { batchNumber: e.target.value })
                        }
                        style={inputStyle}
                      />
                    ) : (
                      <span
                        style={{
                          color: "#9CA3AF",
                          fontSize: 12,
                          fontFamily: "monospace",
                        }}
                      >
                        {item.batchNumber ?? "—"}
                      </span>
                    )}
                  </td>

                  {/* Ціна */}
                  <td
                    style={{
                      ...tdBase,
                      fontFamily: "monospace",
                      color: "#9CA3AF",
                    }}
                  >
                    {item.pricePurchase != null
                      ? `${item.pricePurchase.toLocaleString(intlLocale)} ₴`
                      : "—"}
                  </td>

                  {/* Статус / Дія */}
                  <td
                    style={{
                      ...tdBase,
                      borderRight: "none",
                      width: 90,
                    }}
                  >
                    {isEditable && !item.isProcessed ? (
                      <button
                        onClick={() => updateEdit(item.id, { confirmed: true })}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 4,
                          background: "#1a3a2e",
                          border: "1px solid #065F46",
                          borderRadius: 6,
                          color: "#4ADE80",
                          fontSize: 11,
                          fontWeight: 600,
                          padding: "5px 10px",
                          cursor: "pointer",
                          whiteSpace: "nowrap",
                        }}
                      >
                        <Check size={11} /> {tDetail("confirmItem")}
                      </button>
                    ) : (
                      <span
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 4,
                          color: "#4ADE80",
                          fontSize: 12,
                          fontWeight: 600,
                        }}
                      >
                        <CheckCircle2 size={14} /> {tDetail("done")}
                      </span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* ── Notes ──────────────────────────────────────────────────────── */}
      {receipt.notes && (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "14px 18px",
          }}
        >
          <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
            {t("drawer.notes")}
          </div>
          <div style={{ color: "#9CA3AF", fontSize: 13 }}>{receipt.notes}</div>
        </div>
      )}
    </div>
  );
}
