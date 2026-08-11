"use client";

import { useMemo } from "react";
import { useTranslations, useLocale } from "next-intl";
import type { StoreMigrationFlowDto } from "../../types";

interface Props {
  flows: StoreMigrationFlowDto[];
}

interface AxisStore {
  id: string;
  name: string;
}

/**
 * From-store × to-store transition table (TASK-503). Visual style borrows from
 * `post-campaign/TransitionMatrix.tsx` (sticky first column, horizontally scrollable plain
 * table) but the axis is DYNAMIC — built from the stores that actually appear in `flows`
 * (union of `fromStoreId`/`toStoreId`), not the RFM matrix's fixed 12-segment axis and not
 * every store the tenant owns. The API only returns non-zero cells, so any combination not
 * present here renders as an empty "·" cell. No diagonal cells are possible by definition
 * (a "migration" requires fromStoreId !== toStoreId).
 */
export function StoreMigrationMatrix({ flows }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.storeMigration.matrix");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const axis = useMemo<AxisStore[]>(() => {
    const byId = new Map<string, string>();
    for (const f of flows) {
      byId.set(f.fromStoreId, f.fromStoreName);
      byId.set(f.toStoreId, f.toStoreName);
    }
    return [...byId.entries()]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name, intlLocale));
  }, [flows, intlLocale]);

  const cellByKey = useMemo(() => {
    const m = new Map<string, StoreMigrationFlowDto>();
    for (const f of flows) m.set(`${f.fromStoreId}|${f.toStoreId}`, f);
    return m;
  }, [flows]);

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "16px 16px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, marginBottom: 4 }}>{t("title")}</div>
      <div style={{ color: "#6B7280", fontSize: 11.5, marginBottom: 12 }}>{t("subtitle")}</div>

      {axis.length === 0 ? (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "20px 0", textAlign: "center" }}>{t("empty")}</div>
      ) : (
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
                {axis.map((s) => (
                  <th
                    key={s.id}
                    style={{
                      padding: "6px 8px",
                      borderBottom: "1px solid #1F2937",
                      color: "#6B7280",
                      fontWeight: 600,
                      textAlign: "center",
                      minWidth: 90,
                      whiteSpace: "nowrap",
                      background: "#0A1020",
                    }}
                    title={s.name}
                  >
                    {s.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {axis.map((from) => (
                <tr key={from.id}>
                  <td
                    style={{
                      position: "sticky",
                      left: 0,
                      zIndex: 1,
                      background: "#0A1020",
                      padding: "6px 10px",
                      borderRight: "1px solid #1F2937",
                      borderBottom: "1px solid #0F1924",
                      color: "#9CA3AF",
                      fontWeight: 600,
                      whiteSpace: "nowrap",
                    }}
                    title={from.name}
                  >
                    {from.name}
                  </td>
                  {axis.map((to) => {
                    const cell = from.id === to.id ? undefined : cellByKey.get(`${from.id}|${to.id}`);
                    return (
                      <td
                        key={to.id}
                        title={
                          cell
                            ? t("revenueTooltip", {
                                count: cell.customerCount.toLocaleString(intlLocale),
                                revenue: cell.revenue.toLocaleString(intlLocale, { maximumFractionDigits: 0 }),
                              })
                            : undefined
                        }
                        style={{
                          padding: "6px 8px",
                          textAlign: "center",
                          borderBottom: "1px solid #0F1924",
                          background: cell ? "rgba(96,165,250,0.12)" : "transparent",
                          color: cell ? "#60A5FA" : "#374151",
                          fontFamily: "monospace",
                          fontWeight: cell ? 700 : 400,
                        }}
                      >
                        {from.id === to.id ? "" : cell ? cell.customerCount.toLocaleString(intlLocale) : "·"}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
