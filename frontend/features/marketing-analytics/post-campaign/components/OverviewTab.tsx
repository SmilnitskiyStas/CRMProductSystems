"use client";

import { DailyTurnoverChart } from "./DailyTurnoverChart";
import { SegmentStatusDonut } from "./SegmentStatusDonut";
import { RecommendationCard } from "./RecommendationCard";
import { useExplainPostCampaign } from "../hooks/usePostCampaign";
import type { PostCampaignDailyTurnoverDto, PostCampaignSummaryDto } from "../types";

interface Props {
  segmentId: string;
  storeIds: string[];
  summary: PostCampaignSummaryDto | undefined;
  summaryLoading: boolean;
  dailyTurnover: PostCampaignDailyTurnoverDto | undefined;
  dailyTurnoverLoading: boolean;
}

/**
 * "Огляд" tab (source doc §14): daily turnover chart, segment-status donut, recommendation card
 * with the live "Пояснити детальніше" AI button — the only tab wired to `POST .../explain` (it's
 * bound to the Summary tab's KPIs server-side, `PostCampaignService.ExplainAsync`; the Migration
 * tab's own recommendation has no matching explain endpoint, see `RecommendationCard`'s doc
 * comment). Remounted by page.tsx via a `key` on segment/version change, same convention Фаза 1/2's
 * recommendation cards use, so a stale AI explanation never lingers next to new numbers.
 */
export function OverviewTab({ segmentId, storeIds, summary, summaryLoading, dailyTurnover, dailyTurnoverLoading }: Props) {
  const { mutate, isPending, data, error } = useExplainPostCampaign();

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <DailyTurnoverChart data={dailyTurnover} isLoading={dailyTurnoverLoading} />
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", gap: 16 }}>
        <SegmentStatusDonut summary={summary} isLoading={summaryLoading} />
        {summary && (
          <RecommendationCard
            recommendation={summary.recommendation}
            explain={{
              onExplain: () => mutate({ segmentId, storeIds }),
              isPending,
              explanationUa: data?.explanationUa,
              error,
            }}
          />
        )}
      </div>
    </div>
  );
}
