"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Info } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole, canExportMarketingAnalyticsPii } from "@/lib/roles";
import { useRequireTab } from "@/lib/useRequireTab";
import { useModules } from "@/features/modules/hooks/useModules";
import { useAudienceBuilderStore } from "@/features/marketing-analytics/audience-builder/store/useAudienceBuilderStore";
import {
  useAudienceOverview,
  useAudienceBuyers,
  useMatchedItems,
  useCompetitorOverview,
  useCompetitorBuyers,
} from "@/features/marketing-analytics/audience-builder/hooks/useAudienceBuilder";
import { buildAudienceRequest, buildCompetitorRequest } from "@/features/marketing-analytics/audience-builder/api/audienceBuilder";
import { AudiencePeriodBar } from "@/features/marketing-analytics/audience-builder/components/AudiencePeriodBar";
import { TermSearchBar } from "@/features/marketing-analytics/audience-builder/components/TermSearchBar";
import { TermChips } from "@/features/marketing-analytics/audience-builder/components/TermChips";
import { CombineModeToggle } from "@/features/marketing-analytics/audience-builder/components/CombineModeToggle";
import { ThresholdInputs } from "@/features/marketing-analytics/audience-builder/components/ThresholdInputs";
import { BuildResetBar } from "@/features/marketing-analytics/audience-builder/components/BuildResetBar";
import { ResultTabs, type AudienceResultTab } from "@/features/marketing-analytics/audience-builder/components/ResultTabs";
import { BuyersKpiCards } from "@/features/marketing-analytics/audience-builder/components/BuyersTab/BuyersKpiCards";
import { BuyersTable } from "@/features/marketing-analytics/audience-builder/components/BuyersTab/BuyersTable";
import { CompetitorTermInput } from "@/features/marketing-analytics/audience-builder/components/CompetitorTab/CompetitorTermInput";
import { HorizonToggle } from "@/features/marketing-analytics/audience-builder/components/CompetitorTab/HorizonToggle";
import { CompetitorKpiCards } from "@/features/marketing-analytics/audience-builder/components/CompetitorTab/CompetitorKpiCards";
import { CompetitorBuyersTable } from "@/features/marketing-analytics/audience-builder/components/CompetitorTab/CompetitorBuyersTable";
import { MatchedItemsTable } from "@/features/marketing-analytics/audience-builder/components/MatchedItemsTab/MatchedItemsTable";
import type {
  AudienceFilterState,
  BuyersSortBy,
  CompetitorFilterState,
  MatchedItemsSortBy,
} from "@/features/marketing-analytics/audience-builder/types";

function toDateStr(d: Date): string {
  return d.toISOString().slice(0, 10);
}
function defaultFrom(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return toDateStr(d);
}
function defaultTo(): string {
  return toDateStr(new Date());
}

const sectionTitle: React.CSSProperties = { color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 };

/**
 * Фаза 3 — Audience Builder (TASK-430). Query-builder page: pick text/category terms → OR/AND →
 * optional thresholds → "Сформувати список" → three result tabs (Покупці товару / Конкурентна
 * аудиторія / Знайдені товари). Unlike Фаза 1/2's dashboards, this page's filter state is split
 * across TWO owners on purpose:
 *  - period (from/to) + store selection: page-local `useState`, same convention Фаза 1/2 use.
 *  - terms/mode/thresholds/exclusions/competitor state: the `useAudienceBuilderStore` Zustand
 *    store, written to directly by the leaf control components (TermChips, ThresholdInputs,
 *    MatchedItemsTable's checkboxes, CompetitorTermInput, HorizonToggle) — see that store's own
 *    doc comment for why a store exists here when Фаза 1/2 don't need one.
 * This page still owns every React Query hook call and all pagination/sort state, passing data
 * down as props to presentational table components — same container/presentational split
 * price-segments/page.tsx already established.
 */
export default function AudienceBuilderPage() {
  const t = useTranslations("Dashboard.audienceBuilder.page");

  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;
  // Mirrors Sidebar.tsx's "marketing_analytics" NavGroup gate — third item, after the RFM
  // dashboard and price-segments.
  const effectiveAccess = useRequireTab("/marketing-analytics/audience-builder", "marketing_analytics", roleAccess === true);
  const access = roleAccess === null ? null : effectiveAccess;

  const { data: modulesData } = useModules(access === true);
  const moduleActive = !modulesData || modulesData.modules.includes("marketing_analytics");
  const canExportPii = canExportMarketingAnalyticsPii(me?.role, me?.permissions);

  // ── Period + store filter — page-local, deliberately NOT in the Zustand store (see
  // AudiencePeriodBar's doc comment). ─────────────────────────────────────────────────────────
  const [from, setFrom] = useState<string>(defaultFrom);
  const [to, setTo] = useState<string>(defaultTo);
  const [storeIds, setStoreIds] = useState<string[]>([]);

  // ── Builder state (Zustand) ─────────────────────────────────────────────────────────────────
  const terms = useAudienceBuilderStore((s) => s.terms);
  const mode = useAudienceBuilderStore((s) => s.mode);
  const minQuantity = useAudienceBuilderStore((s) => s.minQuantity);
  const minAmount = useAudienceBuilderStore((s) => s.minAmount);
  const excludedItemIds = useAudienceBuilderStore((s) => s.excludedItemIds);
  const isBuilt = useAudienceBuilderStore((s) => s.isBuilt);
  const competitorTerms = useAudienceBuilderStore((s) => s.competitorTerms);
  const horizon = useAudienceBuilderStore((s) => s.horizon);

  const enabled = access === true && moduleActive;

  // ── Result tab + per-table pagination/sort ──────────────────────────────────────────────────
  const [activeTab, setActiveTab] = useState<AudienceResultTab>("buyers");

  const [buyersPage, setBuyersPage] = useState(1);
  const [buyersSortBy, setBuyersSortBy] = useState<BuyersSortBy>("qty");
  const [buyersSortDesc, setBuyersSortDesc] = useState(true);

  const [matchedPage, setMatchedPage] = useState(1);
  const [matchedSortBy, setMatchedSortBy] = useState<MatchedItemsSortBy>("sold");
  const [matchedSortDesc, setMatchedSortDesc] = useState(true);

  const [competitorPage, setCompetitorPage] = useState(1);
  const [competitorSortBy, setCompetitorSortBy] = useState<BuyersSortBy>("qty");
  const [competitorSortDesc, setCompetitorSortDesc] = useState(true);

  // ── Own-product filter/request objects ──────────────────────────────────────────────────────
  const ownFilter: AudienceFilterState = useMemo(
    () => ({ from, to, storeIds, terms, mode, minQuantity, minAmount, excludedItemIds }),
    [from, to, storeIds, terms, mode, minQuantity, minAmount, excludedItemIds],
  );
  // Page-reset key deliberately EXCLUDES `excludedItemIds` — toggling one SKU keeps the same
  // matched-item SET (only per-row isExcluded flips), so "Знайдені товари" shouldn't bounce back
  // to page 1 on every checkbox click. Buyers' own population genuinely changes on exclusion, so
  // its page-reset effect below keys off the full `ownFilter` instead.
  const matchedItemsSearchKey = useMemo(
    () => ({ from, to, storeIds, terms, mode, minQuantity, minAmount }),
    [from, to, storeIds, terms, mode, minQuantity, minAmount],
  );

  const overviewRequest = useMemo(
    () => buildAudienceRequest(ownFilter, { page: 1, pageSize: 1, sortBy: null, sortDescending: true }),
    [ownFilter],
  );
  const buyersRequest = useMemo(
    () => buildAudienceRequest(ownFilter, { page: buyersPage, pageSize: 10, sortBy: buyersSortBy, sortDescending: buyersSortDesc }),
    [ownFilter, buyersPage, buyersSortBy, buyersSortDesc],
  );
  const matchedItemsRequest = useMemo(
    () => buildAudienceRequest(ownFilter, { page: matchedPage, pageSize: 20, sortBy: matchedSortBy, sortDescending: matchedSortDesc }),
    [ownFilter, matchedPage, matchedSortBy, matchedSortDesc],
  );

  // Overview is cheap (single aggregate row) and feeds TWO consumers (BuyersKpiCards +
  // MatchedItemsTable's "Обрано X з Y") — kept live whenever built, regardless of which result
  // tab is active, so it's never stale on the tab that doesn't happen to trigger its own fetch.
  const { data: overview, isLoading: overviewLoading } = useAudienceOverview(overviewRequest, enabled && isBuilt);
  // The two paginated tables only fetch while their own tab is actually visible.
  const { data: buyersTable, isLoading: buyersLoading } = useAudienceBuyers(
    buyersRequest,
    enabled && isBuilt && activeTab === "buyers",
  );
  const { data: matchedItemsTable, isLoading: matchedLoading } = useMatchedItems(
    matchedItemsRequest,
    enabled && isBuilt && activeTab === "matchedItems",
  );

  useEffect(() => {
    setBuyersPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ownFilter, buyersSortBy, buyersSortDesc]);

  useEffect(() => {
    setMatchedPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [matchedItemsSearchKey, matchedSortBy, matchedSortDesc]);

  function handleBuyersSort(key: BuyersSortBy) {
    if (key === buyersSortBy) setBuyersSortDesc((d) => !d);
    else {
      setBuyersSortBy(key);
      setBuyersSortDesc(true);
    }
  }
  function handleMatchedSort(key: MatchedItemsSortBy) {
    if (key === matchedSortBy) setMatchedSortDesc((d) => !d);
    else {
      setMatchedSortBy(key);
      setMatchedSortDesc(true);
    }
  }

  // ── Competitor filter/request objects ───────────────────────────────────────────────────────
  // ownTerms/ownExcludedItemIds are the SAME state as the main tab (task log 429) — no separate
  // competitor-side copy exists.
  const competitorFilter: CompetitorFilterState = useMemo(
    () => ({ from, to, storeIds, ownTerms: terms, ownExcludedItemIds: excludedItemIds, competitorTerms, horizon }),
    [from, to, storeIds, terms, excludedItemIds, competitorTerms, horizon],
  );
  const competitorOverviewRequest = useMemo(
    () => buildCompetitorRequest(competitorFilter, { page: 1, pageSize: 1, sortBy: null, sortDescending: true }),
    [competitorFilter],
  );
  const competitorBuyersRequest = useMemo(
    () =>
      buildCompetitorRequest(competitorFilter, {
        page: competitorPage,
        pageSize: 10,
        sortBy: competitorSortBy,
        sortDescending: competitorSortDesc,
      }),
    [competitorFilter, competitorPage, competitorSortBy, competitorSortDesc],
  );

  // Requires at least one competitor term before anything fires (analysis §15.1: "Перед
  // використанням уже має бути сформована базова умова «наш товар»" — own terms are covered by
  // `isBuilt` already being required).
  const competitorHasTerms = competitorTerms.length > 0;
  const { data: competitorOverview, isLoading: competitorOverviewLoading } = useCompetitorOverview(
    competitorOverviewRequest,
    enabled && isBuilt && competitorHasTerms,
  );
  const { data: competitorBuyersTable, isLoading: competitorBuyersLoading } = useCompetitorBuyers(
    competitorBuyersRequest,
    enabled && isBuilt && competitorHasTerms && activeTab === "competitor",
  );

  useEffect(() => {
    setCompetitorPage(1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [competitorFilter, competitorSortBy, competitorSortDesc]);

  function handleCompetitorSort(key: BuyersSortBy) {
    if (key === competitorSortBy) setCompetitorSortDesc((d) => !d);
    else {
      setCompetitorSortBy(key);
      setCompetitorSortDesc(true);
    }
  }

  function handleAfterReset() {
    setActiveTab("buyers");
    setBuyersPage(1);
    setMatchedPage(1);
    setCompetitorPage(1);
  }

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
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>{t("subtitle")}</p>
      </div>

      {/* Same historical-data caveat as RFM/price-segments — everything here is also derived
          solely from customer-linked (loyalty) POS sales. */}
      <div style={{ background: "#0F1F3D", border: "1px solid #1D4ED8", borderRadius: 10, padding: "12px 16px", display: "flex", gap: 10, alignItems: "flex-start" }}>
        <Info size={16} color="#60A5FA" style={{ flexShrink: 0, marginTop: 1 }} />
        <p style={{ color: "#93C5FD", fontSize: 12.5, lineHeight: 1.5, margin: 0 }}>{t("historicalDataBanner")}</p>
      </div>

      <AudiencePeriodBar
        from={from}
        to={to}
        onRangeChange={(f, tt) => {
          setFrom(f);
          setTo(tt);
        }}
        storeIds={storeIds}
        onStoreIdsChange={setStoreIds}
      />

      <section style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 20, display: "flex", flexDirection: "column", gap: 16 }}>
        <h2 style={sectionTitle}>{t("searchSection")}</h2>
        <TermSearchBar />
        <TermChips />
        <CombineModeToggle />
        <ThresholdInputs />
        <BuildResetBar onAfterReset={handleAfterReset} />
      </section>

      {!isBuilt ? (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            gap: 10,
            padding: "48px 20px",
            color: "#4B5563",
            textAlign: "center",
          }}
        >
          <div style={{ color: "#9CA3AF", fontSize: 15, fontWeight: 600 }}>{t("emptyState.title")}</div>
          <p style={{ maxWidth: 480, fontSize: 13, lineHeight: 1.6, margin: 0 }}>{t("emptyState.body")}</p>
        </div>
      ) : (
        <>
          <ResultTabs tab={activeTab} onTabChange={setActiveTab} />

          {activeTab === "buyers" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
              <BuyersKpiCards overview={overview} isLoading={overviewLoading} />
              <BuyersTable
                data={buyersTable}
                isLoading={buyersLoading}
                sortBy={buyersSortBy}
                sortDescending={buyersSortDesc}
                onSort={handleBuyersSort}
                page={buyersPage}
                onPageChange={setBuyersPage}
                filter={ownFilter}
                canExportPii={canExportPii}
              />
            </div>
          )}

          {activeTab === "competitor" && (
            <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
              <CompetitorTermInput />
              {!competitorHasTerms ? (
                <div style={{ color: "#4B5563", fontSize: 13, padding: "24px 0", textAlign: "center" }}>{t("competitorEmptyHint")}</div>
              ) : (
                <>
                  <HorizonToggle />
                  <CompetitorKpiCards overview={competitorOverview} isLoading={competitorOverviewLoading} />
                  <CompetitorBuyersTable
                    data={competitorBuyersTable}
                    isLoading={competitorBuyersLoading}
                    sortBy={competitorSortBy}
                    sortDescending={competitorSortDesc}
                    onSort={handleCompetitorSort}
                    page={competitorPage}
                    onPageChange={setCompetitorPage}
                    filter={competitorFilter}
                    canExportPii={canExportPii}
                  />
                </>
              )}
            </div>
          )}

          {activeTab === "matchedItems" && (
            <MatchedItemsTable
              data={matchedItemsTable}
              isLoading={matchedLoading}
              sortBy={matchedSortBy}
              sortDescending={matchedSortDesc}
              onSort={handleMatchedSort}
              page={matchedPage}
              onPageChange={setMatchedPage}
              selectedCount={overview?.itemsInSelectionCount}
            />
          )}
        </>
      )}
    </div>
  );
}
