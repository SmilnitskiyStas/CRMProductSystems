"use client";

import { useTranslations } from "next-intl";
import { useRegionLabel } from "@/features/geo/hooks/useRegions";
import type { DeliveryCoverage } from "../types";

interface Props {
  coverage: DeliveryCoverage | null | undefined;
}

/**
 * Supplier-declared delivery coverage on the profile page. Always visible (NOT
 * premium-gated) — "does this supplier deliver to my region" is decisive for a buyer.
 */
export function SupplierCoveragePanel({ coverage }: Props) {
  const t = useTranslations("Dashboard.marketplace.coverage");
  const regionLabel = useRegionLabel();

  const served = coverage?.served ?? [];
  const notServed = coverage?.notServed ?? [];
  const note = coverage?.note?.trim() ? coverage.note : null;
  const isEmpty = served.length === 0 && notServed.length === 0 && !note;

  return (
    <div style={{ marginBottom: 28 }}>
      <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600, margin: "0 0 14px" }}>
        {t("title")}
      </h2>

      {isEmpty ? (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("empty")}</div>
      ) : (
        <div
          style={{
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            padding: "16px 20px",
            display: "flex",
            flexDirection: "column",
            gap: 12,
          }}
        >
          {served.length > 0 && (
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              {served.map((entry) => (
                <div
                  key={entry.regionCode}
                  style={{
                    display: "flex",
                    gap: 8,
                    alignItems: "baseline",
                    flexWrap: "wrap",
                  }}
                >
                  <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                    {regionLabel(entry.regionCode)}
                  </span>
                  <span style={{ color: "#6B7280", fontSize: 12 }}>
                    {entry.terms?.trim() ? entry.terms : t("termsByAgreement")}
                  </span>
                </div>
              ))}
            </div>
          )}

          {notServed.length > 0 && (
            <div style={{ color: "#6B7280", fontSize: 12 }}>
              {t("notServed", {
                regions: notServed.map((code) => regionLabel(code)).join(", "),
              })}
            </div>
          )}

          {note && (
            <div style={{ color: "#9CA3AF", fontSize: 12, whiteSpace: "pre-wrap" }}>
              {note}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
