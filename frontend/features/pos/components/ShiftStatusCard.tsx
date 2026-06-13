"use client";

import { CreditCard, Clock, Hash, TrendingUp } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import type { ShiftDto, ShiftStatus } from "../types";

interface Props {
  shift: ShiftDto;
  onClose: () => void;
  isClosing: boolean;
}

function shiftStatusMeta(status: ShiftStatus): { label: string; color: string } {
  switch (status) {
    case "Open":
    case "Opening":
      return { label: status === "Open" ? "Відкрита" : "Відкривається…", color: "#22c55e" };
    case "Closed":
    case "Closing":
      return { label: status === "Closed" ? "Закрита" : "Закривається…", color: "#6B7280" };
    case "OpenFailed":
    case "CloseFailed":
      return { label: status === "OpenFailed" ? "Помилка відкриття" : "Помилка закриття", color: "#ef4444" };
  }
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

const FIELD_LABEL: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  marginBottom: 4,
};

const FIELD_VALUE: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 13,
  fontWeight: 500,
};

export function ShiftStatusCard({ shift, onClose, isClosing }: Props) {
  const meta = shiftStatusMeta(shift.status);
  const isOpen = shift.status === "Open";

  return (
    <div
      style={{
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: 20,
        display: "flex",
        gap: 24,
        flexWrap: "wrap",
        alignItems: "flex-start",
        justifyContent: "space-between",
      }}
    >
      {/* Left: shift details */}
      <div style={{ display: "flex", gap: 32, flexWrap: "wrap" }}>
        {/* Status */}
        <div>
          <div style={FIELD_LABEL}>Статус зміни</div>
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 6,
              background: `${meta.color}18`,
              border: `1px solid ${meta.color}40`,
              borderRadius: 6,
              padding: "4px 10px",
              color: meta.color,
              fontSize: 12,
              fontWeight: 700,
            }}
          >
            <span
              style={{ width: 7, height: 7, borderRadius: "50%", background: meta.color, flexShrink: 0 }}
            />
            {meta.label}
          </div>
        </div>

        {/* Opened at */}
        <div>
          <div style={{ ...FIELD_LABEL, display: "flex", alignItems: "center", gap: 4 }}>
            <Clock size={10} /> Відкрито
          </div>
          <div style={FIELD_VALUE}>{formatDateTime(shift.openedAt)}</div>
        </div>

        {/* Shift number */}
        {shift.shiftNumber != null && (
          <div>
            <div style={{ ...FIELD_LABEL, display: "flex", alignItems: "center", gap: 4 }}>
              <Hash size={10} /> Зміна №
            </div>
            <div style={FIELD_VALUE}>{shift.shiftNumber}</div>
          </div>
        )}

        {/* Total sales */}
        <div>
          <div style={{ ...FIELD_LABEL, display: "flex", alignItems: "center", gap: 4 }}>
            <TrendingUp size={10} /> Сума продажів
          </div>
          <div style={{ ...FIELD_VALUE, color: "#34d399", fontSize: 16, fontWeight: 700 }}>
            {shift.totalSales.toFixed(2)} ₴
          </div>
        </div>

        {/* Fiscal status */}
        <div>
          <div style={{ ...FIELD_LABEL, display: "flex", alignItems: "center", gap: 4 }}>
            <CreditCard size={10} /> ПРРО
          </div>
          <div style={{ ...FIELD_VALUE, color: "#9CA3AF", fontFamily: "monospace", fontSize: 12 }}>
            {shift.fiscalStatus || "—"}
          </div>
        </div>
      </div>

      {/* Right: close button */}
      {isOpen && (
        <Btn
          variant="danger"
          onClick={onClose}
          disabled={isClosing}
          icon={<CreditCard size={14} />}
        >
          {isClosing ? "Закривається…" : "Закрити зміну"}
        </Btn>
      )}
    </div>
  );
}
