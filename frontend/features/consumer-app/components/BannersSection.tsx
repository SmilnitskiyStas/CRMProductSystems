"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { API_BASE } from "@/lib/api";
import { useBannerAnalytics, useBanners, useDeactivateBanner, usePublishBanner } from "../hooks/useBanners";
import { LifecycleTabs, type LifecycleTab } from "./LifecycleTabs";
import type { BannerDto } from "../types";

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
};

const rowStyle: React.CSSProperties = {
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: 14,
  display: "flex",
  flexDirection: "column",
  gap: 8,
};

function resolveImageUrl(value: string | null): string | null {
  if (!value) return null;
  if (/^https?:\/\//i.test(value)) return value;
  return `${API_BASE.replace(/\/$/, "")}/${value.replace(/^\//, "")}`;
}

function formatDate(value: string | null): string {
  if (!value) return "—";
  return new Date(value).toLocaleDateString();
}

/**
 * TASK-522: banner list + create/edit form + per-row analytics popover. First of the three
 * sections added to /consumer-app under BonusProgramSection.
 *
 * TASK-525: added the Активні/Минулі/Чернетки history tabs (filtering `lifecycleStatus`
 * client-side — GET /api/banners already returns the tenant's full history, no per-tab
 * fetch) plus a row-level "Опублікувати" action for draft rows.
 */
export function BannersSection() {
  const t = useTranslations("Dashboard.consumerApp.banners");
  const router = useRouter();
  const { data: banners, isLoading, isError } = useBanners();
  const deactivate = useDeactivateBanner();
  const publish = usePublishBanner();

  const [analyticsOpenId, setAnalyticsOpenId] = useState<string | null>(null);
  const [tab, setTab] = useState<LifecycleTab>("running");

  const counts = useMemo(() => {
    const c: Partial<Record<LifecycleTab, number>> = { running: 0, past: 0, draft: 0 };
    for (const b of banners ?? []) c[b.lifecycleStatus] = (c[b.lifecycleStatus] ?? 0) + 1;
    return c;
  }, [banners]);

  const visibleBanners = useMemo(
    () => (banners ?? []).filter((b) => b.lifecycleStatus === tab),
    [banners, tab],
  );

  async function handleDeactivate(banner: BannerDto) {
    if (!window.confirm(t("deactivateConfirm", { title: banner.title }))) return;
    try {
      await deactivate.mutateAsync(banner.id);
      toast.success(t("deactivateSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("deactivateError"));
    }
  }

  async function handlePublish(banner: BannerDto) {
    try {
      await publish.mutateAsync(banner.id);
      toast.success(t("publishSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("publishError"));
    }
  }

  return (
    <div style={cardStyle}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <span style={{ fontSize: 24 }}>📣</span>
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3 }}>{t("subtitle")}</p>
          </div>
        </div>
        <Btn icon={<span aria-hidden="true">＋</span>} onClick={() => router.push("/consumer-app/banners/new")}>
          {t("createButton")}
        </Btn>
      </div>

      <LifecycleTabs
        tab={tab}
        onTabChange={setTab}
        counts={counts}
        labels={{ running: t("tabRunning"), past: t("tabPast"), draft: t("tabDraft") }}
      />

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>}

      {!isLoading && !isError && visibleBanners.length === 0 && (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("emptyHint")}</div>
      )}

      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(min(100%, 460px), 1fr))", gap: 12 }}>
        {visibleBanners.map((banner) => {
          const isDraft = banner.lifecycleStatus === "draft";
          return (
          <div key={banner.id} style={rowStyle}>
            <div style={{ display: "flex", gap: 12, minWidth: 0 }}>
              <div
                style={{
                  width: 116,
                  height: 76,
                  flexShrink: 0,
                  overflow: "hidden",
                  borderRadius: 8,
                  border: "1px solid #293241",
                  background: banner.backgroundColor || "#111827",
                  backgroundImage: banner.imageUrl ? `url("${resolveImageUrl(banner.imageUrl)}")` : undefined,
                  backgroundSize: "cover",
                  backgroundPosition: "center",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                }}
              >
                {!banner.imageUrl && (
                  <span style={{ color: banner.accentColor || "#9CA3AF", fontSize: 24 }}>📣</span>
                )}
              </div>
              <div style={{ minWidth: 0, flex: 1 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
                <span
                  style={{
                    fontSize: 10, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em",
                    padding: "3px 8px", borderRadius: 999,
                    color: isDraft ? "#9CA3AF" : banner.isCurrentlyActive ? "#4ADE80" : "#9CA3AF",
                    background: isDraft ? "#1F2937" : banner.isCurrentlyActive ? "#0F2D1A" : "#1F2937",
                    border: `1px solid ${isDraft ? "#374151" : banner.isCurrentlyActive ? "#166534" : "#374151"}`,
                    flexShrink: 0,
                  }}
                >
                  {isDraft ? t("statusDraft") : banner.isCurrentlyActive ? t("statusActive") : t("statusPaused")}
                </span>
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {banner.title}
                </span>
                </div>
                <p style={{ color: "#6B7280", fontSize: 12, lineHeight: 1.45, margin: 0, display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical", overflow: "hidden" }}>
                  {banner.description}
                </p>
              </div>
            </div>

            <div style={{ display: "flex", gap: 16, flexWrap: "wrap", color: "#6B7280", fontSize: 12 }}>
              <span>{t("locationsCount", { count: banner.locationIds.length })}</span>
              <span>{formatDate(banner.validFrom)} — {formatDate(banner.validUntil)}</span>
              <span>{t("viewCount", { count: banner.viewCount })}</span>
              <span>{t("clickCount", { count: banner.clickCount })}</span>
            </div>

              <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginTop: "auto" }}>
                <Btn
                  size="sm"
                  variant="ghost"
                  onClick={() => setAnalyticsOpenId((prev) => (prev === banner.id ? null : banner.id))}
                >
                  {t("analyticsButton")}
                </Btn>
                <Btn size="sm" variant="ghost" onClick={() => router.push(`/consumer-app/banners/${banner.id}/edit`)}>
                  {t("editButton")}
                </Btn>
                {isDraft && (
                  <Btn size="sm" variant="success" onClick={() => handlePublish(banner)} disabled={publish.isPending}>
                    {t("publishButton")}
                  </Btn>
                )}
                <Btn size="sm" variant="danger" onClick={() => handleDeactivate(banner)} disabled={deactivate.isPending}>
                  {t("deleteButton")}
                </Btn>
              </div>

            {analyticsOpenId === banner.id && <AnalyticsPanel bannerId={banner.id} />}
          </div>
          );
        })}
      </div>
    </div>
  );
}

function AnalyticsPanel({ bannerId }: { bannerId: string }) {
  const t = useTranslations("Dashboard.consumerApp.banners");
  const { data, isLoading, isError } = useBannerAnalytics(bannerId);

  return (
    <div style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 8, padding: "10px 12px", display: "flex", gap: 20 }}>
      {isLoading && <span style={{ color: "#4B5563", fontSize: 12 }}>{t("loading")}</span>}
      {isError && <span style={{ color: "#F87171", fontSize: 12 }}>{t("loadError")}</span>}
      {data && (
        <>
          <AnalyticsStat label={t("analyticsViews")} value={data.viewCount} />
          <AnalyticsStat label={t("analyticsClicks")} value={data.clickCount} />
          <AnalyticsStat label={t("analyticsCtr")} value={`${(data.ctr * 100).toFixed(1)}%`} />
        </>
      )}
    </div>
  );
}

function AnalyticsStat({ label, value }: { label: string; value: number | string }) {
  return (
    <div>
      <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700 }}>{value}</div>
      <div style={{ color: "#4B5563", fontSize: 11 }}>{label}</div>
    </div>
  );
}
