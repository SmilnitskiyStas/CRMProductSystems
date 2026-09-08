"use client";

import { useLocale, useTranslations } from "next-intl";
import { Loader2, RefreshCw, Sparkles, X } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { CollapsibleSection } from "@/components/ui/CollapsibleSection";
import { useStores } from "@/features/stores/hooks/useStores";
import { useStoreContext } from "@/lib/useStoreContext";
import {
  useCancelWeatherPromoDiscounts,
  useRefreshWeatherPromo,
  useWeatherPromoActiveDiscounts,
  useWeatherPromoSuggestions,
} from "../hooks/useWeatherPromo";
import { SuggestionCard } from "./SuggestionCard";

/** Relative-time string for a past ISO timestamp, localized via Intl.RelativeTimeFormat. */
function relativeTime(iso: string, locale: string): string {
  const diffSec = Math.round((new Date(iso).getTime() - Date.now()) / 1000);
  const rtf = new Intl.RelativeTimeFormat(locale === "en" ? "en" : "uk", { numeric: "auto" });
  const abs = Math.abs(diffSec);
  if (abs < 60) return rtf.format(Math.round(diffSec), "second");
  if (abs < 3600) return rtf.format(Math.round(diffSec / 60), "minute");
  if (abs < 86_400) return rtf.format(Math.round(diffSec / 3600), "hour");
  return rtf.format(Math.round(diffSec / 86_400), "day");
}

/**
 * Full weekly weather-promo panel for `/events`. Self-hides when the AI Analyst agent is not
 * configured. The only surface that spends an AI call — via the explicit refresh / generate
 * button (or the backend's 12h cache on first load).
 */
export function WeatherPromoPanel() {
  const t = useTranslations("Dashboard.weatherPromo");
  const locale = useLocale();

  const { data, isLoading, isError } = useWeatherPromoSuggestions();
  const refresh = useRefreshWeatherPromo();
  const { data: stores = [] } = useStores();
  const defaultStoreIds = useStoreContext((s) => s.selectedStoreIds);

  const notConfigured = data?.status === "not_configured";
  // Running campaigns are listed even when the AI agent isn't configured — otherwise a manager
  // who created discounts here would lose the only way to cancel them (the "Акційні товари"
  // screen is `mobile_app`-gated). 403 (no `inventory` module) still disables the query.
  const { data: activeDiscounts = [] } = useWeatherPromoActiveDiscounts({ enabled: !isError });
  const cancelDiscounts = useCancelWeatherPromoDiscounts();

  function cancelOne(id: string) {
    cancelDiscounts.mutate([id], {
      onSuccess: (r) => {
        toast.success(t("discountCancelled"));
        r.warnings.forEach((w) => toast.warning(w));
      },
      onError: (err) => toast.error(err.message),
    });
  }

  // A hard error (403 no `inventory` module / 502 AI down) hides everything.
  if (isError) return null;
  if (isLoading && !data) return null;
  if (!data) return null;
  // When the AI agent isn't set up, still show the panel *if* there are running campaigns to manage.
  if (notConfigured && activeDiscounts.length === 0) return null;

  const isOk = data.status === "ok";
  const hasSuggestions = isOk && data.suggestions.length > 0;
  const pending = refresh.isPending;

  function runRefresh() {
    refresh.mutate(undefined, {
      onError: (err) => toast.error(err.message),
    });
  }

  return (
    <div style={{ marginBottom: 18 }}>
      <CollapsibleSection title={t("title")} defaultOpen>
        {/* Controls row — hidden when the AI agent isn't configured (panel is then just the
            running-campaigns list so a manager can still cancel). */}
        {!notConfigured && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            flexWrap: "wrap",
            gap: 10,
            marginBottom: hasSuggestions || data.status === "insufficient_data" ? 14 : 0,
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 8, color: "#93C5FD", fontSize: 12 }}>
            <Sparkles size={14} style={{ flexShrink: 0 }} />
            {isOk && data.generatedAt && (
              <span style={{ color: "#6B7280" }}>
                {t("updatedAt", { time: relativeTime(data.generatedAt, locale) })}
              </span>
            )}
          </div>
          <Btn
            variant={isOk && data.generatedAt ? "ghost" : "primary"}
            size="sm"
            disabled={pending}
            icon={
              pending ? (
                <Loader2 size={13} style={{ animation: "spin 1s linear infinite" }} />
              ) : (
                <RefreshCw size={13} />
              )
            }
            onClick={runRefresh}
          >
            {isOk && data.generatedAt ? t("refresh") : t("generate")}
          </Btn>
        </div>
        )}

        {/* Running promo campaigns — visible even without mobile_app (that screen is gated) */}
        {activeDiscounts.length > 0 && (
          <div
            style={{
              background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8,
              padding: "10px 12px", marginBottom: 14,
            }}
          >
            <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 8 }}>
              {t("activeDiscountsTitle", { count: activeDiscounts.length })}
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              {activeDiscounts.map((d) => (
                <div key={d.id} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12 }}>
                  <span style={{ color: "#E8EDF5", flex: 1, minWidth: 0, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                    {d.productName} · {d.storeName}
                  </span>
                  <span style={{ color: "#FCD34D", flexShrink: 0 }}>−{d.discountPercent}%</span>
                  <span style={{ color: "#6B7280", flexShrink: 0 }}>
                    {d.validFrom}{d.validUntil ? ` – ${d.validUntil}` : ""}
                  </span>
                  <button
                    type="button"
                    onClick={() => cancelOne(d.id)}
                    disabled={cancelDiscounts.isPending}
                    title={t("cancelDiscount")}
                    aria-label={t("cancelDiscount")}
                    style={{
                      background: "transparent", border: "none", color: "#6B7280",
                      cursor: cancelDiscounts.isPending ? "not-allowed" : "pointer",
                      padding: 2, flexShrink: 0, display: "flex",
                    }}
                  >
                    <X size={13} />
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Body by status */}
        {!notConfigured && data.status === "insufficient_data" && (
          <p style={{ color: "#6B7280", fontSize: 13, margin: 0, lineHeight: 1.5 }}>
            {t("insufficientData")}
          </p>
        )}

        {!notConfigured && isOk && !hasSuggestions && (
          <p style={{ color: "#6B7280", fontSize: 13, margin: 0 }}>{t("noSuggestions")}</p>
        )}

        {hasSuggestions && (
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            {data.suggestions.map((s, i) => (
              <SuggestionCard
                key={`${s.title}-${s.startsAt}-${i}`}
                suggestion={s}
                stores={stores}
                defaultStoreIds={defaultStoreIds}
              />
            ))}
          </div>
        )}
      </CollapsibleSection>

      <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}
