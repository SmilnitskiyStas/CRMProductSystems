"use client";

import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Loader2 } from "lucide-react";
import type { PostCampaignImportColumnPreviewDto } from "../types";

interface Props {
  preview: PostCampaignImportColumnPreviewDto;
  selectedColumnIndex: number;
  onSelectColumn: (i: number) => void;
  onConfirm: () => void;
  isPending: boolean;
}

const selectStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
};

/**
 * Column auto-detect confirm/override (source doc §6.3): the backend already picked
 * `preview.detectedColumnIndex` (echoed back as the column that produced the CURRENT
 * `PostCampaignImportResultDto` counts, whether auto-detected or explicitly requested — see
 * `SegmentImportParser.ParseDelimitedRows`) and shows up to 10 sample rows across EVERY column so
 * the user can visually confirm or pick a different one. Picking a different column and clicking
 * "Підтвердити" re-calls the import endpoint with an explicit `columnIndex` — the backend treats
 * this as a brand-new import (a fresh segment, brief's documented v1 tradeoff — see
 * `ImportPanel.tsx`'s own remarks), so this is only shown as actionable while the selection
 * actually differs from what's already been imported.
 */
export function ColumnPreviewPicker({ preview, selectedColumnIndex, onSelectColumn, onConfirm, isPending }: Props) {
  const t = useTranslations("Dashboard.postCampaign.columnPreview");
  const changed = selectedColumnIndex !== preview.detectedColumnIndex;

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px", display: "flex", flexDirection: "column", gap: 10 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 10 }}>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 700 }}>{t("title")}</div>
          <p style={{ color: "#6B7280", fontSize: 12, margin: "2px 0 0" }}>
            {t("detected", { column: preview.detectedColumnHeader || t("unnamedColumn", { index: preview.detectedColumnIndex + 1 }) })}
          </p>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <label style={{ color: "#4B5563", fontSize: 12 }}>{t("columnLabel")}</label>
          <select value={selectedColumnIndex} onChange={(e) => onSelectColumn(Number(e.target.value))} style={selectStyle}>
            {preview.headers.map((h, i) => (
              <option key={i} value={i}>
                {h || t("unnamedColumn", { index: i + 1 })}
              </option>
            ))}
          </select>
          {changed && (
            <Btn
              variant="primary"
              size="sm"
              disabled={isPending}
              icon={isPending ? <Loader2 size={13} className="animate-spin" /> : undefined}
              onClick={onConfirm}
            >
              {isPending ? t("reimporting") : t("confirmButton")}
            </Btn>
          )}
        </div>
      </div>

      {preview.sampleRows.length > 0 && (
        <div style={{ overflowX: "auto", border: "1px solid #1F2937", borderRadius: 8 }}>
          <table style={{ borderCollapse: "collapse", width: "100%", minWidth: 320, fontSize: 12 }}>
            <thead>
              <tr style={{ background: "#0A1020" }}>
                {preview.headers.map((h, i) => (
                  <th
                    key={i}
                    style={{
                      padding: "6px 10px",
                      textAlign: "left",
                      color: i === selectedColumnIndex ? "#93C5FD" : "#6B7280",
                      fontWeight: 600,
                      whiteSpace: "nowrap",
                      borderBottom: "1px solid #1F2937",
                      background: i === selectedColumnIndex ? "#111827" : undefined,
                    }}
                  >
                    {h || t("unnamedColumn", { index: i + 1 })}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {preview.sampleRows.map((row, ri) => (
                <tr key={ri}>
                  {row.map((cell, ci) => (
                    <td
                      key={ci}
                      style={{
                        padding: "5px 10px",
                        color: ci === selectedColumnIndex ? "#E8EDF5" : "#6B7280",
                        whiteSpace: "nowrap",
                        borderBottom: "1px solid #0F1924",
                        background: ci === selectedColumnIndex ? "rgba(59,130,246,0.06)" : undefined,
                      }}
                    >
                      {cell ?? "—"}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
