"use client";

import { useMemo, useState, type CSSProperties, type ReactNode } from "react";
import { useQueries } from "@tanstack/react-query";
import { BarChart3, Download, Eye, FileSpreadsheet, MapPin, MousePointerClick, ScanLine, ShoppingBag, Smartphone, Target, Users } from "lucide-react";
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { toast } from "sonner";
import { DateRangePicker, computePreviousPeriod, toDateInputValue, type SimpleDateRange } from "@/components/ui/DateRangePicker";
import { useStoreContext, useStoreScopeReady } from "@/lib/useStoreContext";
import { useBanners } from "../hooks/useBanners";
import { useMobileCatalogSettings } from "../hooks/useMobileCatalogSettings";
import { usePromotionCampaigns } from "../hooks/usePromotionCampaigns";
import { bannersApi } from "../api/banners";
import { mobileCatalogSettingsApi, type CatalogAnalytics } from "../api/mobileCatalogSettings";
import { promotionCampaignsApi } from "../api/promotionCampaigns";
import type { BannerAnalyticsDto, PromotionCampaignAnalyticsDto } from "../types";
import { exportAnalyticsCsv, exportAnalyticsXlsx, type AnalyticsExportPayload } from "../utils/analyticsExport";
import { SectionTabs } from "./SectionTabs";

type Tab = "overview" | "content" | "funnel" | "comparison" | "audience";
type ComparisonType = "all" | "banner" | "catalog" | "promotion";
type Detail =
  | { kind: "banner"; title: string; analytics?: BannerAnalyticsDto }
  | { kind: "catalog"; title: string; analytics?: CatalogAnalytics }
  | { kind: "promotion"; title: string; analytics?: PromotionCampaignAnalyticsDto };

const panel: CSSProperties = { background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 18 };
const muted: CSSProperties = { color: "#64748B", fontSize: 12, margin: 0 };

function number(value: number) {
  return new Intl.NumberFormat("uk-UA", { maximumFractionDigits: 2 }).format(value);
}

function money(value: number) {
  return new Intl.NumberFormat("uk-UA", { style: "currency", currency: "UAH", maximumFractionDigits: 2 }).format(value);
}

function Metric({ label, value, hint, icon, change }: { label: string; value: string; hint: string; icon: ReactNode; change?: number | null }) {
  return (
    <article style={{ ...panel, minHeight: 116, display: "flex", flexDirection: "column", justifyContent: "space-between", gap: 12 }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
        <span style={{ color: "#94A3B8", fontSize: 12, fontWeight: 600 }}>{label}</span>
        <span style={{ color: "#60A5FA", display: "inline-flex" }}>{icon}</span>
      </div>
      <strong style={{ color: "#E8EDF5", fontSize: 25, lineHeight: 1 }}>{value}</strong>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }}><p style={muted}>{hint}</p>{change !== undefined && change !== null && <span style={{ color: change >= 0 ? "#4ADE80" : "#F87171", fontSize: 11, fontWeight: 700 }}>{change >= 0 ? "+" : ""}{number(change)}%</span>}</div>
    </article>
  );
}

function EmptyMetricNotice({ children }: { children: ReactNode }) {
  return <div style={{ ...panel, color: "#64748B", fontSize: 13, textAlign: "center", padding: 28 }}>{children}</div>;
}

export function ConsumerAppAnalyticsSection() {
  const [tab, setTab] = useState<Tab>("overview");
  const [detail, setDetail] = useState<Detail | null>(null);
  const [comparisonType, setComparisonType] = useState<ComparisonType>("all");
  const [comparisonSort, setComparisonSort] = useState<"revenue" | "conversion" | "reach">("revenue");
  const [isExportingXlsx, setIsExportingXlsx] = useState(false);
  const [range, setRange] = useState<SimpleDateRange>(() => {
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - 29);
    return { from, to };
  });
  const [compareEnabled, setCompareEnabled] = useState(false);
  const [compareRange, setCompareRange] = useState<SimpleDateRange>(() => computePreviousPeriod(range.from, range.to));
  const selectedStoreIds = useStoreContext((state) => state.selectedStoreIds);
  const storeScopeReady = useStoreScopeReady();
  const from = toDateInputValue(range.from);
  const to = toDateInputValue(range.to);
  const compareFrom = toDateInputValue(compareRange.from);
  const compareTo = toDateInputValue(compareRange.to);
  const banners = useBanners();
  const catalogs = useMobileCatalogSettings();
  const promotions = usePromotionCampaigns();
  const matchesStoreScope = (locationIds: string[]) => selectedStoreIds.length === 0 || locationIds.length === 0 || locationIds.some((id) => selectedStoreIds.includes(id));
  const visibleBanners = useMemo(() => (banners.data ?? []).filter((item) => matchesStoreScope(item.locationIds)), [banners.data, selectedStoreIds]);
  const visiblePromotions = useMemo(() => (promotions.data ?? []).filter((item) => matchesStoreScope(item.locationIds)), [promotions.data, selectedStoreIds]);
  const measurableCatalogs = useMemo(
    () => (catalogs.data ?? []).filter((item) => item.status !== "draft" && matchesStoreScope(item.locationIds)),
    [catalogs.data, selectedStoreIds],
  );
  const catalogQueries = useQueries({
    queries: measurableCatalogs.map((catalog) => ({
      queryKey: ["mobile-catalog-publications", catalog.id, "analytics", from, to, selectedStoreIds],
      queryFn: () => mobileCatalogSettingsApi.analytics(catalog.id, { from, to, storeIds: selectedStoreIds }),
      staleTime: 60_000,
      enabled: storeScopeReady,
    })),
  });
  const comparisonCatalogQueries = useQueries({
    queries: measurableCatalogs.map((catalog) => ({
      queryKey: ["mobile-catalog-publications", catalog.id, "analytics", compareFrom, compareTo, selectedStoreIds],
      queryFn: () => mobileCatalogSettingsApi.analytics(catalog.id, { from: compareFrom, to: compareTo, storeIds: selectedStoreIds }),
      staleTime: 60_000,
      enabled: storeScopeReady && compareEnabled,
    })),
  });
  const bannerQueries = useQueries({
    queries: visibleBanners.map((banner) => ({
      queryKey: ["banners", banner.id, "analytics", from, to, selectedStoreIds],
      queryFn: () => bannersApi.getAnalytics(banner.id, { from, to, storeIds: selectedStoreIds }),
      staleTime: 60_000,
    })),
  });
  const comparisonBannerQueries = useQueries({
    queries: visibleBanners.map((banner) => ({
      queryKey: ["banners", banner.id, "analytics", compareFrom, compareTo, selectedStoreIds],
      queryFn: () => bannersApi.getAnalytics(banner.id, { from: compareFrom, to: compareTo, storeIds: selectedStoreIds }),
      staleTime: 60_000,
      enabled: compareEnabled,
    })),
  });
  const promotionQueries = useQueries({
    queries: visiblePromotions.map((campaign) => ({
      queryKey: ["promotion-campaigns", campaign.id, "analytics", from, to, selectedStoreIds],
      queryFn: () => promotionCampaignsApi.analytics(campaign.id, { from, to, storeIds: selectedStoreIds }),
      staleTime: 60_000,
      enabled: storeScopeReady,
    })),
  });
  const comparisonPromotionQueries = useQueries({
    queries: visiblePromotions.map((campaign) => ({
      queryKey: ["promotion-campaigns", campaign.id, "analytics", compareFrom, compareTo, selectedStoreIds],
      queryFn: () => promotionCampaignsApi.analytics(campaign.id, { from: compareFrom, to: compareTo, storeIds: selectedStoreIds }),
      staleTime: 60_000,
      enabled: storeScopeReady && compareEnabled,
    })),
  });

  const catalogRows = useMemo(
    () => measurableCatalogs.map((catalog, index) => ({ catalog, analytics: catalogQueries[index]?.data as CatalogAnalytics | undefined })),
    [measurableCatalogs, catalogQueries],
  );
  const bannerRows = useMemo(() => visibleBanners.map((banner, index) => ({ banner, analytics: bannerQueries[index]?.data as BannerAnalyticsDto | undefined })), [visibleBanners, bannerQueries]);
  const promotionRows = useMemo(() => visiblePromotions.map((campaign, index) => ({ campaign, analytics: promotionQueries[index]?.data as PromotionCampaignAnalyticsDto | undefined })), [visiblePromotions, promotionQueries]);
  const aggregate = (catalogAnalytics: Array<CatalogAnalytics | undefined>, bannerAnalytics: Array<BannerAnalyticsDto | undefined>) => {
    const catalog = catalogAnalytics.reduce((sum, analytics) => ({
      views: sum.views + (analytics?.catalogViews ?? 0),
      uniqueUsers: sum.uniqueUsers + (analytics?.uniqueUsers ?? 0),
      productViews: sum.productViews + (analytics?.productViews ?? 0),
      scans: sum.scans + (analytics?.productScans ?? 0),
      purchases: sum.purchases + (analytics?.purchases ?? 0),
      revenue: sum.revenue + (analytics?.revenue ?? 0),
    }), { views: 0, uniqueUsers: 0, productViews: 0, scans: 0, purchases: 0, revenue: 0 });
    const bannerViews = bannerAnalytics.reduce((sum, item) => sum + (item?.viewCount ?? 0), 0);
    const bannerClicks = bannerAnalytics.reduce((sum, item) => sum + (item?.clickCount ?? 0), 0);
    return { ...catalog, bannerViews, bannerClicks };
  };
  const totals = aggregate(catalogRows.map((row) => row.analytics), bannerRows.map((row) => row.analytics));
  const comparisonTotals = aggregate(comparisonCatalogQueries.map((query) => query.data), comparisonBannerQueries.map((query) => query.data));
  const percentageChange = (current: number, previous: number) => previous === 0 ? (current === 0 ? 0 : null) : ((current - previous) / previous) * 100;

  const allQueries = [...catalogQueries, ...bannerQueries, ...promotionQueries, ...(compareEnabled ? [...comparisonCatalogQueries, ...comparisonBannerQueries, ...comparisonPromotionQueries] : [])];
  const isLoading = !storeScopeReady || banners.isLoading || catalogs.isLoading || promotions.isLoading || allQueries.some((query) => query.isLoading);
  const hasError = banners.isError || catalogs.isError || promotions.isError || allQueries.some((query) => query.isError);
  const activePromotions = visiblePromotions.filter((item) => item.status === "published").length;
  const bannerCtr = totals.bannerViews ? (totals.bannerClicks / totals.bannerViews) * 100 : 0;
  const scanToPurchase = totals.scans ? (totals.purchases / totals.scans) * 100 : 0;
  const promotionTotals = promotionRows.reduce((sum, row) => ({ impressions: sum.impressions + (row.analytics?.impressions ?? 0), opens: sum.opens + (row.analytics?.opens ?? 0), receipts: sum.receipts + (row.analytics?.usedReceipts ?? 0), revenue: sum.revenue + (row.analytics?.revenue ?? 0) }), { impressions: 0, opens: 0, receipts: 0, revenue: 0 });
  const comparisonPromotionTotals = comparisonPromotionQueries.reduce((sum, query) => ({ impressions: sum.impressions + (query.data?.impressions ?? 0), opens: sum.opens + (query.data?.opens ?? 0), receipts: sum.receipts + (query.data?.usedReceipts ?? 0), revenue: sum.revenue + (query.data?.revenue ?? 0) }), { impressions: 0, opens: 0, receipts: 0, revenue: 0 });
  const comparisonRows = useMemo(() => {
    const rows: Array<{ id: string; type: Exclude<ComparisonType, "all">; title: string; reach: number; interactions: number; purchases: number | null; revenue: number | null; conversion: number; detail: Detail }> = [
      ...bannerRows.map(({ banner, analytics }) => ({ id: banner.id, type: "banner" as const, title: banner.title, reach: analytics?.viewCount ?? 0, interactions: analytics?.clickCount ?? 0, purchases: null, revenue: null, conversion: (analytics?.ctr ?? 0) * 100, detail: { kind: "banner" as const, title: banner.title, analytics } })),
      ...catalogRows.map(({ catalog, analytics }) => ({ id: catalog.id, type: "catalog" as const, title: catalog.title, reach: analytics?.catalogViews ?? 0, interactions: analytics?.productScans ?? 0, purchases: analytics?.purchases ?? 0, revenue: analytics?.revenue ?? 0, conversion: analytics?.conversionPercent ?? 0, detail: { kind: "catalog" as const, title: catalog.title, analytics } })),
      ...promotionRows.map(({ campaign, analytics }) => ({ id: campaign.id, type: "promotion" as const, title: campaign.title, reach: analytics?.impressions ?? 0, interactions: analytics?.opens ?? 0, purchases: analytics?.usedReceipts ?? 0, revenue: analytics?.revenue ?? 0, conversion: analytics?.conversionPercent ?? 0, detail: { kind: "promotion" as const, title: campaign.title, analytics } })),
    ];
    return rows.filter((row) => comparisonType === "all" || row.type === comparisonType).sort((a, b) => comparisonSort === "reach" ? b.reach - a.reach : comparisonSort === "conversion" ? b.conversion - a.conversion : (b.revenue ?? -1) - (a.revenue ?? -1));
  }, [bannerRows, catalogRows, promotionRows, comparisonSort, comparisonType]);
  const audienceRows = useMemo(() => {
    const aggregate = new Map<string, { key: string; label: string; tierId: string | null; reach: number; interactions: number; purchases: number; revenue: number }>();
    const add = (row: { key: string; label: string; tierId: string | null; reach: number; interactions: number; purchases: number; revenue: number }) => {
      const current = aggregate.get(row.key) ?? { key: row.key, label: row.label, tierId: row.tierId, reach: 0, interactions: 0, purchases: 0, revenue: 0 };
      current.reach += row.reach;
      current.interactions += row.interactions;
      current.purchases += row.purchases;
      current.revenue += row.revenue;
      aggregate.set(row.key, current);
    };
    catalogRows.forEach((row) => (row.analytics?.audience ?? []).forEach(add));
    promotionRows.forEach((row) => (row.analytics?.audience ?? []).forEach(add));
    const order = (key: string) => key === "all" ? 0 : key === "loyalty" ? 1 : key === "new" ? 2 : key === "returning" ? 3 : 4;
    return Array.from(aggregate.values()).sort((a, b) => order(a.key) - order(b.key) || b.revenue - a.revenue);
  }, [catalogRows, promotionRows]);

  const exportPayload = useMemo<AnalyticsExportPayload>(() => {
    const include = (type: Exclude<ComparisonType, "all">) => comparisonType === "all" || comparisonType === type;
    const typeNames = { banner: "Банер", catalog: "Каталог", promotion: "Акція" } as const;
    return {
      from,
      to,
      storeScope: selectedStoreIds.length === 0 ? "Усі магазини" : `Обрано магазинів: ${selectedStoreIds.length}`,
      contentType: comparisonType === "all" ? "Усі типи" : typeNames[comparisonType],
      generatedAt: new Date(),
      summary: [
        ...(include("banner") ? [
          { metric: "Перегляди банерів", value: totals.bannerViews, format: "number" as const },
          { metric: "Кліки банерів", value: totals.bannerClicks, format: "number" as const },
          { metric: "CTR банерів", value: bannerCtr / 100, format: "percent" as const },
        ] : []),
        ...(include("catalog") ? [
          { metric: "Перегляди каталогів", value: totals.views, format: "number" as const },
          { metric: "Відкриття товарів", value: totals.productViews, format: "number" as const },
          { metric: "Сканування товарів", value: totals.scans, format: "number" as const },
          { metric: "Покупки з каталогу", value: totals.purchases, format: "number" as const },
          { metric: "Дохід з каталогу", value: totals.revenue, format: "currency" as const },
          { metric: "Сканування → покупка", value: scanToPurchase / 100, format: "percent" as const },
        ] : []),
        ...(include("promotion") ? [
          { metric: "Покази акцій", value: promotionTotals.impressions, format: "number" as const },
          { metric: "Відкриття акцій", value: promotionTotals.opens, format: "number" as const },
          { metric: "Використані акційні пропозиції", value: promotionTotals.receipts, format: "number" as const },
          { metric: "Дохід від акцій", value: promotionTotals.revenue, format: "currency" as const },
        ] : []),
      ],
      content: comparisonRows.map((row) => ({ type: typeNames[row.type], title: row.title, reach: row.reach, interactions: row.interactions, purchases: row.purchases, conversionPercent: row.conversion, revenue: row.revenue })),
      daily: [
        ...(include("banner") ? bannerRows.flatMap(({ banner, analytics }) => (analytics?.daily ?? []).map((row) => ({ type: "Банер", title: banner.title, date: row.date, reach: row.views, interactions: row.clicks, purchases: 0, revenue: 0 }))) : []),
        ...(include("catalog") ? catalogRows.flatMap(({ catalog, analytics }) => (analytics?.daily ?? []).map((row) => ({ type: "Каталог", title: catalog.title, date: row.date, reach: row.catalogViews, interactions: row.scans, purchases: row.purchases, revenue: row.revenue }))) : []),
        ...(include("promotion") ? promotionRows.flatMap(({ campaign, analytics }) => (analytics?.daily ?? []).map((row) => ({ type: "Акція", title: campaign.title, date: row.date, reach: row.impressions, interactions: row.opens, purchases: row.usedReceipts, revenue: row.revenue }))) : []),
      ],
      stores: [
        ...(include("banner") ? bannerRows.flatMap(({ banner, analytics }) => (analytics?.stores ?? []).map((row) => ({ type: "Банер", title: banner.title, storeName: row.storeName, reach: row.views, interactions: row.clicks, purchases: 0, revenue: 0 }))) : []),
        ...(include("catalog") ? catalogRows.flatMap(({ catalog, analytics }) => (analytics?.stores ?? []).map((row) => ({ type: "Каталог", title: catalog.title, storeName: row.storeName, reach: row.catalogViews, interactions: row.scans, purchases: row.purchases, revenue: row.revenue }))) : []),
        ...(include("promotion") ? promotionRows.flatMap(({ campaign, analytics }) => (analytics?.stores ?? []).map((row) => ({ type: "Акція", title: campaign.title, storeName: row.storeName, reach: row.impressions, interactions: row.opens, purchases: row.usedReceipts, revenue: row.revenue }))) : []),
      ],
      products: [
        ...(include("catalog") ? catalogRows.flatMap(({ catalog, analytics }) => (analytics?.products ?? []).map((row) => ({ type: "Каталог", title: catalog.title, productName: row.productName, interactions: row.scans, purchases: row.purchases, revenue: row.revenue }))) : []),
        ...(include("promotion") ? promotionRows.flatMap(({ campaign, analytics }) => (analytics?.products ?? []).map((row) => ({ type: "Акція", title: campaign.title, productName: row.productName, interactions: 0, purchases: row.purchasedUnits, revenue: row.revenue }))) : []),
      ],
      audience: audienceRows.map((row) => ({ segment: row.label, reach: row.reach, interactions: row.interactions, purchases: row.purchases, revenue: row.revenue })),
    };
  }, [audienceRows, bannerCtr, bannerRows, catalogRows, comparisonRows, comparisonType, from, promotionRows, promotionTotals.impressions, promotionTotals.opens, promotionTotals.receipts, promotionTotals.revenue, scanToPurchase, selectedStoreIds.length, to, totals]);

  const handleXlsxExport = async () => {
    setIsExportingXlsx(true);
    try {
      await exportAnalyticsXlsx(exportPayload);
      toast.success("Звіт XLSX сформовано");
    } catch {
      toast.error("Не вдалося сформувати XLSX-звіт");
    } finally {
      setIsExportingXlsx(false);
    }
  };

  return (
    <section style={{ minWidth: 0 }}>
      <div style={{ ...panel, marginBottom: 16, display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: 18, flexWrap: "wrap" }}>
        <DateRangePicker range={range} onRangeChange={(next) => { setRange(next); if (compareEnabled) setCompareRange(computePreviousPeriod(next.from, next.to)); }} compareEnabled={compareEnabled} onCompareToggle={(enabled) => { setCompareEnabled(enabled); if (enabled) setCompareRange(computePreviousPeriod(range.from, range.to)); }} compareRange={compareRange} onCompareRangeChange={setCompareRange} />
        <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, color: "#94A3B8", background: "#10151D", border: "1px solid #1F2937", borderRadius: 8, padding: "8px 11px", fontSize: 12 }}><MapPin size={15} color="#60A5FA" /><span>{selectedStoreIds.length === 0 ? "Усі магазини" : `Обрано магазинів: ${selectedStoreIds.length}`}</span></div>
          <button type="button" onClick={() => { exportAnalyticsCsv(exportPayload); toast.success("Звіт CSV сформовано"); }} disabled={isLoading} style={exportButtonStyle}><Download size={14} />CSV</button>
          <button type="button" onClick={handleXlsxExport} disabled={isLoading || isExportingXlsx} style={exportButtonStyle}><FileSpreadsheet size={14} />{isExportingXlsx ? "Формування…" : "Excel"}</button>
        </div>
      </div>
      <SectionTabs
        items={[
          { key: "overview", label: "Огляд", icon: <BarChart3 size={15} /> },
          { key: "content", label: "Акції та контент", icon: <Target size={15} /> },
          { key: "funnel", label: "Воронка", icon: <ScanLine size={15} /> },
          { key: "comparison", label: "Порівняння", icon: <Target size={15} /> },
          { key: "audience", label: "Аудиторії", icon: <Users size={15} /> },
        ] as const}
        activeKey={tab}
        onChange={setTab}
        ariaLabel="Розділи аналітики застосунку"
      />

      {hasError && <div style={{ ...panel, borderColor: "#7F1D1D", color: "#FCA5A5", marginBottom: 14 }}>Частину аналітики не вдалося завантажити. Доступні дані показані нижче.</div>}
      {isLoading && <p style={{ color: "#64748B", fontSize: 13 }}>Завантаження аналітики…</p>}

      {tab === "overview" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12 }}>
            <Metric label="Перегляди контенту" value={number(totals.bannerViews + totals.views)} hint="Банери та опубліковані каталоги" icon={<Eye size={18} />} change={compareEnabled ? percentageChange(totals.bannerViews + totals.views, comparisonTotals.bannerViews + comparisonTotals.views) : undefined} />
            <Metric label="Взаємодії" value={number(totals.bannerClicks + totals.productViews)} hint="Кліки банерів і відкриття товарів" icon={<MousePointerClick size={18} />} change={compareEnabled ? percentageChange(totals.bannerClicks + totals.productViews, comparisonTotals.bannerClicks + comparisonTotals.productViews) : undefined} />
            <Metric label="Сканування товарів" value={number(totals.scans)} hint="Сканування пропозицій каталогу" icon={<ScanLine size={18} />} change={compareEnabled ? percentageChange(totals.scans, comparisonTotals.scans) : undefined} />
            <Metric label="Покупки з каталогу" value={number(totals.purchases)} hint={`Атрибутований дохід: ${money(totals.revenue)}`} icon={<ShoppingBag size={18} />} change={compareEnabled ? percentageChange(totals.purchases, comparisonTotals.purchases) : undefined} />
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))", gap: 12 }}>
            <div style={panel}><h2 style={{ color: "#E8EDF5", fontSize: 14, margin: "0 0 12px" }}>Стан мобільного контенту</h2><div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: 10 }}><SmallStat label="Банери" value={visibleBanners.length} /><SmallStat label="Каталоги" value={measurableCatalogs.length} /><SmallStat label="Активні акції" value={activePromotions} /></div></div>
            <div style={panel}><h2 style={{ color: "#E8EDF5", fontSize: 14, margin: "0 0 12px" }}>Що рахуємо окремо</h2><p style={{ ...muted, lineHeight: 1.65 }}>Цей розділ показує дії всередині застосунку та їх атрибуцію. Загальний оборот, усі POS-чеки й RFM-сегменти залишаються у відповідних розділах аналітики.</p></div>
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(190px, 1fr))", gap: 12 }}>
            <Metric label="Покази акцій" value={number(promotionTotals.impressions)} hint="Покази карток акцій у застосунку" icon={<Eye size={18} />} change={compareEnabled ? percentageChange(promotionTotals.impressions, comparisonPromotionTotals.impressions) : undefined} />
            <Metric label="Відкриття акцій" value={number(promotionTotals.opens)} hint={`Open rate: ${number(promotionTotals.impressions ? promotionTotals.opens / promotionTotals.impressions * 100 : 0)}%`} icon={<MousePointerClick size={18} />} change={compareEnabled ? percentageChange(promotionTotals.opens, comparisonPromotionTotals.opens) : undefined} />
            <Metric label="Використані пропозиції" value={number(promotionTotals.receipts)} hint="POS-чеки з товарами активних акцій" icon={<ShoppingBag size={18} />} change={compareEnabled ? percentageChange(promotionTotals.receipts, comparisonPromotionTotals.receipts) : undefined} />
            <Metric label="Дохід від акцій" value={money(promotionTotals.revenue)} hint="Продажі акційних товарів після ідентифікації" icon={<Target size={18} />} change={compareEnabled ? percentageChange(promotionTotals.revenue, comparisonPromotionTotals.revenue) : undefined} />
          </div>
        </div>
      )}

      {tab === "content" && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(360px, 1fr))", gap: 12 }}>
          <ContentPanel title="Банери" subtitle="Перегляди, кліки та CTR">
            {bannerRows.length === 0 ? <EmptyMetricNotice>Банерів для вибраних магазинів поки немає.</EmptyMetricNotice> : bannerRows.map(({ banner: item, analytics }) => <ContentRow key={item.id} title={item.title} status={item.lifecycleStatus === "running" ? "Активний" : item.lifecycleStatus === "draft" ? "Чернетка" : "Завершений"} metrics={`${number(analytics?.viewCount ?? 0)} переглядів · ${number(analytics?.clickCount ?? 0)} кліків · ${number((analytics?.ctr ?? 0) * 100)}% CTR`} onClick={() => setDetail({ kind: "banner", title: item.title, analytics })} />)}
          </ContentPanel>
          <ContentPanel title="Каталоги" subtitle="Від перегляду до покупки">
            {catalogRows.length === 0 ? <EmptyMetricNotice>Каталогів із доступною статистикою поки немає.</EmptyMetricNotice> : catalogRows.map(({ catalog, analytics }) => <ContentRow key={catalog.id} title={catalog.title} status={catalog.status === "published" ? "Опублікований" : catalog.status === "scheduled" ? "Запланований" : "Архів"} metrics={`${number(analytics?.catalogViews ?? 0)} переглядів · ${number(analytics?.productScans ?? 0)} сканувань · ${number(analytics?.purchases ?? 0)} покупок`} onClick={() => setDetail({ kind: "catalog", title: catalog.title, analytics })} />)}
          </ContentPanel>
          <ContentPanel title="Акції" subtitle="Покази, відкриття та реальні POS-чеки">
            {promotionRows.length === 0 ? <EmptyMetricNotice>Акцій для вибраних магазинів поки немає.</EmptyMetricNotice> : promotionRows.map(({ campaign, analytics }) => <ContentRow key={campaign.id} title={campaign.title} status={campaign.status === "published" ? "Опублікована" : campaign.status === "draft" ? "Чернетка" : "Скасована"} metrics={`${number(analytics?.impressions ?? 0)} показів · ${number(analytics?.opens ?? 0)} відкриттів · ${number(analytics?.usedReceipts ?? 0)} чеків · ${money(analytics?.revenue ?? 0)}`} onClick={() => setDetail({ kind: "promotion", title: campaign.title, analytics })} />)}
          </ContentPanel>
          {selectedStoreIds.length > 0 && <div style={{ gridColumn: "1 / -1", color: "#64748B", fontSize: 11 }}>Каталоги, покупки та сканування відфільтровано точно за магазином. Для історичних подій банерів магазин не записувався, тому їх показники фільтруються за періодом, а сам перелік — за магазинами, призначеними банеру.</div>}
        </div>
      )}

      {tab === "funnel" && (
        <div style={{ ...panel, display: "flex", flexDirection: "column", gap: 18 }}>
          <div><h2 style={{ color: "#E8EDF5", fontSize: 15, margin: 0 }}>Воронка каталогу</h2><p style={{ ...muted, marginTop: 5 }}>Покупка зараховується лише після сканування товару та його появи в реальному POS-чеку клієнта.</p></div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(150px, 1fr))", gap: 10 }}>
            <FunnelStep label="Переглянули каталог" value={totals.views} tone="#3B82F6" />
            <FunnelStep label="Відкрили товар" value={totals.productViews} tone="#6366F1" />
            <FunnelStep label="Просканували" value={totals.scans} tone="#8B5CF6" />
            <FunnelStep label="Придбали" value={totals.purchases} tone="#22C55E" />
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 10 }}>
            <SmallStat label="Сканування → покупка" value={`${number(scanToPurchase)}%`} />
            <SmallStat label="Дохід з атрибуцією" value={money(totals.revenue)} />
            <SmallStat label="CTR банерів" value={`${number(bannerCtr)}%`} />
          </div>
          <AttributionPolicyCard policy={catalogRows.find((row) => row.analytics?.attributionPolicy)?.analytics?.attributionPolicy ?? promotionRows.find((row) => row.analytics?.attributionPolicy)?.analytics?.attributionPolicy} />
        </div>
      )}
      {tab === "comparison" && <CampaignComparisonTable rows={comparisonRows} type={comparisonType} onTypeChange={setComparisonType} sort={comparisonSort} onSortChange={setComparisonSort} onOpen={setDetail} />}
      {tab === "audience" && <AudienceAnalyticsTable rows={audienceRows} />}
      {detail && <AnalyticsDetailPanel detail={detail} onClose={() => setDetail(null)} />}
    </section>
  );
}

function SmallStat({ label, value }: { label: string; value: number | string }) {
  return <div style={{ background: "#10151D", border: "1px solid #1F2937", borderRadius: 9, padding: 12 }}><strong style={{ color: "#E8EDF5", fontSize: 18, display: "block" }}>{value}</strong><span style={{ color: "#64748B", fontSize: 11 }}>{label}</span></div>;
}

function ContentPanel({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return <section style={{ ...panel, minWidth: 0 }}><div style={{ display: "flex", alignItems: "center", gap: 9, marginBottom: 14 }}><Smartphone size={17} color="#60A5FA" /><div><h2 style={{ color: "#E8EDF5", fontSize: 14, margin: 0 }}>{title}</h2><p style={muted}>{subtitle}</p></div></div><div style={{ display: "flex", flexDirection: "column", gap: 8 }}>{children}</div></section>;
}

function ContentRow({ title, status, metrics, onClick }: { title: string; status: string; metrics: string; onClick?: () => void }) {
  return <button type="button" onClick={onClick} style={{ width: "100%", background: "#10151D", border: "1px solid #1F2937", borderRadius: 9, padding: 12, display: "flex", justifyContent: "space-between", alignItems: "center", gap: 16, flexWrap: "wrap", cursor: onClick ? "pointer" : "default", textAlign: "left" }}><div><strong style={{ color: "#DCE4EF", fontSize: 13 }}>{title}</strong><div style={{ color: "#64748B", fontSize: 11, marginTop: 4 }}>{metrics}</div></div><span style={{ color: "#93C5FD", background: "rgba(59,130,246,.12)", borderRadius: 999, padding: "4px 8px", fontSize: 10, fontWeight: 700 }}>{status}</span></button>;
}

function FunnelStep({ label, value, tone }: { label: string; value: number; tone: string }) {
  return <div style={{ borderRadius: 10, padding: 15, background: `${tone}18`, border: `1px solid ${tone}55` }}><strong style={{ color: "#F8FAFC", fontSize: 22, display: "block" }}>{number(value)}</strong><span style={{ color: "#94A3B8", fontSize: 11 }}>{label}</span></div>;
}

function AttributionPolicyCard({ policy }: { policy?: { modelVersion: string; confidence: string; name: string; rules: string[]; limitation: string } }) {
  const rules = policy?.rules ?? ["У чеку вказана картка програми лояльності", "Товар входить до пропозиції", "Магазин і дата відповідають умовам", "Транзакцію не скасовано"];
  return <div style={{ background: "rgba(30,64,175,.08)", border: "1px solid rgba(59,130,246,.3)", borderRadius: 10, padding: 15 }}><div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}><div><strong style={{ color: "#BFDBFE", fontSize: 13 }}>Модель атрибуції: {policy?.name ?? "Ідентифікований POS-чек із товаром пропозиції"}</strong><p style={{ color: "#64748B", fontSize: 11, margin: "4px 0 0" }}>Версія {policy?.modelVersion ?? "loyalty-pos-v1"}</p></div><span style={{ alignSelf: "flex-start", color: "#FCD34D", background: "rgba(245,158,11,.12)", borderRadius: 999, padding: "4px 9px", fontSize: 10, fontWeight: 700 }}>Ймовірна атрибуція</span></div><div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(210px,1fr))", gap: 7, marginTop: 13 }}>{rules.map((rule) => <div key={rule} style={{ color: "#94A3B8", fontSize: 11 }}>✓ {rule}</div>)}</div><p style={{ color: "#64748B", fontSize: 11, lineHeight: 1.55, margin: "13px 0 0" }}>{policy?.limitation ?? "POS-рядок не містить ідентифікатора конкретної кампанії, тому результат показує використання пропозиції з високою ймовірністю."}</p></div>;
}

function AnalyticsDetailPanel({ detail, onClose }: { detail: Detail; onClose: () => void }) {
  const daily: Array<{ date: string; primary: number; secondary: number; purchases: number }> = detail.kind === "catalog"
    ? (detail.analytics?.daily ?? []).map((row) => ({ date: row.date, primary: row.catalogViews, secondary: row.scans, purchases: row.purchases }))
    : detail.kind === "promotion"
      ? (detail.analytics?.daily ?? []).map((row) => ({ date: row.date, primary: row.impressions, secondary: row.opens, purchases: row.usedReceipts }))
      : (detail.analytics?.daily ?? []).map((row) => ({ date: row.date, primary: row.views, secondary: row.clicks, purchases: 0 }));
  const stores = detail.analytics?.stores ?? [];
  const products = detail.kind === "catalog" ? detail.analytics?.products ?? [] : detail.kind === "promotion" ? detail.analytics?.products ?? [] : [];
  return <div style={{ position: "fixed", inset: 0, zIndex: 80, background: "rgba(2,6,12,.72)", display: "flex", justifyContent: "flex-end" }} onClick={onClose}>
    <aside onClick={(event) => event.stopPropagation()} style={{ width: "min(760px, 94vw)", height: "100%", overflowY: "auto", background: "#0B1017", borderLeft: "1px solid #253044", padding: 22, boxSizing: "border-box", boxShadow: "-18px 0 45px rgba(0,0,0,.35)" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16, marginBottom: 18 }}><div><span style={{ color: "#60A5FA", fontSize: 11, fontWeight: 700, textTransform: "uppercase" }}>Детальний звіт</span><h2 style={{ color: "#E8EDF5", fontSize: 19, margin: "5px 0 0" }}>{detail.title}</h2></div><button type="button" onClick={onClose} style={{ background: "transparent", border: "1px solid #263244", borderRadius: 8, color: "#94A3B8", padding: "7px 10px", cursor: "pointer" }}>✕</button></div>
      {detail.kind === "banner" ? <><div style={{ ...panel, marginBottom: 14 }}><div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 10 }}><SmallStat label="Перегляди" value={number(detail.analytics?.viewCount ?? 0)} /><SmallStat label="Кліки" value={number(detail.analytics?.clickCount ?? 0)} /><SmallStat label="CTR" value={`${number((detail.analytics?.ctr ?? 0) * 100)}%`} /></div></div><div style={{ ...panel, height: 300, marginBottom: 14 }}><h3 style={{ color: "#E8EDF5", fontSize: 13, margin: "0 0 14px" }}>Динаміка за днями</h3>{daily.length === 0 ? <EmptyMetricNotice>За цей період подій немає.</EmptyMetricNotice> : <ResponsiveContainer width="100%" height="85%"><LineChart data={daily}><CartesianGrid stroke="#1F2937" strokeDasharray="3 3" /><XAxis dataKey="date" tick={{ fill: "#64748B", fontSize: 10 }} /><YAxis tick={{ fill: "#64748B", fontSize: 10 }} /><Tooltip contentStyle={{ background: "#111827", border: "1px solid #334155", borderRadius: 8 }} /><Legend /><Line type="monotone" dataKey="primary" name="Перегляди" stroke="#3B82F6" strokeWidth={2} /><Line type="monotone" dataKey="secondary" name="Кліки" stroke="#A78BFA" strokeWidth={2} /></LineChart></ResponsiveContainer>}</div><BreakdownTable title="За магазинами" rows={stores.map((row) => ({ name: row.storeName, activity: "clicks" in row ? row.clicks : 0, purchases: 0, revenue: 0 }))} /><p style={{ ...muted, lineHeight: 1.6 }}>Події, записані до оновлення трекінгу, враховуються в загальному підсумку, але не приписуються магазину.</p></> : <>
        <div style={{ ...panel, height: 300, marginBottom: 14 }}><h3 style={{ color: "#E8EDF5", fontSize: 13, margin: "0 0 14px" }}>Динаміка за днями</h3>{daily.length === 0 ? <EmptyMetricNotice>За цей період подій немає.</EmptyMetricNotice> : <ResponsiveContainer width="100%" height="85%"><LineChart data={daily}><CartesianGrid stroke="#1F2937" strokeDasharray="3 3" /><XAxis dataKey="date" tick={{ fill: "#64748B", fontSize: 10 }} tickFormatter={(value) => new Date(value).toLocaleDateString("uk-UA", { day: "2-digit", month: "2-digit" })} /><YAxis tick={{ fill: "#64748B", fontSize: 10 }} /><Tooltip contentStyle={{ background: "#111827", border: "1px solid #334155", borderRadius: 8, color: "#E5E7EB" }} /><Legend /><Line type="monotone" dataKey="primary" name="Покази" stroke="#3B82F6" strokeWidth={2} /><Line type="monotone" dataKey="secondary" name={detail.kind === "catalog" ? "Сканування" : "Відкриття"} stroke="#A78BFA" strokeWidth={2} /><Line type="monotone" dataKey="purchases" name="Покупки" stroke="#22C55E" strokeWidth={2} /></LineChart></ResponsiveContainer>}</div>
        <BreakdownTable title="За магазинами" rows={stores.map((row) => ({ name: row.storeName, activity: detail.kind === "catalog" && "scans" in row ? row.scans : "opens" in row ? row.opens : 0, purchases: detail.kind === "catalog" && "purchases" in row ? row.purchases : "usedReceipts" in row ? row.usedReceipts : 0, revenue: "revenue" in row ? row.revenue : 0 }))} />
        <BreakdownTable title="За товарами" rows={products.map((row) => ({ name: "productName" in row ? row.productName : "Товар", activity: "views" in row ? row.views : 0, purchases: "purchases" in row ? row.purchases : "purchasedUnits" in row ? row.purchasedUnits : 0, revenue: row.revenue }))} />
        <AttributionPolicyCard policy={detail.analytics?.attributionPolicy} />
      </>}
    </aside>
  </div>;
}

function BreakdownTable({ title, rows }: { title: string; rows: Array<{ name: string; activity: number; purchases: number; revenue: number }> }) {
  return <div style={{ ...panel, marginBottom: 14 }}><h3 style={{ color: "#E8EDF5", fontSize: 13, margin: "0 0 12px" }}>{title}</h3>{rows.length === 0 ? <p style={muted}>Даних поки немає.</p> : <div style={{ overflowX: "auto" }}><table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}><thead><tr style={{ color: "#64748B", textAlign: "left" }}><th style={{ padding: "8px 6px" }}>Назва</th><th>Взаємодії</th><th>Покупки</th><th style={{ textAlign: "right" }}>Дохід</th></tr></thead><tbody>{rows.map((row) => <tr key={row.name} style={{ borderTop: "1px solid #1F2937", color: "#CBD5E1" }}><td style={{ padding: "10px 6px", fontWeight: 600 }}>{row.name}</td><td>{number(row.activity)}</td><td>{number(row.purchases)}</td><td style={{ textAlign: "right" }}>{money(row.revenue)}</td></tr>)}</tbody></table></div>}</div>;
}

function CampaignComparisonTable({ rows, type, onTypeChange, sort, onSortChange, onOpen }: {
  rows: Array<{ id: string; type: Exclude<ComparisonType, "all">; title: string; reach: number; interactions: number; purchases: number | null; revenue: number | null; conversion: number; detail: Detail }>;
  type: ComparisonType; onTypeChange: (value: ComparisonType) => void;
  sort: "revenue" | "conversion" | "reach"; onSortChange: (value: "revenue" | "conversion" | "reach") => void;
  onOpen: (detail: Detail) => void;
}) {
  const typeLabel = { banner: "Банер", catalog: "Каталог", promotion: "Акція" } as const;
  return <div style={panel}>
    <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", gap: 14, flexWrap: "wrap", marginBottom: 16 }}><div><h2 style={{ color: "#E8EDF5", fontSize: 15, margin: 0 }}>Ефективність мобільного контенту</h2><p style={{ ...muted, marginTop: 5 }}>Показники розраховано за вибраний зверху період і магазини</p></div><div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}><label style={{ color: "#64748B", fontSize: 11 }}>Тип<select value={type} onChange={(event) => onTypeChange(event.target.value as ComparisonType)} style={comparisonSelect}><option value="all">Усі</option><option value="promotion">Акції</option><option value="catalog">Каталоги</option><option value="banner">Банери</option></select></label><label style={{ color: "#64748B", fontSize: 11 }}>Сортування<select value={sort} onChange={(event) => onSortChange(event.target.value as typeof sort)} style={comparisonSelect}><option value="revenue">За доходом</option><option value="conversion">За конверсією</option><option value="reach">За охопленням</option></select></label></div></div>
    {rows.length === 0 ? <EmptyMetricNotice>За вибраними умовами кампаній немає.</EmptyMetricNotice> : <div style={{ overflowX: "auto" }}><table style={{ width: "100%", minWidth: 760, borderCollapse: "collapse", fontSize: 12 }}><thead><tr style={{ color: "#64748B", textAlign: "left", borderBottom: "1px solid #263244" }}><th style={comparisonHead}>Контент</th><th style={comparisonHead}>Тип</th><th style={comparisonHead}>Охоплення</th><th style={comparisonHead}>Взаємодії</th><th style={comparisonHead}>Покупки/чеки</th><th style={comparisonHead}>Конверсія</th><th style={{ ...comparisonHead, textAlign: "right" }}>Дохід</th></tr></thead><tbody>{rows.map((row, index) => <tr key={`${row.type}-${row.id}`} onClick={() => onOpen(row.detail)} style={{ borderBottom: "1px solid #182130", color: "#CBD5E1", cursor: "pointer", background: index === 0 ? "rgba(59,130,246,.055)" : "transparent" }}><td style={{ padding: "12px 8px", fontWeight: 650 }}><span style={{ color: "#64748B", marginRight: 8 }}>#{index + 1}</span>{row.title}</td><td><span style={{ color: "#93C5FD", background: "rgba(59,130,246,.12)", borderRadius: 999, padding: "4px 8px", fontSize: 10 }}>{typeLabel[row.type]}</span></td><td>{number(row.reach)}</td><td>{number(row.interactions)}</td><td>{row.purchases === null ? "—" : number(row.purchases)}</td><td><strong style={{ color: row.conversion >= 10 ? "#4ADE80" : "#FBBF24" }}>{number(row.conversion)}%</strong></td><td style={{ textAlign: "right", fontWeight: 650 }}>{row.revenue === null ? "—" : money(row.revenue)}</td></tr>)}</tbody></table></div>}
    <p style={{ ...muted, marginTop: 13 }}>Для банерів покупки й дохід не визначаються, оскільки банер може вести на різні типи контенту. Натисніть рядок, щоб відкрити детальний звіт.</p>
  </div>;
}

function AudienceAnalyticsTable({ rows }: { rows: Array<{ key: string; label: string; tierId: string | null; reach: number; interactions: number; purchases: number; revenue: number }> }) {
  const newCustomers = rows.find((row) => row.key === "new");
  const returningCustomers = rows.find((row) => row.key === "returning");
  const loyalty = rows.find((row) => row.key === "loyalty");
  return <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))", gap: 12 }}>
      <Metric label="Учасники лояльності" value={number(loyalty?.purchases ?? 0)} hint={`Дохід: ${money(loyalty?.revenue ?? 0)}`} icon={<Users size={18} />} />
      <Metric label="Нові покупці" value={number(newCustomers?.purchases ?? 0)} hint={`Перший чек у вибраному періоді · ${money(newCustomers?.revenue ?? 0)}`} icon={<ShoppingBag size={18} />} />
      <Metric label="Постійні покупці" value={number(returningCustomers?.purchases ?? 0)} hint={`Мали покупки раніше · ${money(returningCustomers?.revenue ?? 0)}`} icon={<Target size={18} />} />
    </div>
    <div style={panel}>
      <div style={{ marginBottom: 14 }}><h2 style={{ color: "#E8EDF5", fontSize: 15, margin: 0 }}>Ефективність за аудиторіями</h2><p style={{ ...muted, marginTop: 5 }}>Охоплення та взаємодії підсумовуються по каталогах і акціях; покупки й дохід надходять тільки з реальних POS-чеків.</p></div>
      {rows.length === 0 ? <EmptyMetricNotice>За вибраний період даних для сегментації ще немає.</EmptyMetricNotice> : <div style={{ overflowX: "auto" }}><table style={{ width: "100%", minWidth: 720, borderCollapse: "collapse", fontSize: 12 }}><thead><tr style={{ color: "#64748B", textAlign: "left", borderBottom: "1px solid #263244" }}><th style={comparisonHead}>Сегмент</th><th style={comparisonHead}>Тип</th><th style={comparisonHead}>Охоплення</th><th style={comparisonHead}>Взаємодії</th><th style={comparisonHead}>Покупки/чеки</th><th style={comparisonHead}>Конверсія</th><th style={{ ...comparisonHead, textAlign: "right" }}>Дохід</th></tr></thead><tbody>{rows.map((row) => {
        const conversion = row.interactions > 0 ? row.purchases / row.interactions * 100 : 0;
        return <tr key={row.key} style={{ borderBottom: "1px solid #182130", color: "#CBD5E1" }}><td style={{ padding: "12px 8px", fontWeight: 650 }}>{row.label}</td><td><span style={{ color: row.tierId ? "#C4B5FD" : "#93C5FD", background: row.tierId ? "rgba(139,92,246,.12)" : "rgba(59,130,246,.12)", borderRadius: 999, padding: "4px 8px", fontSize: 10 }}>{row.tierId ? "Рівень" : "Сегмент"}</span></td><td>{number(row.reach)}</td><td>{number(row.interactions)}</td><td>{number(row.purchases)}</td><td>{number(conversion)}%</td><td style={{ textAlign: "right", fontWeight: 650 }}>{money(row.revenue)}</td></tr>;
      })}</tbody></table></div>}
      <p style={{ ...muted, marginTop: 13 }}>Один клієнт одночасно входить до загального сегмента, програми лояльності та свого рівня. Тому рядки не потрібно складати між собою.</p>
    </div>
  </div>;
}

const comparisonSelect: CSSProperties = { display: "block", marginTop: 4, minWidth: 145, background: "#10151D", border: "1px solid #263244", borderRadius: 7, color: "#CBD5E1", padding: "7px 9px", fontSize: 12, outline: "none" };
const comparisonHead: CSSProperties = { padding: "9px 8px", fontWeight: 600, whiteSpace: "nowrap" };
const exportButtonStyle: CSSProperties = { display: "inline-flex", alignItems: "center", gap: 6, minHeight: 35, padding: "7px 11px", borderRadius: 8, border: "1px solid #2B3A50", background: "#101722", color: "#BFDBFE", fontSize: 12, fontWeight: 650, cursor: "pointer" };
