"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useExportStoreMigration } from "../../hooks/useMarketingAnalytics";
import type { StoreMigrationCustomerRowDto } from "../../types";

interface Props {
  rows: StoreMigrationCustomerRowDto[];
  isLoading: boolean;
  /** Screen fetch limit, echoed back to show a "showing first N" note when the list is capped. */
  limit: number;
  /** Resolved concrete date range (from the already-loaded overview's periodFrom/periodTo) +
   * the active store selection — same "server's own resolved range, not re-derived
   * client-side" convention as ExportButtons.tsx's ExportBaseContext. */
  exportContext: { storeIds: string[]; from: string; to: string };
  /** Mirrors the backend's `MarketingAnalyticsAuthorization.CanExportPii` gate — a caller who
   * lacks it just silently gets a masked file, so the toggle is hidden entirely below that
   * rank rather than shown-but-ignored. */
  canExportPii: boolean;
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * Drill-down customer list for the store-migration section (TASK-503). PII (phone/email) is
 * ALWAYS masked here — the `/store-migration/customers` GET has no unmask param at all (per
 * the TASK-502 handoff note). The unmask capability only ever applies to the Excel export
 * below, so the "show full phone/email" toggle lives next to the export button, not on the
 * table itself.
 */
export function StoreMigrationCustomerTable({ rows, isLoading, limit, exportContext, canExportPii }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.storeMigration.customerTable");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [unmaskPii, setUnmaskPii] = useState(false);
  const { mutate: exportMigration, isPending } = useExportStoreMigration();

  function handleExport() {
    exportMigration(
      {
        storeIds: exportContext.storeIds.length > 0 ? exportContext.storeIds : null,
        from: exportContext.from,
        to: exportContext.to,
        unmaskPii,
      },
      {
        onSuccess: () => toast.success(t("successToast")),
        onError: (e) => toast.error(t("errorToast", { message: errorMessage(e) })),
      },
    );
  }

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 10, marginBottom: 12 }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          {canExportPii && (
            <label style={{ display: "flex", alignItems: "center", gap: 7, cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={unmaskPii}
                onChange={(e) => setUnmaskPii(e.target.checked)}
                style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
              />
              <span style={{ color: "#9CA3AF", fontSize: 12 }}>{t("unmaskPiiLabel")}</span>
            </label>
          )}
          <Btn
            variant="ghost"
            size="sm"
            disabled={isPending}
            icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
            onClick={handleExport}
          >
            {isPending ? t("downloading") : t("exportButton")}
          </Btn>
        </div>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0", textAlign: "center" }}>{t("loading")}</div>
      ) : rows.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0", textAlign: "center" }}>{t("empty")}</div>
      ) : (
        <>
          <div style={{ overflowX: "auto" }}>
            <table style={{ borderCollapse: "collapse", width: "100%", fontSize: 12.5 }}>
              <thead>
                <tr>
                  {[
                    t("headerName"),
                    t("headerPhone"),
                    t("headerEmail"),
                    t("headerFrom"),
                    t("headerTo"),
                    t("headerChecks"),
                    t("headerRevenue"),
                  ].map((h, i) => (
                    <th
                      key={h}
                      style={{
                        padding: "6px 10px",
                        borderBottom: "1px solid #1F2937",
                        color: "#6B7280",
                        fontWeight: 600,
                        textAlign: i >= 5 ? "right" : "left",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.customerId}>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#E8EDF5" }}>{row.name}</td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#9CA3AF", fontFamily: "monospace", whiteSpace: "nowrap" }}>
                      {row.phone ?? "—"}
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#9CA3AF", whiteSpace: "nowrap" }}>{row.email ?? "—"}</td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#9CA3AF", whiteSpace: "nowrap" }}>
                      {row.fromStoreName} <span style={{ color: "#4B5563" }}>({row.fromDate})</span>
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#9CA3AF", whiteSpace: "nowrap" }}>
                      {row.toStoreName} <span style={{ color: "#4B5563" }}>({row.toDate})</span>
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#E8EDF5", fontFamily: "monospace", textAlign: "right" }}>
                      {row.transactionCountInPeriod.toLocaleString(intlLocale)}
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #0F1924", color: "#4ADE80", fontFamily: "monospace", textAlign: "right" }}>
                      {row.revenueInPeriod.toLocaleString(intlLocale, { maximumFractionDigits: 0 })}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {rows.length >= limit && (
            <div style={{ color: "#4B5563", fontSize: 11.5, marginTop: 10 }}>{t("truncatedNote", { limit })}</div>
          )}
        </>
      )}
    </div>
  );
}
