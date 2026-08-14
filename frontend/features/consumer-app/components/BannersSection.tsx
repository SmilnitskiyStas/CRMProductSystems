"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useBannerAnalytics, useBanners, useDeactivateBanner } from "../hooks/useBanners";
import { BannerForm } from "./BannerForm";
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
  padding: "12px 14px",
  display: "flex",
  flexDirection: "column",
  gap: 8,
};

function formatDate(value: string | null): string {
  if (!value) return "—";
  return new Date(value).toLocaleDateString();
}

/**
 * TASK-522: banner list + create/edit form + per-row analytics popover. First of the three
 * sections added to /consumer-app under BonusProgramSection.
 */
export function BannersSection() {
  const t = useTranslations("Dashboard.consumerApp.banners");
  const { data: banners, isLoading, isError } = useBanners();
  const deactivate = useDeactivateBanner();

  // false = closed, "create" = create mode, otherwise the id being edited.
  const [formTarget, setFormTarget] = useState<false | "create" | string>(false);
  const [analyticsOpenId, setAnalyticsOpenId] = useState<string | null>(null);

  async function handleDeactivate(banner: BannerDto) {
    if (!window.confirm(t("deactivateConfirm", { title: banner.title }))) return;
    try {
      await deactivate.mutateAsync(banner.id);
      toast.success(t("deactivateSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("deactivateError"));
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
        <Btn size="sm" onClick={() => setFormTarget("create")}>{t("createButton")}</Btn>
      </div>

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>}

      {!isLoading && !isError && (banners ?? []).length === 0 && (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("emptyHint")}</div>
      )}

      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {(banners ?? []).map((banner) => (
          <div key={banner.id} style={rowStyle}>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
                <span
                  style={{
                    fontSize: 10, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em",
                    padding: "3px 8px", borderRadius: 999,
                    color: banner.isCurrentlyActive ? "#4ADE80" : "#9CA3AF",
                    background: banner.isCurrentlyActive ? "#0F2D1A" : "#1F2937",
                    border: `1px solid ${banner.isCurrentlyActive ? "#166534" : "#374151"}`,
                    flexShrink: 0,
                  }}
                >
                  {banner.isCurrentlyActive ? t("statusActive") : t("statusPaused")}
                </span>
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {banner.title}
                </span>
              </div>
              <div style={{ display: "flex", gap: 8, flexShrink: 0 }}>
                <Btn
                  size="sm"
                  variant="ghost"
                  onClick={() => setAnalyticsOpenId((prev) => (prev === banner.id ? null : banner.id))}
                >
                  {t("analyticsButton")}
                </Btn>
                <Btn size="sm" variant="ghost" onClick={() => setFormTarget(banner.id)}>
                  {t("editButton")}
                </Btn>
                <Btn size="sm" variant="danger" onClick={() => handleDeactivate(banner)} disabled={deactivate.isPending}>
                  {t("deleteButton")}
                </Btn>
              </div>
            </div>

            <div style={{ display: "flex", gap: 16, flexWrap: "wrap", color: "#6B7280", fontSize: 12 }}>
              <span>{t("locationsCount", { count: banner.locationIds.length })}</span>
              <span>{formatDate(banner.validFrom)} — {formatDate(banner.validUntil)}</span>
              <span>{t("viewCount", { count: banner.viewCount })}</span>
              <span>{t("clickCount", { count: banner.clickCount })}</span>
            </div>

            {analyticsOpenId === banner.id && <AnalyticsPanel bannerId={banner.id} />}
          </div>
        ))}
      </div>

      {formTarget !== false && (
        <BannerForm
          bannerId={formTarget === "create" ? null : formTarget}
          onClose={() => setFormTarget(false)}
        />
      )}
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
