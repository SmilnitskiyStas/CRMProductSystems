"use client";

import { useTranslations, useLocale } from "next-intl";
import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer } from "recharts";
import { RFM_SEGMENT_COLOR_GROUP, RFM_COLOR_PALETTE, RFM_CANNOT_LOSE_THEM_ACCENT, RFM_NO_PURCHASE_COLOR } from "../types";
import type { RfmColorTokens, PostCampaignSegmentDistributionItemDto } from "../types";

interface Props {
  title: string;
  distribution: PostCampaignSegmentDistributionItemDto[];
  isLoading: boolean;
}

/** Literal reuse of `SegmentGrid.tsx`'s exact color-resolution logic (brief's explicit instruction
 * — "reuse SegmentGrid's color language ... don't invent new ones") rather than copy-pasting the
 * lookup table a second time. */
function colorFor(segment: PostCampaignSegmentDistributionItemDto["segment"]): RfmColorTokens {
  if (segment === null) return RFM_NO_PURCHASE_COLOR;
  const base = RFM_COLOR_PALETTE[RFM_SEGMENT_COLOR_GROUP[segment]];
  return segment === "CannotLoseThem" ? { ...base, accent: RFM_CANNOT_LOSE_THEM_ACCENT } : base;
}

/**
 * One "Сегменти ДО"/"Сегменти ПІСЛЯ" donut (source doc §26) — the API only ever returns NON-ZERO
 * segments (`PostCampaignMigrationDto.beforeDistribution`/`afterDistribution` are built from a
 * sparse server-side dictionary), which already matches the source doc's own "нульові сегменти у
 * легенді можуть бути приховані" note — no client-side zero-padding needed here (unlike the
 * transition matrix below, which explicitly needs the full fixed axis for its dot-cell rule).
 */
export function MigrationDonut({ title, distribution, isLoading }: Props) {
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const total = distribution.reduce((sum, d) => sum + d.count, 0);

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px", flex: 1, minWidth: 260 }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 12 }}>{title}</div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0", textAlign: "center" }}>{tCommon("loading")}</div>
      ) : distribution.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0", textAlign: "center" }}>—</div>
      ) : (
        <div style={{ display: "flex", alignItems: "center", gap: 16, flexWrap: "wrap" }}>
          <div style={{ position: "relative", width: 130, height: 130, flexShrink: 0 }}>
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie data={distribution} dataKey="count" nameKey="labelUa" innerRadius={38} outerRadius={60} paddingAngle={distribution.length > 1 ? 2 : 0} stroke="none">
                  {distribution.map((d) => (
                    <Cell key={d.segment ?? "none"} fill={colorFor(d.segment).accent} />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
                  formatter={(value, name) => [Number(value).toLocaleString(intlLocale), name]}
                />
              </PieChart>
            </ResponsiveContainer>
            <div style={{ position: "absolute", inset: 0, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", pointerEvents: "none" }}>
              <span style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, fontFamily: "monospace" }}>{total.toLocaleString(intlLocale)}</span>
            </div>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: 5, flex: 1, minWidth: 130 }}>
            {distribution.map((d) => (
              <div key={d.segment ?? "none"} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 8 }}>
                <span style={{ display: "flex", alignItems: "center", gap: 6, minWidth: 0 }}>
                  <span style={{ width: 8, height: 8, borderRadius: "50%", background: colorFor(d.segment).accent, flexShrink: 0 }} />
                  <span style={{ color: "#9CA3AF", fontSize: 12, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{d.labelUa}</span>
                </span>
                <span style={{ color: "#E8EDF5", fontSize: 11.5, fontFamily: "monospace", flexShrink: 0 }}>
                  {d.count.toLocaleString(intlLocale)} <span style={{ color: "#4B5563" }}>({d.sharePercent.toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%)</span>
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
