"use client";

import {
  PieChart,
  Pie,
  Cell,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import { useTranslations, useLocale } from "next-intl";
import type { PosAnalyticsSummaryDto } from "../types";

interface Props {
  data: PosAnalyticsSummaryDto;
}

const COLORS = {
  cash: "#4ADE80",
  card: "#3B82F6",
};

export function PosPaymentPieChart({ data }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.paymentPie");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const total = data.cashRevenue + data.cardRevenue;

  if (total === 0) {
    return (
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 10,
          padding: "20px 16px",
          color: "#4B5563",
          fontSize: 13,
          textAlign: "center",
        }}
      >
        {t("empty")}
      </div>
    );
  }

  const chartData = [
    { name: t("cash"), value: data.cashRevenue },
    { name: t("card"), value: data.cardRevenue },
  ];

  const cashPct = total > 0 ? ((data.cashRevenue / total) * 100).toFixed(1) : "0";
  const cardPct = total > 0 ? ((data.cardRevenue / total) * 100).toFixed(1) : "0";

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 16px",
      }}
    >
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 8 }}>
        {t("title")}
      </div>
      <ResponsiveContainer width="100%" height={200}>
        <PieChart>
          <Pie
            data={chartData}
            cx="50%"
            cy="50%"
            innerRadius={55}
            outerRadius={80}
            paddingAngle={3}
            dataKey="value"
          >
            <Cell fill={COLORS.cash} />
            <Cell fill={COLORS.card} />
          </Pie>
          <Tooltip
            contentStyle={{
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
            }}
            formatter={(val) => [`${Number(val).toLocaleString(intlLocale)} ₴`, ""]}
          />
          <Legend
            iconType="circle"
            iconSize={8}
            formatter={(val) => (
              <span style={{ color: "#9CA3AF", fontSize: 12 }}>{val}</span>
            )}
          />
        </PieChart>
      </ResponsiveContainer>
      <div style={{ display: "flex", justifyContent: "center", gap: 24, marginTop: 4 }}>
        <div style={{ textAlign: "center" }}>
          <div style={{ color: COLORS.cash, fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
            {cashPct}%
          </div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>{t("cash")}</div>
        </div>
        <div style={{ textAlign: "center" }}>
          <div style={{ color: COLORS.card, fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
            {cardPct}%
          </div>
          <div style={{ color: "#4B5563", fontSize: 11 }}>{t("card")}</div>
        </div>
      </div>
    </div>
  );
}
