"use client";

import { useTranslations, useLocale } from "next-intl";
import type { PosCashierStatsDto } from "../types";

interface Props {
  data: PosCashierStatsDto;
}

const ROW_BORDER = "1px solid #1F2937";

const baseTd: React.CSSProperties = {
  padding: "10px 16px",
  fontSize: 13,
  borderBottom: ROW_BORDER,
  borderRight: "1px solid #1F2937",
};

const tdText: React.CSSProperties = { ...baseTd, color: "#E8EDF5", fontWeight: 500 };
const tdNum: React.CSSProperties = { ...baseTd, color: "#9CA3AF", fontFamily: "monospace", textAlign: "right" };
const tdRevenue: React.CSSProperties = { ...tdNum, color: "#60A5FA" };

function thStyle(): React.CSSProperties {
  return {
    padding: "10px 16px",
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    borderBottom: "1px solid #374151",
    borderRight: "1px solid #374151",
    background: "#0A0F1A",
    textAlign: "left",
  };
}

export function PosCashierStatsTable({ data }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.cashiers");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  if (!data || data.cashiers.length === 0) {
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

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
      <div style={{ padding: "16px 16px 12px", borderBottom: "1px solid #1F2937" }}>
        <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>{t("title")}</div>
      </div>
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse" }}>
          <thead>
            <tr>
              <th style={thStyle()}>{t("headers.cashier")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.revenue")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.receipts")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.averageTicket")}</th>
              <th style={{ ...thStyle(), textAlign: "right" }}>{t("headers.shifts")}</th>
            </tr>
          </thead>
          <tbody>
            {data.cashiers.map((c) => (
              <tr
                key={c.cashierId}
                style={{ transition: "background 0.1s" }}
                onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.background = "#111827")}
                onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.background = "transparent")}
              >
                <td style={tdText}>{c.cashierName}</td>
                <td style={tdRevenue}>{c.totalRevenue.toLocaleString(intlLocale)} ₴</td>
                <td style={tdNum}>{c.transactionCount.toLocaleString(intlLocale)}</td>
                <td style={tdNum}>
                  {c.averageTicket.toLocaleString(intlLocale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₴
                </td>
                <td style={tdNum}>{c.shiftCount.toLocaleString(intlLocale)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
