"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Info, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole, canExportMarketingAnalyticsPii } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { useStoreContext, useStoreScopeReady } from "@/lib/useStoreContext";
import { useModules } from "@/features/modules/hooks/useModules";
import { usePostCampaignStore } from "@/features/marketing-analytics/post-campaign/store/usePostCampaignStore";
import {
  usePostCampaignSegments,
  useAnalyzePostCampaignSegment,
  usePostCampaignSummary,
  usePostCampaignDailyTurnover,
  usePostCampaignRfmActivity,
  usePostCampaignMigration,
  usePostCampaignCustomers,
} from "@/features/marketing-analytics/post-campaign/hooks/usePostCampaign";
import { ImportPanel } from "@/features/marketing-analytics/post-campaign/components/ImportPanel";
import { ValidationSummary } from "@/features/marketing-analytics/post-campaign/components/ValidationSummary";
import { DraftAnalyzedBanner } from "@/features/marketing-analytics/post-campaign/components/DraftAnalyzedBanner";
import { AfterPeriodBar } from "@/features/marketing-analytics/post-campaign/components/AfterPeriodBar";
import { SegmentsList } from "@/features/marketing-analytics/post-campaign/components/SegmentsList";
import { TopKpiCards } from "@/features/marketing-analytics/post-campaign/components/TopKpiCards";
import { ReportTabs } from "@/features/marketing-analytics/post-campaign/components/ReportTabs";
import { OverviewTab } from "@/features/marketing-analytics/post-campaign/components/OverviewTab";
import { RfmActivityCards } from "@/features/marketing-analytics/post-campaign/components/RfmActivityCards";
import { MigrationTab } from "@/features/marketing-analytics/post-campaign/components/MigrationTab";
import { CustomerTable } from "@/features/marketing-analytics/post-campaign/components/CustomerTable";
import type { PostCampaignCustomerSortBy, PostCampaignReportTab } from "@/features/marketing-analytics/post-campaign/types";

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

const sectionTitle: React.CSSProperties = { color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 };

/**
 * Фаза 4 — Post-Campaign Audience Analysis (TASK-473). Import an externally-sourced customer
 * list → pick equal before/after windows → "Аналізувати сегмент" → 3 report tabs + a full customer
 * table, per `docs/uployal/AUDIENCE_ANALYSIS.md`. Filter/segment-tracking state is split the same
 * way audience-builder's page splits its own:
 *  - store selection: the header's global multi-select StoreSelector (`useStoreContext`),
 *    same as every sibling phase (TASK-515).
 *  - import draft / which segment's report is showing / after-period: `usePostCampaignStore`
 *    (Zustand) — see that store's own doc comment for why (mirrors `isBuilt`'s draft-vs-committed
 *    shape, extended to track TWO segment ids for the source doc's §7 draft-vs-analyzed rule).
 * This page owns every React Query hook call and all tab/pagination/sort state, passing data down
 * as props to presentational components — same container/presentational split every sibling phase
 * already established.
 */
export default function PostCampaignPage() {
  const t = useTranslations("Dashboard.postCampaign.page");

  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;
  // Mirrors Sidebar.tsx's "marketing_analytics" NavGroup gate — fourth item, after Audience Builder.
  const effectiveAccess = useRequireTab("/marketing-analytics/post-campaign", "marketing_analytics", roleAccess === true);
  const access = roleAccess === null ? null : effectiveAccess;

  const { data: modulesData } = useModules(access === true);
  const moduleActive = !modulesData || modulesData.modules.includes("marketing_analytics");
  const canExportPii = canExportMarketingAnalyticsPii(me?.role, me?.permissions);
  const ready = useStoreScopeReady();
  const enabled = access === true && moduleActive && ready;

  // ── Store filter — reads the header's global multi-select StoreSelector (TASK-515), same
  // as every sibling phase now does. ─────────────────────────────────────────────────────────
  const { selectedStoreIds: storeIds } = useStoreContext();

  // ── Segment tracking + import draft + after-period (Zustand) ────────────────────────────────
  const importResult = usePostCampaignStore((s) => s.importResult);
  const draftSegmentId = usePostCampaignStore((s) => s.draftSegmentId);
  const reportSegmentId = usePostCampaignStore((s) => s.reportSegmentId);
  const reportVersion = usePostCampaignStore((s) => s.reportVersion);
  const afterStart = usePostCampaignStore((s) => s.afterStart);
  const afterEnd = usePostCampaignStore((s) => s.afterEnd);
  const setAfterRange = usePostCampaignStore((s) => s.setAfterRange);
  const applyAnalyzeResult = usePostCampaignStore((s) => s.applyAnalyzeResult);
  const selectExistingSegment = usePostCampaignStore((s) => s.selectExistingSegment);
  const clearAll = usePostCampaignStore((s) => s.clearAll);

  const hasReport = !!reportSegmentId;
  const hasUnappliedChanges = !!draftSegmentId && draftSegmentId !== reportSegmentId;
  const hasReportForCurrentDraft = !!draftSegmentId && draftSegmentId === reportSegmentId;

  const { data: segments = [], isLoading: segmentsLoading } = usePostCampaignSegments();
  const { mutate: analyzeMutate, isPending: isAnalyzing } = useAnalyzePostCampaignSegment();

  function handleAnalyze() {
    if (!draftSegmentId) return;
    if (afterStart > afterEnd) {
      toast.error(t("invalidRangeError"));
      return;
    }
    analyzeMutate(
      { segmentId: draftSegmentId, body: { afterStart, afterEnd } },
      {
        onSuccess: (result) => {
          applyAnalyzeResult(result);
          setCustomersPage(1);
        },
        onError: (e) => toast.error(t("analyzeErrorToast", { message: errorMessage(e) })),
      },
    );
  }

  function handleSelectSegment(item: (typeof segments)[number]) {
    selectExistingSegment(item);
    setActiveTab("overview");
    setCustomersPage(1);
  }

  function handleClear() {
    clearAll();
    setActiveTab("overview");
    setCustomersPage(1);
  }

  // ── Report tab + customer-table pagination/sort ─────────────────────────────────────────────
  const [activeTab, setActiveTab] = useState<PostCampaignReportTab>("overview");
  const [customersPage, setCustomersPage] = useState(1);
  const [customersSortBy, setCustomersSortBy] = useState<PostCampaignCustomerSortBy>("turnoverafter");
  const [customersSortDesc, setCustomersSortDesc] = useState(true);

  const customersParams = useMemo(
    () => ({ page: customersPage, pageSize: 20, sortBy: customersSortBy, sortDescending: customersSortDesc }),
    [customersPage, customersSortBy, customersSortDesc],
  );

  useEffect(() => {
    setCustomersPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reportSegmentId, reportVersion, storeIds, customersSortBy, customersSortDesc]);

  function handleCustomersSort(key: PostCampaignCustomerSortBy) {
    if (key === customersSortBy) setCustomersSortDesc((d) => !d);
    else {
      setCustomersSortBy(key);
      setCustomersSortDesc(true);
    }
  }

  // ── Report queries — only fire once a segment has actually been analyzed ───────────────────
  const summaryQuery = usePostCampaignSummary(reportSegmentId, reportVersion, storeIds, enabled && hasReport);
  const dailyTurnoverQuery = usePostCampaignDailyTurnover(reportSegmentId, reportVersion, storeIds, enabled && hasReport && activeTab === "overview");
  const rfmActivityQuery = usePostCampaignRfmActivity(reportSegmentId, reportVersion, storeIds, enabled && hasReport && activeTab === "activity");
  const migrationQuery = usePostCampaignMigration(reportSegmentId, reportVersion, storeIds, enabled && hasReport && activeTab === "migration");
  // Always fetched (not gated to a specific tab) — the customer table is its own always-visible
  // section below the 3 report tabs, per this task's brief (items 6-7 are listed separately).
  const customersQuery = usePostCampaignCustomers(reportSegmentId, reportVersion, customersParams, storeIds, enabled && hasReport);

  const beforeWindow = summaryQuery.data ? { start: summaryQuery.data.beforeStart, end: summaryQuery.data.beforeEnd } : null;

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  if (!moduleActive) {
    return (
      <div style={{ padding: "80px 32px", textAlign: "center", display: "flex", flexDirection: "column", alignItems: "center", gap: 16 }}>
        <div style={{ fontSize: 40 }}>🔒</div>
        <h2 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>{t("moduleGate.title")}</h2>
        <p style={{ color: "#4B5563", fontSize: 14, maxWidth: 440 }}>{t("moduleGate.body")}</p>
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 24, width: "100%" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: 12 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>{t("subtitle")}</p>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <SegmentsList segments={segments} isLoading={segmentsLoading} selectedSegmentId={draftSegmentId} onSelect={handleSelectSegment} />
          <button
            type="button"
            onClick={handleClear}
            disabled={!draftSegmentId && !importResult}
            title={t("clearButton")}
            style={{
              display: "flex",
              alignItems: "center",
              gap: 6,
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 8,
              padding: "8px 12px",
              color: !draftSegmentId && !importResult ? "#374151" : "#9CA3AF",
              fontSize: 13,
              cursor: !draftSegmentId && !importResult ? "default" : "pointer",
            }}
          >
            <Trash2 size={14} />
            {t("clearButton")}
          </button>
        </div>
      </div>

      {/* Same historical-data caveat as every sibling MarketingAnalytics page. */}
      <div style={{ background: "#0F1F3D", border: "1px solid #1D4ED8", borderRadius: 10, padding: "12px 16px", display: "flex", gap: 10, alignItems: "flex-start" }}>
        <Info size={16} color="#60A5FA" style={{ flexShrink: 0, marginTop: 1 }} />
        <p style={{ color: "#93C5FD", fontSize: 12.5, lineHeight: 1.5, margin: 0 }}>{t("historicalDataBanner")}</p>
      </div>

      <section style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
        <h2 style={sectionTitle}>{t("importSection")}</h2>
        <ImportPanel />
      </section>

      {importResult && <ValidationSummary result={importResult} />}

      {hasUnappliedChanges && <DraftAnalyzedBanner onAnalyzeClick={handleAnalyze} />}

      <AfterPeriodBar
        afterStart={afterStart}
        afterEnd={afterEnd}
        onRangeChange={setAfterRange}
        beforeWindow={beforeWindow}
        canAnalyze={!!draftSegmentId && afterStart <= afterEnd}
        isAnalyzing={isAnalyzing}
        hasReportForCurrentSegment={hasReportForCurrentDraft}
        onAnalyzeClick={handleAnalyze}
      />

      {!hasReport ? (
        <div style={{ display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center", gap: 10, padding: "48px 20px", color: "#4B5563", textAlign: "center" }}>
          <p style={{ maxWidth: 480, fontSize: 13, lineHeight: 1.6, margin: 0 }}>{t("emptyStateHint")}</p>
        </div>
      ) : (
        <>
          <TopKpiCards summary={summaryQuery.data} isLoading={summaryQuery.isLoading} />

          <ReportTabs tab={activeTab} onTabChange={setActiveTab} />

          {activeTab === "overview" && (
            <OverviewTab
              segmentId={reportSegmentId!}
              storeIds={storeIds}
              summary={summaryQuery.data}
              summaryLoading={summaryQuery.isLoading}
              dailyTurnover={dailyTurnoverQuery.data}
              dailyTurnoverLoading={dailyTurnoverQuery.isLoading}
            />
          )}
          {activeTab === "activity" && <RfmActivityCards data={rfmActivityQuery.data} isLoading={rfmActivityQuery.isLoading} />}
          {activeTab === "migration" && <MigrationTab data={migrationQuery.data} isLoading={migrationQuery.isLoading} />}

          <CustomerTable
            segmentId={reportSegmentId!}
            storeIds={storeIds}
            data={customersQuery.data}
            isLoading={customersQuery.isLoading}
            sortBy={customersSortBy}
            sortDescending={customersSortDesc}
            onSort={handleCustomersSort}
            page={customersPage}
            onPageChange={setCustomersPage}
            canExportPii={canExportPii}
          />
        </>
      )}
    </div>
  );
}
