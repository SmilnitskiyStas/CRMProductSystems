"use client";

import { useTranslations } from "next-intl";
import { Sparkles, Loader2, AlertTriangle } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { ApiError } from "@/lib/api";
import type { PriceSegmentRecommendationDto } from "../types";

interface ExplainState {
  onExplain: () => void;
  isPending: boolean;
  explanationUa?: string;
  error?: unknown;
}

function Block({ label, text, accent }: { label: string; text: string; accent: string }) {
  return (
    <div>
      <div
        style={{
          color: accent,
          fontSize: 11,
          fontWeight: 700,
          textTransform: "uppercase",
          letterSpacing: "0.05em",
          marginBottom: 4,
        }}
      >
        {label}
      </div>
      <p style={{ color: "#D1D5DB", fontSize: 13, lineHeight: 1.6, margin: 0, whiteSpace: "pre-wrap" }}>{text}</p>
    </div>
  );
}

/**
 * Shared Тригер/Дія/Оффер/Застереження + "Пояснити детальніше" block. Not part of the original
 * file list — factored out because 3 call sites (PriceAudienceTable, FrequencyAudienceTable,
 * AllTimeView/SegmentRecommendationCard) all share the exact same `PriceSegmentRecommendationDto`
 * shape and the same Claude-backed /explain interaction; this avoids copy-pasting ~40 lines of
 * JSX/logic three times (noted in the task log per CLAUDE.md's "judgment call, implement per
 * convention" guidance). Mirrors `features/marketing-analytics/components/SegmentDetail/
 * RecommendationCard.tsx`'s behavior, minus RFM's `productsForPromo` (Фаза 2 has no per-audience
 * top-products query to anchor a cross-sell list against — task log 420).
 *
 * Callers remount this (via a `key` on the recommendation-owning ancestor, same convention RFM
 * uses) whenever the segment/audience or active filters change, so a Claude explanation for the
 * OLD numbers never lingers next to a NEW template recommendation.
 */
export function RecommendationBlock({
  recommendation,
  explain,
  compact,
}: {
  recommendation: PriceSegmentRecommendationDto;
  explain: ExplainState;
  compact?: boolean;
}) {
  const t = useTranslations("Dashboard.priceSegments.recommendation");
  const unavailable = explain.error instanceof ApiError && explain.error.status === 503;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: compact ? 10 : 14 }}>
      <Block label={t("triggerLabel")} text={recommendation.triggerUa} accent="#2DD4BF" />
      <Block label={t("actionLabel")} text={recommendation.actionUa} accent="#60A5FA" />
      <Block label={t("offerLabel")} text={recommendation.offerUa} accent="#4ADE80" />
      <Block label={t("cautionLabel")} text={recommendation.cautionUa} accent="#FBBF24" />

      <div style={{ borderTop: "1px solid #1F2937", paddingTop: 12 }}>
        <Btn
          variant="ghost"
          size="sm"
          disabled={explain.isPending}
          icon={explain.isPending ? <Loader2 size={13} className="animate-spin" /> : <Sparkles size={13} color="#A78BFA" />}
          onClick={explain.onExplain}
        >
          {explain.isPending ? t("explainLoading") : t("explainButton")}
        </Btn>

        {explain.explanationUa && (
          <div style={{ marginTop: 12, background: "#18122B", border: "1px solid #4C1D95", borderRadius: 8, padding: "12px 14px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 6 }}>
              <Sparkles size={13} color="#A78BFA" />
              <span style={{ color: "#C4B5FD", fontSize: 11, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em" }}>
                {t("aiBadge")}
              </span>
            </div>
            <p style={{ color: "#E8EDF5", fontSize: 13, lineHeight: 1.6, whiteSpace: "pre-wrap", margin: 0 }}>
              {explain.explanationUa}
            </p>
          </div>
        )}

        {explain.error != null && (
          <div style={{ marginTop: 10, color: "#F87171", fontSize: 12, display: "flex", gap: 6, alignItems: "center" }}>
            <AlertTriangle size={13} style={{ flexShrink: 0 }} />
            {unavailable ? t("explainUnavailable") : t("explainError")}
          </div>
        )}
      </div>
    </div>
  );
}
