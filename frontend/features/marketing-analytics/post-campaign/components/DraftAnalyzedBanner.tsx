"use client";

import { useTranslations } from "next-intl";
import { AlertCircle } from "lucide-react";

/**
 * "Є незастосовані зміни аудиторії" — the source doc's single most-important UX finding
 * (`docs/uployal/AUDIENCE_ANALYSIS.md` §7/§31.3: the competitor only weakly signals this via a
 * changed "Розпізнано N ID" caption next to an otherwise-unchanged old report). Rendered whenever
 * `usePostCampaignStore`'s `draftSegmentId !== reportSegmentId` — page.tsx owns that comparison and
 * only mounts this component when it's true, so the component itself stays a plain, stateless
 * banner (no store reads of its own).
 */
export function DraftAnalyzedBanner({ onAnalyzeClick }: { onAnalyzeClick: () => void }) {
  const t = useTranslations("Dashboard.postCampaign.draftBanner");

  return (
    <div
      style={{
        background: "#2A2109",
        border: "1px solid #92400E",
        borderRadius: 10,
        padding: "12px 16px",
        display: "flex",
        gap: 10,
        alignItems: "center",
        justifyContent: "space-between",
        flexWrap: "wrap",
      }}
    >
      <div style={{ display: "flex", gap: 10, alignItems: "flex-start" }}>
        <AlertCircle size={16} color="#FBBF24" style={{ flexShrink: 0, marginTop: 1 }} />
        <div>
          <div style={{ color: "#FBBF24", fontSize: 13, fontWeight: 700 }}>{t("title")}</div>
          <p style={{ color: "#D1B26B", fontSize: 12, lineHeight: 1.5, margin: "2px 0 0" }}>{t("body")}</p>
        </div>
      </div>
      <button
        type="button"
        onClick={onAnalyzeClick}
        style={{
          background: "#92400E",
          border: "1px solid #B45309",
          borderRadius: 7,
          color: "#FEF3C7",
          fontSize: 12,
          fontWeight: 600,
          padding: "7px 14px",
          cursor: "pointer",
          whiteSpace: "nowrap",
        }}
      >
        {t("cta")}
      </button>
    </div>
  );
}
