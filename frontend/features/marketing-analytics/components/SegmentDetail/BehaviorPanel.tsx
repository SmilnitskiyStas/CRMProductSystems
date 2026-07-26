"use client";

import { useTranslations, useLocale } from "next-intl";
import { BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer } from "recharts";
import { Clock, CalendarDays } from "lucide-react";
import type { RfmBehaviorDto } from "../../types";

interface Props {
  behavior: RfmBehaviorDto | undefined;
  isLoading: boolean;
}

// dayOfWeekIso: 1=Mon..7=Sun (ISO) — "frontend owns the label mapping" per the backend contract.
const DAY_KEYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

function StatRow({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "flex", justifyContent: "space-between", padding: "6px 0", borderBottom: "1px solid #161B26" }}>
      <span style={{ color: "#6B7280", fontSize: 12 }}>{label}</span>
      <span style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 600, fontFamily: "monospace" }}>{value}</span>
    </div>
  );
}

export function BehaviorPanel({ behavior, isLoading }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.behavior");
  const tDay = useTranslations("Dashboard.marketingAnalytics.behavior.days");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (isLoading || !behavior) {
    return (
      <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "20px 16px" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 12 }}>{t("title")}</div>
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
      </div>
    );
  }

  const dayChartData = behavior.byDayOfWeek.map((d) => ({
    name: tDay(DAY_KEYS[d.dayOfWeekIso - 1] ?? "mon"),
    share: d.sharePercent,
  }));
  const hourChartData = behavior.byHour.map((h) => ({
    name: `${String(h.hour).padStart(2, "0")}`,
    share: h.sharePercent,
  }));

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px 20px", display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>

      {/* Peak day/hour highlight */}
      <div style={{ display: "flex", gap: 10 }}>
        <div style={{ flex: 1, background: "#111827", borderRadius: 8, padding: "10px 12px", display: "flex", alignItems: "center", gap: 8 }}>
          <CalendarDays size={15} color="#60A5FA" />
          <div>
            <div style={{ color: "#4B5563", fontSize: 10, textTransform: "uppercase" }}>{t("peakDay")}</div>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
              {behavior.peakDayOfWeekIso ? tDay(DAY_KEYS[behavior.peakDayOfWeekIso - 1] ?? "mon") : "—"}
            </div>
          </div>
        </div>
        <div style={{ flex: 1, background: "#111827", borderRadius: 8, padding: "10px 12px", display: "flex", alignItems: "center", gap: 8 }}>
          <Clock size={15} color="#60A5FA" />
          <div>
            <div style={{ color: "#4B5563", fontSize: 10, textTransform: "uppercase" }}>{t("peakHour")}</div>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
              {behavior.peakHour !== null ? `${String(behavior.peakHour).padStart(2, "0")}:00` : "—"}
            </div>
          </div>
        </div>
      </div>

      {/* Day-of-week distribution */}
      {dayChartData.length > 0 && (
        <div>
          <div style={{ color: "#6B7280", fontSize: 11, marginBottom: 6, textTransform: "uppercase", letterSpacing: "0.04em" }}>
            {t("byDayOfWeek")}
          </div>
          <ResponsiveContainer width="100%" height={120}>
            <BarChart data={dayChartData} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
              <XAxis dataKey="name" tick={{ fill: "#6B7280", fontSize: 11 }} axisLine={false} tickLine={false} />
              <YAxis hide />
              <Tooltip
                contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
                formatter={(v) => [`${Number(v).toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`, t("shareOfReceipts")]}
                cursor={{ fill: "rgba(255,255,255,0.03)" }}
              />
              <Bar dataKey="share" fill="#3B82F6" radius={[4, 4, 0, 0]} maxBarSize={28} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Hourly distribution */}
      {hourChartData.length > 0 && (
        <div>
          <div style={{ color: "#6B7280", fontSize: 11, marginBottom: 6, textTransform: "uppercase", letterSpacing: "0.04em" }}>
            {t("byHour")}
          </div>
          <ResponsiveContainer width="100%" height={110}>
            <BarChart data={hourChartData} margin={{ left: 0, right: 8, top: 4, bottom: 0 }}>
              <XAxis dataKey="name" tick={{ fill: "#4B5563", fontSize: 9 }} axisLine={false} tickLine={false} interval={1} />
              <YAxis hide />
              <Tooltip
                contentStyle={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, color: "#E8EDF5", fontSize: 12 }}
                formatter={(v) => [`${Number(v).toLocaleString(intlLocale, { maximumFractionDigits: 1 })}%`, t("shareOfReceipts")]}
                cursor={{ fill: "rgba(255,255,255,0.03)" }}
              />
              <Bar dataKey="share" fill="#2DD4BF" radius={[3, 3, 0, 0]} maxBarSize={14} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Windowed stats (respect the active RFM period) */}
      <div>
        <StatRow label={t("averageTicket")} value={`${behavior.averageTicket.toLocaleString(intlLocale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴`} />
        <StatRow label={t("receiptCount")} value={behavior.receiptCount.toLocaleString(intlLocale)} />
        <StatRow label={t("receiptsPerCustomer")} value={behavior.receiptsPerCustomer.toLocaleString(intlLocale, { maximumFractionDigits: 1 })} />
        <StatRow label={t("lastVisit")} value={behavior.lastVisit ?? "—"} />
        <StatRow label={t("averageRecencyDays")} value={t("daysSuffix", { n: behavior.averageRecencyDays.toLocaleString(intlLocale, { maximumFractionDigits: 1 }) })} />
      </div>

      {/* LTV — ALWAYS all-time, never conflated with the windowed stats above (mandatory per RFM_ANALYSIS.md §13.1/§18). */}
      <div style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, padding: "10px 12px" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 6 }}>
          <span style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.04em" }}>{t("ltvTitle")}</span>
          <span style={{ background: "#052e16", color: "#4ADE80", fontSize: 9, fontWeight: 700, padding: "1px 6px", borderRadius: 4 }}>
            {t("allTimeBadge")}
          </span>
        </div>
        <StatRow label={t("averageLtv")} value={`${behavior.averageLtv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`} />
        <StatRow label={t("totalLtv")} value={`${behavior.totalLtv.toLocaleString(intlLocale, { maximumFractionDigits: 0 })} ₴`} />
      </div>
    </div>
  );
}
