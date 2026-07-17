"use client";

import { Wallet, AlertTriangle, CheckCircle2 } from "lucide-react";
import { useTranslations } from "next-intl";
import type { ShiftDto } from "../types";

interface Props {
  shift: ShiftDto;
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
  fontSize: 15,
  fontWeight: 700,
};

/**
 * Cash-count reconciliation, shown only when the shift was closed with an
 * `actualClosingCash` count (TASK-357). Renders nothing for shifts closed the
 * old way (closingCash === null) — keeps the Z-report unchanged for those.
 */
export function CashReconciliationSummary({ shift }: Props) {
  const t = useTranslations("Dashboard.pos.cashReconciliation");
  if (shift.closingCash == null) return null;

  const discrepancy = shift.cashDiscrepancy ?? 0;
  const isMatch = discrepancy === 0;
  const isSurplus = discrepancy > 0;
  const isShortage = discrepancy < 0;

  const statusColor = isMatch ? "#22c55e" : isSurplus ? "#F59E0B" : "#ef4444";
  const statusLabel = isMatch
    ? t("match")
    : isSurplus
    ? t("surplus", { amount: discrepancy.toFixed(2) })
    : t("shortage", { amount: discrepancy.toFixed(2) });

  return (
    <div
      style={{
        marginTop: 16,
        paddingTop: 16,
        borderTop: "1px solid #1F2937",
      }}
    >
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 8,
          marginBottom: 14,
        }}
      >
        <Wallet size={15} color="#9CA3AF" />
        <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700 }}>
          {t("title")}
        </span>
      </div>

      <div style={{ display: "flex", gap: 40, flexWrap: "wrap", alignItems: "flex-start" }}>
        <div>
          <div style={FIELD_LABEL}>{t("openingCash")}</div>
          <div style={FIELD_VALUE}>{(shift.openingCash ?? 0).toFixed(2)} ₴</div>
        </div>

        <div>
          <div style={FIELD_LABEL}>{t("expectedAmount")}</div>
          <div style={FIELD_VALUE}>{(shift.expectedCashAmount ?? 0).toFixed(2)} ₴</div>
        </div>

        <div>
          <div style={FIELD_LABEL}>{t("actualCash")}</div>
          <div style={FIELD_VALUE}>{shift.closingCash.toFixed(2)} ₴</div>
        </div>

        <div>
          <div style={FIELD_LABEL}>{t("discrepancy")}</div>
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 6,
              background: `${statusColor}18`,
              border: `1px solid ${statusColor}40`,
              borderRadius: 6,
              padding: "4px 10px",
              color: statusColor,
              fontSize: 13,
              fontWeight: 700,
            }}
          >
            {isMatch ? <CheckCircle2 size={13} /> : <AlertTriangle size={13} />}
            {statusLabel}
          </div>
        </div>
      </div>
    </div>
  );
}
