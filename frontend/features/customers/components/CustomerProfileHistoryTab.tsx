"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { useCustomerProfileHistory } from "../hooks/useCustomers";

interface Props {
  customerId: string;
  /** True only while this tab is the active one — drives React Query's `enabled`, per the
   *  TASK-621b handoff's explicit lazy-load-on-open instruction (not on drawer mount). */
  active: boolean;
}

const PAGE_SIZE = 20;

const FIELD_NAME_KEY: Record<string, string> = {
  full_name: "fullName",
  email: "email",
  phone: "phone",
};

function formatDateTime(iso: string, intlLocale: string) {
  return new Date(iso).toLocaleString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * TASK-621 (§4 "Історія профілю" tab), backed by TASK-621b's
 * `GET /api/customers/{id}/profile-history`. Empty (`items: []`) is a normal 200 — not an
 * error — per that handoff's explicit note (a customer who never joined loyalty has no
 * consumer-side profile to show history for).
 */
export function CustomerProfileHistoryTab({ customerId, active }: Props) {
  const t = useTranslations("Dashboard.customers.profileHistory");
  const tFields = useTranslations("Dashboard.customers.profileHistory.fields");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [page, setPage] = useState(1);

  const { data, isLoading } = useCustomerProfileHistory(customerId, page, PAGE_SIZE, active);

  if (!active) return null;

  if (isLoading && !data) {
    return (
      <div style={{ color: "#374151", fontSize: 13, padding: "20px 0", textAlign: "center" }}>
        {t("loading")}
      </div>
    );
  }

  const items = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;

  if (items.length === 0) {
    return (
      <div style={{ color: "#374151", fontSize: 13, padding: "20px 0", textAlign: "center" }}>
        {t("empty")}
      </div>
    );
  }

  function fieldLabel(fieldName: string): string {
    const key = FIELD_NAME_KEY[fieldName];
    return key ? tFields(key) : fieldName;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      {items.map((change, i) => (
        <div
          key={i}
          style={{
            background: "#0A1020",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: "10px 14px",
          }}
        >
          <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 4 }}>
            <span style={{ color: "#93C5FD", fontSize: 12, fontWeight: 600 }}>
              {fieldLabel(change.fieldName)}
            </span>
            <span style={{ color: "#4B5563", fontSize: 11 }}>
              {formatDateTime(change.changedAt, intlLocale)}
            </span>
          </div>
          <div style={{ color: "#9CA3AF", fontSize: 13 }}>
            <span style={{ color: "#6B7280" }}>{change.oldValue ?? "—"}</span>
            {" → "}
            <span style={{ color: "#E8EDF5" }}>{change.newValue ?? "—"}</span>
          </div>
        </div>
      ))}

      {totalPages > 1 && (
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 4 }}>
          <button
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page === 1}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: page === 1 ? "#374151" : "#9CA3AF",
              fontSize: 12,
              padding: "5px 12px",
              cursor: page === 1 ? "not-allowed" : "pointer",
            }}
          >
            {t("prevButton")}
          </button>
          <span style={{ color: "#4B5563", fontSize: 12 }}>
            {page} / {totalPages}
          </span>
          <button
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page === totalPages}
            style={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: page === totalPages ? "#374151" : "#9CA3AF",
              fontSize: 12,
              padding: "5px 12px",
              cursor: page === totalPages ? "not-allowed" : "pointer",
            }}
          >
            {t("nextButton")}
          </button>
        </div>
      )}
    </div>
  );
}
