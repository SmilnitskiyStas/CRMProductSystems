"use client";

import { Award } from "lucide-react";
import { useTranslations } from "next-intl";
import type { CustomerDetail } from "../types";

interface Props {
  detail: CustomerDetail;
}

/**
 * TASK-621 (§4 "Лояльність" tab). Renders the three distinct null-states documented in
 * `.claude/logs/handoffs/618-to-frontend_backend-developer.md` — these are NOT collapsible into
 * a single "empty" check, each needs its own message:
 *   1. Never joined loyalty at all → currentTierName/compositeScore/tierProgressPercent all null.
 *   2. Joined but no tier assigned yet (new member / nightly recompute hasn't run) →
 *      compositeScore is a real number, currentTierName/tierProgressPercent both null.
 *   3. Already at the top tier → tierProgressPercent null while currentTierName/compositeScore
 *      are populated (no "next tier" to progress toward).
 */
export function CustomerTierCard({ detail }: Props) {
  const t = useTranslations("Dashboard.customers.tier");

  const notEnrolled = detail.currentTierName === null && detail.compositeScore === null;
  const noTierYet = detail.currentTierName === null && detail.compositeScore !== null;
  const atTopTier = detail.currentTierName !== null && detail.tierProgressPercent === null;
  const hasProgress = detail.currentTierName !== null && detail.tierProgressPercent !== null;

  if (notEnrolled) {
    return (
      <div style={cardStyle}>
        <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "20px 0" }}>
          {t("notEnrolled")}
        </div>
      </div>
    );
  }

  return (
    <div style={cardStyle}>
      {/* Badge row */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 14 }}>
        {detail.currentTierName ? (
          <span
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 6,
              background: "#1D3461",
              border: "1px solid #3B82F6",
              color: "#93C5FD",
              borderRadius: 20,
              padding: "4px 12px",
              fontSize: 13,
              fontWeight: 700,
            }}
          >
            <Award size={14} />
            {detail.currentTierName}
          </span>
        ) : (
          <span style={{ color: "#9CA3AF", fontSize: 13, fontStyle: "italic" }}>
            {t("noTierYetBadge")}
          </span>
        )}
      </div>

      {/* Score */}
      <InfoField label={t("scoreLabel")} value={String(detail.compositeScore)} />

      {/* State-specific note / progress bar */}
      <div style={{ marginTop: 14 }}>
        {noTierYet && (
          <div style={{ color: "#6B7280", fontSize: 12 }}>{t("noTierYetNote")}</div>
        )}
        {atTopTier && (
          <div style={{ color: "#FCD34D", fontSize: 12, fontWeight: 500 }}>{t("topTierNote")}</div>
        )}
        {hasProgress && (
          <>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                marginBottom: 6,
                color: "#6B7280",
                fontSize: 11,
                fontWeight: 500,
                textTransform: "uppercase",
                letterSpacing: "0.05em",
              }}
            >
              <span>{t("progressLabel")}</span>
              <span>{detail.tierProgressPercent}%</span>
            </div>
            <div
              style={{
                height: 8,
                borderRadius: 4,
                background: "#1F2937",
                overflow: "hidden",
              }}
            >
              <div
                style={{
                  width: `${Math.min(100, Math.max(0, detail.tierProgressPercent ?? 0))}%`,
                  height: "100%",
                  background: "#3B82F6",
                  borderRadius: 4,
                }}
              />
            </div>
          </>
        )}
      </div>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  background: "#0A1020",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: 16,
};

function InfoField({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div
        style={{
          color: "#6B7280",
          fontSize: 11,
          fontWeight: 500,
          textTransform: "uppercase",
          letterSpacing: "0.05em",
          marginBottom: 3,
        }}
      >
        {label}
      </div>
      <div style={{ color: "#E8EDF5", fontSize: 13 }}>{value}</div>
    </div>
  );
}
