"use client";

import { useTranslations } from "next-intl";
import type { PostCampaignReportTab } from "../types";

const TABS: PostCampaignReportTab[] = ["overview", "activity", "migration"];

interface Props {
  tab: PostCampaignReportTab;
  onTabChange: (t: PostCampaignReportTab) => void;
}

/**
 * 3 report tabs — Огляд / Активність R/F/M / Міграція сегментів (source doc §3/§14/§18/§25).
 * Presentational only, mirrors `audience-builder/components/ResultTabs.tsx` exactly — page.tsx
 * owns which tab is active.
 */
export function ReportTabs({ tab, onTabChange }: Props) {
  const t = useTranslations("Dashboard.postCampaign.reportTabs");

  return (
    <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
      {TABS.map((tb) => {
        const active = tab === tb;
        return (
          <button
            key={tb}
            type="button"
            onClick={() => onTabChange(tb)}
            style={{
              padding: "10px 18px",
              background: "transparent",
              border: "none",
              borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
              color: active ? "#3B82F6" : "#6B7280",
              fontSize: 13,
              fontWeight: active ? 600 : 400,
              cursor: "pointer",
              marginBottom: -1,
              transition: "color 0.15s",
            }}
          >
            {t(tb)}
          </button>
        );
      })}
    </div>
  );
}
