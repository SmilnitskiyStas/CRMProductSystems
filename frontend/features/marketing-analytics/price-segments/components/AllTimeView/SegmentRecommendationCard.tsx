"use client";

import { useTranslations } from "next-intl";
import { RecommendationBlock } from "../RecommendationBlock";
import { useExplainAllTimeSegment } from "../../hooks/usePriceSegments";
import type { PriceSegmentRecommendationDto, PriceSegmentKey } from "../../types";

interface Props {
  recommendation: PriceSegmentRecommendationDto | null;
  selectedSegment: PriceSegmentKey | null;
  storeIds: string[];
}

/**
 * All-time mode's recommendation slot. `recommendation` is null until a segment is picked via
 * SegmentDistributionChart — mirrors the competitor's own
 * "Оберіть ціновий сегмент нижче — рекомендація підлаштується під нього" prompt (analysis doc
 * §14), rendered as a deliberate hint state, never as an error or a blank card. Remounts (via
 * `key={selectedSegment}`) on every segment change so a Claude explanation for the OLD segment
 * never lingers next to a NEW template recommendation — same convention as
 * PriceAudienceTable/FrequencyAudienceTable's own remount key.
 */
export function SegmentRecommendationCard({ recommendation, selectedSegment, storeIds }: Props) {
  const t = useTranslations("Dashboard.priceSegments.allTime.recommendation");
  const { mutate, isPending, data, error } = useExplainAllTimeSegment();

  if (!selectedSegment || !recommendation) {
    return (
      <div
        style={{
          background: "#0D1117",
          border: "1px dashed #374151",
          borderRadius: 10,
          padding: "24px 16px",
          textAlign: "center",
          color: "#4B5563",
          fontSize: 13,
        }}
      >
        {t("selectSegmentHint")}
      </div>
    );
  }

  return (
    <div key={selectedSegment} style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "18px 20px" }}>
      <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, marginBottom: 14 }}>{t("title")}</div>
      <RecommendationBlock
        recommendation={recommendation}
        explain={{
          onExplain: () => mutate({ segment: selectedSegment, storeIds }),
          isPending,
          explanationUa: data?.explanationUa,
          error,
        }}
      />
    </div>
  );
}
