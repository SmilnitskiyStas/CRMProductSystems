"use client";

import { useTranslations, useLocale } from "next-intl";
import { LineChart, Line, XAxis, YAxis, Tooltip, Legend, CartesianGrid, ResponsiveContainer } from "recharts";
import type { PostCampaignDailyTurnoverDto } from "../types";

interface Props {
  data: PostCampaignDailyTurnoverDto | undefined;
  isLoading: boolean;
}

function formatK(v: number): string {
  return Math.abs(v) >= 1000 ? `${(v / 1000).toLocaleString(undefined, { maximumFractionDigits: 0 })}k` : String(v);
}

/**
 * "Динаміка обороту: до vs після" (source doc §15) — two series aligned by ORDINAL day-in-window
 * (`dayIndex`, shared between before/after), not calendar date, exactly per the source doc's
 * requirement; the X-axis is still LABELED with the after-window's real calendar dates
 * (`afterCalendarDate`). "після" renders solid/teal, "до" dashed/gray, matching the source doc's
 * exact visual convention (§15: "суцільна бірюзова — після; сіра пунктирна — до"). Y-axis uses a
 * "k" shorthand for thousands (source doc's own requirement).
 */
export function DailyTurnoverChart({ data, isLoading }: Props) {
  const t = useTranslations("Dashboard.postCampaign.dailyTurnoverChart");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const points = (data?.points ?? []).map((p) => ({
    name: new Date(p.afterCalendarDate).toLocaleDateString(intlLocale, { day: "2-digit", month: "2-digit" }),
    before: p.beforeAmount,
    after: p.afterAmount,
  }));

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px 8px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>{t("title")}</div>
      <div style={{ color: "#6B7280", fontSize: 11.5, marginBottom: 12 }}>{t("subtitle")}</div>

      {isLoading || !data ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{tCommon("loading")}</div>
      ) : points.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("empty")}</div>
      ) : (
        <ResponsiveContainer width="100%" height={260}>
          <LineChart data={points} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#161B26" vertical={false} />
            <XAxis dataKey="name" tick={{ fill: "#6B7280", fontSize: 10.5 }} axisLine={false} tickLine={false} interval="preserveStartEnd" />
            <YAxis tick={{ fill: "#4B5563", fontSize: 11 }} axisLine={false} tickLine={false} width={40} tickFormatter={formatK} />
            <Tooltip
              contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
              formatter={(value, name) => [
                `${Number(value).toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`,
                name === "after" ? t("afterSeries") : t("beforeSeries"),
              ]}
            />
            <Legend formatter={(value) => (value === "after" ? t("afterSeries") : t("beforeSeries"))} wrapperStyle={{ fontSize: 12, color: "#9CA3AF" }} />
            <Line type="monotone" dataKey="before" name="before" stroke="#6B7280" strokeWidth={1.5} strokeDasharray="5 4" dot={false} />
            <Line type="monotone" dataKey="after" name="after" stroke="#2DD4BF" strokeWidth={2.5} dot={false} />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
