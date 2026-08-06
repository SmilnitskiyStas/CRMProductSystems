"use client";

import { useTranslations, useLocale } from "next-intl";
import { RFM_AXIS, rfmLabelOf, rfmRankOf } from "../types";
import type { PostCampaignMigrationCellDto, RfmSegmentKey } from "../types";

interface Props {
  matrix: PostCampaignMigrationCellDto[];
  isLoading: boolean;
}

function cellKey(k: RfmSegmentKey | null): string {
  return k ?? "none";
}

/**
 * RFM transition matrix (source doc §27.1): rows = before, columns = after, diagonal = no change,
 * green = improvement, red = decline, a genuinely empty combination shows a dot. The API only
 * returns NON-ZERO cells (`PostCampaignMigrationDto.matrix` is built from a sparse server-side
 * dictionary), so this component renders the FULL fixed 12×12 axis (`RFM_AXIS` — 11 real segments
 * in priority order + the "Без покупок" bucket) itself and fills in 0/dot for every combination the
 * response doesn't mention — same "always show the complete fixed set, not just what's non-zero"
 * precedent `SegmentGrid.tsx` already established for the 11-segment overview grid. Horizontally
 * scrollable with a sticky first column — 12 columns don't fit most viewports at a readable size.
 */
export function TransitionMatrix({ matrix, isLoading }: Props) {
  const t = useTranslations("Dashboard.postCampaign.transitionMatrix");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const counts = new Map<string, number>();
  for (const cell of matrix) {
    counts.set(`${cellKey(cell.before)}|${cellKey(cell.after)}`, cell.count);
  }

  function countOf(before: RfmSegmentKey | null, after: RfmSegmentKey | null): number {
    return counts.get(`${cellKey(before)}|${cellKey(after)}`) ?? 0;
  }

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>{t("title")}</div>
      <div style={{ color: "#6B7280", fontSize: 11.5, marginBottom: 12 }}>{t("subtitle")}</div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0", textAlign: "center" }}>{tCommon("loading")}</div>
      ) : (
        <>
          <div style={{ overflowX: "auto", border: "1px solid #1F2937", borderRadius: 8 }}>
            <table style={{ borderCollapse: "collapse", fontSize: 11.5 }}>
              <thead>
                <tr>
                  <th
                    style={{
                      position: "sticky",
                      left: 0,
                      zIndex: 2,
                      background: "#0A1020",
                      padding: "6px 10px",
                      borderBottom: "1px solid #1F2937",
                      borderRight: "1px solid #1F2937",
                      color: "#4B5563",
                      fontWeight: 600,
                      textAlign: "left",
                      minWidth: 140,
                    }}
                  >
                    {t("cornerLabel")}
                  </th>
                  {RFM_AXIS.map((after) => (
                    <th
                      key={cellKey(after)}
                      style={{
                        padding: "6px 6px",
                        borderBottom: "1px solid #1F2937",
                        color: "#6B7280",
                        fontWeight: 600,
                        textAlign: "center",
                        minWidth: 50,
                        whiteSpace: "nowrap",
                        background: "#0A1020",
                      }}
                      title={rfmLabelOf(after)}
                    >
                      {rfmLabelOf(after)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {RFM_AXIS.map((before) => (
                  <tr key={cellKey(before)}>
                    <td
                      style={{
                        position: "sticky",
                        left: 0,
                        zIndex: 1,
                        background: "#0A1020",
                        padding: "6px 10px",
                        borderRight: "1px solid #1F2937",
                        borderBottom: "1px solid #0F1924",
                        color: before === null ? "#6B7280" : "#9CA3AF",
                        fontWeight: 600,
                        whiteSpace: "nowrap",
                      }}
                      title={rfmLabelOf(before)}
                    >
                      {rfmLabelOf(before)}
                    </td>
                    {RFM_AXIS.map((after) => {
                      const count = countOf(before, after);
                      const beforeRank = rfmRankOf(before);
                      const afterRank = rfmRankOf(after);
                      const diagonal = beforeRank === afterRank;
                      const improved = !diagonal && afterRank < beforeRank;

                      let bg = "transparent";
                      let color = "#374151";
                      if (count > 0) {
                        if (diagonal) {
                          bg = "#111827";
                          color = "#9CA3AF";
                        } else if (improved) {
                          bg = "rgba(74,222,128,0.14)";
                          color = "#4ADE80";
                        } else {
                          bg = "rgba(248,113,113,0.14)";
                          color = "#F87171";
                        }
                      }

                      return (
                        <td
                          key={cellKey(after)}
                          style={{
                            padding: "6px 6px",
                            textAlign: "center",
                            borderBottom: "1px solid #0F1924",
                            background: bg,
                            color,
                            fontFamily: "monospace",
                            fontWeight: count > 0 ? 700 : 400,
                          }}
                        >
                          {count > 0 ? count.toLocaleString(intlLocale) : "·"}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div style={{ display: "flex", flexWrap: "wrap", gap: 14, marginTop: 12 }}>
            <LegendItem swatch="#111827" border="#374151" label={t("legendStable")} />
            <LegendItem swatch="rgba(74,222,128,0.25)" border="#4ADE80" label={t("legendUp")} />
            <LegendItem swatch="rgba(248,113,113,0.25)" border="#F87171" label={t("legendDown")} />
            <span style={{ display: "flex", alignItems: "center", gap: 6, color: "#4B5563", fontSize: 11.5 }}>
              <span style={{ fontFamily: "monospace" }}>·</span> {t("legendEmpty")}
            </span>
          </div>
        </>
      )}
    </div>
  );
}

function LegendItem({ swatch, border, label }: { swatch: string; border: string; label: string }) {
  return (
    <span style={{ display: "flex", alignItems: "center", gap: 6, color: "#9CA3AF", fontSize: 11.5 }}>
      <span style={{ width: 12, height: 12, borderRadius: 3, background: swatch, border: `1px solid ${border}` }} />
      {label}
    </span>
  );
}
