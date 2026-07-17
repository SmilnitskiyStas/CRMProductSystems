"use client";

import { useRouter } from "next/navigation";
import { BarChart2, AlertTriangle } from "lucide-react";
import { useTranslations } from "next-intl";
import type { DailySale } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";

interface Props {
  sales: DailySale[];
  onToggleAnomaly: (id: string, isAnomaly: boolean) => void;
}

const th: React.CSSProperties = {
  textAlign: "left",
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.5,
  padding: "10px 12px",
  borderBottom: "1px solid #1F2937",
  whiteSpace: "nowrap",
};

const td: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 13,
  padding: "10px 12px",
  borderBottom: "1px solid #161B22",
};

export function SalesTable({ sales, onToggleAnomaly }: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.sales.table");

  const sourceLabels: Record<string, string> = {
    manual: t("source.manual"),
    pos: t("source.pos"),
    import: t("source.import"),
  };

  if (sales.length === 0) {
    return (
      <div style={{ color: "#4B5563", fontSize: 14, padding: "48px 0", textAlign: "center" }}>
        {t("empty")}
      </div>
    );
  }

  return (
    <table style={{ width: "100%", borderCollapse: "collapse" }}>
      <thead>
        <tr>
          <th style={th}>{t("headers.date")}</th>
          <th style={th}>{t("headers.product")}</th>
          <th style={th}>{t("headers.barcode")}</th>
          <th style={th}>{t("headers.store")}</th>
          <th style={{ ...th, textAlign: "right" }}>{t("headers.sold")}</th>
          <th style={{ ...th, textAlign: "right" }}>{t("headers.eod")}</th>
          <th style={th}>{t("headers.source")}</th>
          <th style={th}>{t("headers.marks")}</th>
          <th style={th}></th>
        </tr>
      </thead>
      <tbody>
        {sales.map((s) => (
          <tr key={s.id} style={s.isAnomaly ? { opacity: 0.45 } : undefined}>
            <td style={{ ...td, fontFamily: "monospace" }}>{s.date}</td>
            <td style={td}>{s.productName}</td>
            <td style={{ ...td, color: "#6B7280", fontFamily: "monospace", fontSize: 12 }}>
              {s.barcode ?? "—"}
            </td>
            <td style={{ ...td, color: "#9CA3AF" }}>{s.storeName}</td>
            <td style={{ ...td, textAlign: "right", fontWeight: 600 }}>{s.quantitySold}</td>
            <td style={{ ...td, textAlign: "right", color: "#9CA3AF" }}>
              {s.quantityEndOfDay ?? "—"}
            </td>
            <td style={{ ...td, color: "#6B7280", fontSize: 12 }}>
              {sourceLabels[s.source] ?? s.source}
            </td>
            <td style={td}>
              {s.isPromoDay && (
                <span style={{
                  background: "#7C2D12", color: "#FDBA74", fontSize: 11,
                  borderRadius: 6, padding: "2px 8px", marginRight: 6,
                }}>{t("promoTag")}</span>
              )}
              {s.isAnomaly && (
                <span style={{
                  background: "#7F1D1D", color: "#FCA5A5", fontSize: 11,
                  borderRadius: 6, padding: "2px 8px",
                }}>{t("anomalyTag")}</span>
              )}
            </td>
            <td style={{ ...td, textAlign: "right" }}>
              <ActionMenu
                items={[
                  {
                    label: s.isAnomaly ? t("actionMenu.includeInAdu") : t("actionMenu.markAnomaly"),
                    icon: <AlertTriangle size={13} />,
                    onClick: () => onToggleAnomaly(s.id, !s.isAnomaly),
                  },
                  { separator: true },
                  {
                    label: t("actionMenu.productAnalytics"),
                    icon: <BarChart2 size={13} />,
                    onClick: () => router.push(`/inventory/${s.productId}?tab=analytics`),
                    disabled: !s.productId,
                  },
                ]}
              />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
