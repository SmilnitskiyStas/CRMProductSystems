"use client";

import { useTranslations } from "next-intl";
import { Sparkles, Loader2, AlertTriangle } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { ApiError } from "@/lib/api";
import type { PostCampaignRecommendationDto } from "../types";

interface ExplainState {
  onExplain: () => void;
  isPending: boolean;
  explanationUa?: string;
  error?: unknown;
}

function Block({ label, text, accent }: { label: string; text: string; accent: string }) {
  return (
    <div>
      <div style={{ color: accent, fontSize: 11, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 4 }}>
        {label}
      </div>
      <p style={{ color: "#D1D5DB", fontSize: 13, lineHeight: 1.6, margin: 0, whiteSpace: "pre-wrap" }}>{text}</p>
    </div>
  );
}

/**
 * Тригер/Дія/Оффер/Застереження block, reused by BOTH the Overview tab (Summary's recommendation)
 * and the Migration tab (Migration's own recommendation) — both DTOs share the exact same
 * `PostCampaignRecommendationDto` shape server-side. Same visual/interaction pattern as
 * `features/marketing-analytics/components/SegmentDetail/RecommendationCard.tsx` and
 * `price-segments/components/RecommendationBlock.tsx` (per this task's brief item 7) — built
 * locally rather than importing either literally, matching this codebase's own established
 * precedent: EVERY sibling phase (price-segments, audience-builder) re-implements this exact block
 * locally against its own DTO/explain-hook shape rather than cross-importing a RFM-specific
 * component (see task log for the full reasoning) — `PiiUnmaskToggle`/`TableControls` are the only
 * pieces this feature imports literally, since those are genuinely generic/stateless.
 *
 * `explain` is optional: only the Overview tab wires it to the real `/explain` endpoint (which is
 * bound to the Summary tab's KPIs server-side, `PostCampaignService.ExplainAsync`) — the Migration
 * tab's card renders the same template block with no AI button at all, since no migration-specific
 * explain endpoint exists.
 */
export function RecommendationCard({
  recommendation,
  explain,
}: {
  recommendation: PostCampaignRecommendationDto;
  explain?: ExplainState;
}) {
  const t = useTranslations("Dashboard.postCampaign.recommendation");
  const unavailable = explain?.error instanceof ApiError && explain.error.status === 503;

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "18px 20px", display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title")}</div>

      <Block label={t("triggerLabel")} text={recommendation.triggerUa} accent="#2DD4BF" />
      <Block label={t("actionLabel")} text={recommendation.actionUa} accent="#60A5FA" />
      <Block label={t("offerLabel")} text={recommendation.offerUa} accent="#4ADE80" />
      <Block label={t("cautionLabel")} text={recommendation.cautionUa} accent="#FBBF24" />

      {explain && (
        <div style={{ borderTop: "1px solid #1F2937", paddingTop: 14 }}>
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
              <p style={{ color: "#E8EDF5", fontSize: 13, lineHeight: 1.6, whiteSpace: "pre-wrap", margin: 0 }}>{explain.explanationUa}</p>
            </div>
          )}

          {explain.error != null && (
            <div style={{ marginTop: 10, color: "#F87171", fontSize: 12, display: "flex", gap: 6, alignItems: "center" }}>
              <AlertTriangle size={13} style={{ flexShrink: 0 }} />
              {unavailable ? t("explainUnavailable") : t("explainError")}
            </div>
          )}
        </div>
      )}

      {/* No-control-group caveat — mandatory per source doc §17.8/§28: the causal language in the
          copy above ("кампанія підняла продажі", etc.) is not statistically proven without a
          holdout/control audience. */}
      <p style={{ color: "#4B5563", fontSize: 10.5, lineHeight: 1.5, margin: 0, fontStyle: "italic" }}>{t("noControlGroupNote")}</p>
    </div>
  );
}
