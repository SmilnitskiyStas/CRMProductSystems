"use client";

import { useTranslations } from "next-intl";
import { Sparkles, Loader2, AlertTriangle } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { ApiError } from "@/lib/api";
import { useExplainRfmSegment } from "../../hooks/useMarketingAnalytics";
import type { RfmRecommendationDto, RfmSegmentKey, MarketingAnalyticsFilters } from "../../types";

interface Props {
  recommendation: RfmRecommendationDto;
  segmentKey: RfmSegmentKey;
  filters: MarketingAnalyticsFilters;
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
 * Тригер/Дія/Оффер/Застереження/Товари — always rendered immediately from the segment
 * detail response (template-based, no fetch). "Пояснити детальніше" calls the Claude-backed
 * /explain endpoint ONLY on click (never automatically on segment open, per the brief) and
 * appends its answer as an EXTRA block below, never replacing the template above.
 *
 * NOTE: the parent (SegmentDetailPanel) mounts this with `key={segmentKey + filters}` so a
 * segment or filter change remounts it fresh — an AI explanation generated for the OLD
 * numbers must never linger next to a NEW template recommendation.
 */
export function RecommendationCard({ recommendation, segmentKey, filters }: Props) {
  const t = useTranslations("Dashboard.marketingAnalytics.recommendation");
  const { mutate, isPending, data, error } = useExplainRfmSegment();

  function handleExplain() {
    mutate({ key: segmentKey, filters });
  }

  const unavailable = error instanceof ApiError && error.status === 503;

  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "18px 20px", display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{t("title")}</div>

      <Block label={t("triggerLabel")} text={recommendation.triggerUa} accent="#2DD4BF" />
      <Block label={t("actionLabel")} text={recommendation.actionUa} accent="#60A5FA" />
      <Block label={t("offerLabel")} text={recommendation.offerUa} accent="#4ADE80" />
      <Block label={t("cautionLabel")} text={recommendation.cautionUa} accent="#FBBF24" />

      {recommendation.productsForPromo.length > 0 && (
        <div>
          <div style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
            {t("productsLabel")}
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
            {recommendation.productsForPromo.map((p) => (
              <span
                key={p}
                style={{
                  padding: "4px 10px",
                  borderRadius: 6,
                  fontSize: 12,
                  background: "#111827",
                  border: "1px solid #1F2937",
                  color: "#9CA3AF",
                }}
              >
                {p}
              </span>
            ))}
          </div>
        </div>
      )}

      <div style={{ borderTop: "1px solid #1F2937", paddingTop: 14 }}>
        <Btn
          variant="ghost"
          size="sm"
          disabled={isPending}
          icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Sparkles size={13} color="#A78BFA" />}
          onClick={handleExplain}
        >
          {isPending ? t("explainLoading") : t("explainButton")}
        </Btn>

        {data && (
          <div style={{ marginTop: 12, background: "#18122B", border: "1px solid #4C1D95", borderRadius: 8, padding: "12px 14px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 6 }}>
              <Sparkles size={13} color="#A78BFA" />
              <span style={{ color: "#C4B5FD", fontSize: 11, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em" }}>
                {t("aiBadge")}
              </span>
            </div>
            <p style={{ color: "#E8EDF5", fontSize: 13, lineHeight: 1.6, whiteSpace: "pre-wrap", margin: 0 }}>
              {data.explanationUa}
            </p>
          </div>
        )}

        {error && (
          <div style={{ marginTop: 10, color: "#F87171", fontSize: 12, display: "flex", gap: 6, alignItems: "center" }}>
            <AlertTriangle size={13} style={{ flexShrink: 0 }} />
            {unavailable ? t("explainUnavailable") : t("explainError")}
          </div>
        )}
      </div>
    </div>
  );
}
