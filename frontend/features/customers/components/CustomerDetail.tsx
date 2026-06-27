"use client";

import { useEffect } from "react";
import { useCustomer } from "../hooks/useCustomers";
import type { Customer, CustomerTransaction } from "../types";
import { Btn } from "@/components/ui/Btn";

const UAH = new Intl.NumberFormat("uk-UA", { style: "currency", currency: "UAH" });

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function PaymentTypeBadge({ type }: { type: string }) {
  const labels: Record<string, string> = {
    cash: "Готівка",
    card: "Картка",
    online: "Онлайн",
  };
  return (
    <span style={{ color: "#9CA3AF", fontSize: 12 }}>
      {labels[type] ?? type}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const configs: Record<string, { bg: string; color: string; border: string; label: string }> = {
    completed: { bg: "#0d2318", color: "#4ADE80", border: "#14532D", label: "Завершено" },
    pending:   { bg: "#1c1a0a", color: "#FCD34D", border: "#78350F", label: "Очікує" },
    cancelled: { bg: "#2d0a0a", color: "#F87171", border: "#7F1D1D", label: "Скасовано" },
    refunded:  { bg: "#0a1628", color: "#60A5FA", border: "#1D4ED8", label: "Повернення" },
  };
  const cfg = configs[status] ?? { bg: "#111827", color: "#6B7280", border: "#374151", label: status };
  return (
    <span
      style={{
        background: cfg.bg,
        border: `1px solid ${cfg.border}`,
        borderRadius: 20,
        padding: "2px 10px",
        color: cfg.color,
        fontSize: 11,
        fontWeight: 600,
        whiteSpace: "nowrap",
      }}
    >
      {cfg.label}
    </span>
  );
}

function TransactionRow({ tx }: { tx: CustomerTransaction }) {
  return (
    <div
      style={{
        display: "grid",
        gridTemplateColumns: "1fr 120px 100px 130px",
        padding: "10px 14px",
        borderBottom: "1px solid #0F1924",
        alignItems: "center",
      }}
    >
      <div style={{ color: "#6B7280", fontSize: 12 }}>{formatDateTime(tx.createdAt)}</div>
      <div style={{ color: "#4ADE80", fontSize: 13, fontWeight: 500 }}>{UAH.format(tx.totalAmount)}</div>
      <PaymentTypeBadge type={tx.paymentType} />
      <div><StatusBadge status={tx.status} /></div>
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────────

interface Props {
  customer: Customer;
  onClose: () => void;
  onEdit: () => void;
}

export function CustomerDetail({ customer, onClose, onEdit }: Props) {
  const { data: detail, isLoading } = useCustomer(customer.id);

  // Close on Escape
  useEffect(() => {
    function handler(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed",
          inset: 0,
          background: "rgba(0,0,0,0.5)",
          zIndex: 200,
        }}
      />

      {/* Drawer */}
      <div
        style={{
          position: "fixed",
          top: 0,
          right: 0,
          bottom: 0,
          width: "min(520px, 96vw)",
          background: "#0D1117",
          borderLeft: "1px solid #1F2937",
          zIndex: 201,
          display: "flex",
          flexDirection: "column",
          overflowY: "auto",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
            flexShrink: 0,
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {customer.name}
          </h2>
          <div style={{ display: "flex", gap: 8 }}>
            <Btn size="sm" onClick={onEdit}>
              Редагувати
            </Btn>
            <button
              onClick={onClose}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 8,
                padding: "5px 9px",
                color: "#4B5563",
                fontSize: 16,
                cursor: "pointer",
              }}
            >
              ✕
            </button>
          </div>
        </div>

        {/* Info card */}
        <div
          style={{
            margin: "18px 22px 0",
            background: "#0A1020",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: 16,
          }}
        >
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "1fr 1fr",
              gap: "14px 20px",
            }}
          >
            <InfoField label="Телефон"     value={customer.phone  ?? "—"} />
            <InfoField label="Email"       value={customer.email  ?? "—"} />
            <InfoField label="Замовлень"   value={String(customer.totalOrders)} />
            <InfoField label="Витрачено"   value={UAH.format(customer.totalSpent)} valueColor="#4ADE80" />
            <InfoField label="Зареєстрований" value={formatDate(customer.createdAt)} />
            {customer.notes && (
              <div style={{ gridColumn: "1 / -1" }}>
                <InfoField label="Нотатки" value={customer.notes} />
              </div>
            )}
          </div>

          {/* Tags */}
          {customer.tags.length > 0 && (
            <div style={{ marginTop: 12 }}>
              <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
                Теги
              </div>
              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {customer.tags.map((t) => (
                  <span
                    key={t}
                    style={{
                      background: "#0a1628",
                      border: "1px solid #1D4ED8",
                      borderRadius: 20,
                      padding: "2px 10px",
                      color: "#60A5FA",
                      fontSize: 12,
                    }}
                  >
                    {t}
                  </span>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Recent transactions */}
        <div style={{ margin: "18px 22px 22px" }}>
          <h3 style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", margin: "0 0 10px" }}>
            Останні транзакції
          </h3>

          {isLoading ? (
            <div style={{ color: "#374151", fontSize: 13, padding: "20px 0" }}>Завантаження…</div>
          ) : !detail || detail.recentTransactions.length === 0 ? (
            <div style={{ color: "#374151", fontSize: 13, padding: "20px 0" }}>Транзакцій ще немає</div>
          ) : (
            <div
              style={{
                background: "#0A1020",
                border: "1px solid #1F2937",
                borderRadius: 10,
                overflow: "hidden",
              }}
            >
              {/* Tx header */}
              <div
                style={{
                  display: "grid",
                  gridTemplateColumns: "1fr 120px 100px 130px",
                  padding: "8px 14px",
                  borderBottom: "1px solid #1F2937",
                  background: "#060D18",
                }}
              >
                {["Дата", "Сума", "Оплата", "Статус"].map((h) => (
                  <div key={h} style={{ color: "#374151", fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
                    {h}
                  </div>
                ))}
              </div>
              {detail.recentTransactions.map((tx) => (
                <TransactionRow key={tx.id} tx={tx} />
              ))}
            </div>
          )}
        </div>
      </div>
    </>
  );
}

// ── Helper ─────────────────────────────────────────────────────────────────────

function InfoField({
  label,
  value,
  valueColor,
}: {
  label: string;
  value: string;
  valueColor?: string;
}) {
  return (
    <div>
      <div style={{ color: "#6B7280", fontSize: 11, fontWeight: 500, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 3 }}>
        {label}
      </div>
      <div style={{ color: valueColor ?? "#E8EDF5", fontSize: 13 }}>
        {value}
      </div>
    </div>
  );
}
