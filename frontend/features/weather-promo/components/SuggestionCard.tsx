"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { toast } from "sonner";
import { CalendarClock, Check, CloudSun } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";
import type { LocationDto } from "@/features/locations/types";
import { useApplyWeatherPromo } from "../hooks/useWeatherPromo";
import type { ApplyWeatherPromoResult, WeatherPromoConfidence, WeatherPromoSuggestion } from "../types";

interface Props {
  suggestion: WeatherPromoSuggestion;
  /** Active locations for the store multi-select. */
  stores: LocationDto[];
  /** Global header store selection — the card's default store scope. Empty = all stores. */
  defaultStoreIds: string[];
}

const MIN_PCT = 1;
const MAX_PCT = 90;

const CONFIDENCE_STYLE: Record<WeatherPromoConfidence, { bg: string; color: string }> = {
  high: { bg: "#1a3a2e", color: "#4ADE80" },
  medium: { bg: "#78350F", color: "#FBBF24" },
  low: { bg: "#374151", color: "#9CA3AF" },
};

const cardStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: 16,
  display: "flex",
  flexDirection: "column",
  gap: 12,
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 5,
};

const checkboxRowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 8,
  color: "#CBD5E1",
  fontSize: 13,
  cursor: "pointer",
};

export function SuggestionCard({ suggestion, stores, defaultStoreIds }: Props) {
  const t = useTranslations("Dashboard.weatherPromo");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const apply = useApplyWeatherPromo();

  const [discountPct, setDiscountPct] = useState(() =>
    clamp(Math.round(suggestion.recommendedDiscountPct)),
  );
  const [selectedProductIds, setSelectedProductIds] = useState<string[]>(() =>
    suggestion.products.map((p) => p.itemId),
  );
  const [storeIds, setStoreIds] = useState<string[]>(defaultStoreIds);
  const [createDiscounts, setCreateDiscounts] = useState(true);
  const [createCalendarEvent, setCreateCalendarEvent] = useState(true);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [result, setResult] = useState<ApplyWeatherPromoResult | null>(null);

  const activeStores = useMemo(() => stores.filter((s) => s.isActive), [stores]);
  const conf = CONFIDENCE_STYLE[suggestion.confidence] ?? CONFIDENCE_STYLE.low;

  const canApply =
    selectedProductIds.length > 0 &&
    (createDiscounts || createCalendarEvent) &&
    discountPct >= MIN_PCT &&
    discountPct <= MAX_PCT;

  const storeCountLabel =
    storeIds.length > 0 ? String(storeIds.length) : t("allStores");

  function toggleProduct(itemId: string) {
    setSelectedProductIds((prev) =>
      prev.includes(itemId) ? prev.filter((x) => x !== itemId) : [...prev, itemId],
    );
  }

  function toggleStore(id: string) {
    setStoreIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function runApply() {
    apply.mutate(
      {
        title: suggestion.title,
        startsAt: suggestion.startsAt,
        endsAt: suggestion.endsAt,
        discountPct,
        storeIds,
        productIds: selectedProductIds,
        createCalendarEvent,
        createDiscounts,
      },
      {
        onSuccess: (res) => {
          setConfirmOpen(false);
          setResult(res);
          toast.success(t("toastCreated", { discounts: res.discountIds.length }));
          for (const w of res.warnings) toast.warning(w);
        },
        onError: (err) => toast.error(err.message),
      },
    );
  }

  return (
    <div style={cardStyle}>
      {/* Title + confidence */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 10 }}>
        <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{suggestion.title}</span>
        <span
          style={{
            flexShrink: 0,
            fontSize: 10,
            fontWeight: 700,
            textTransform: "uppercase",
            letterSpacing: "0.05em",
            borderRadius: 5,
            padding: "3px 8px",
            background: conf.bg,
            color: conf.color,
          }}
        >
          {t(`confidence.${suggestion.confidence}`)}
        </span>
      </div>

      {/* Weather window */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, color: "#93C5FD", fontSize: 12 }}>
        <CloudSun size={14} style={{ flexShrink: 0 }} />
        <span>
          {t("weatherWindow", { from: suggestion.startsAt, to: suggestion.endsAt })} · {suggestion.weatherSummary}
        </span>
      </div>

      {/* Rationale */}
      <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.5 }}>{suggestion.rationale}</p>

      {/* Product table */}
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr style={{ color: "#6B7280", textAlign: "left" }}>
              <th style={{ padding: "6px 8px", fontWeight: 500, width: 28 }} />
              <th style={{ padding: "6px 8px", fontWeight: 500 }}>{t("columns.product")}</th>
              <th style={{ padding: "6px 8px", fontWeight: 500, whiteSpace: "nowrap" }}>{t("columns.price")}</th>
              <th style={{ padding: "6px 8px", fontWeight: 500 }}>{t("columns.reason")}</th>
            </tr>
          </thead>
          <tbody>
            {suggestion.products.map((p) => (
              <tr key={p.itemId} style={{ borderTop: "1px solid #1F2937" }}>
                <td style={{ padding: "6px 8px" }}>
                  <input
                    type="checkbox"
                    checked={selectedProductIds.includes(p.itemId)}
                    onChange={() => toggleProduct(p.itemId)}
                  />
                </td>
                <td style={{ padding: "6px 8px", color: "#E8EDF5" }}>{p.name}</td>
                <td style={{ padding: "6px 8px", color: "#CBD5E1", whiteSpace: "nowrap" }}>
                  {p.currentPrice != null
                    ? `${p.currentPrice.toLocaleString(intlLocale, {
                        minimumFractionDigits: 2,
                        maximumFractionDigits: 2,
                      })} ₴`
                    : "—"}
                </td>
                <td style={{ padding: "6px 8px", color: "#6B7280" }}>{p.reason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Discount % + stores */}
      <div style={{ display: "grid", gridTemplateColumns: "120px 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("discountLabel")}</label>
          <input
            type="number"
            min={MIN_PCT}
            max={MAX_PCT}
            value={discountPct}
            onChange={(e) => setDiscountPct(clamp(Number(e.target.value)))}
            style={{
              width: "100%",
              background: "#0D1117",
              border: "1px solid #374151",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
              padding: "8px 12px",
              outline: "none",
              boxSizing: "border-box",
            }}
          />
        </div>
        <div>
          <label style={labelStyle}>{t("storesLabel")}</label>
          <LocationsMultiSelectDropdown
            locations={activeStores}
            selectedIds={storeIds}
            onToggle={toggleStore}
            summaryLabel={t("storesSelected", { count: storeIds.length })}
            placeholderLabel={t("storesPlaceholder")}
            doneLabel={t("storesDone")}
          />
        </div>
      </div>

      {/* Toggles */}
      <div style={{ display: "flex", flexWrap: "wrap", gap: 16 }}>
        <label style={checkboxRowStyle}>
          <input
            type="checkbox"
            checked={createDiscounts}
            onChange={(e) => setCreateDiscounts(e.target.checked)}
          />
          {t("createDiscountsLabel")}
        </label>
        <label style={checkboxRowStyle}>
          <input
            type="checkbox"
            checked={createCalendarEvent}
            onChange={(e) => setCreateCalendarEvent(e.target.checked)}
          />
          {t("createEventLabel")}
        </label>
      </div>

      {/* Action / created state */}
      {result ? (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            color: "#4ADE80",
            fontSize: 13,
            fontWeight: 500,
          }}
        >
          <Check size={15} style={{ flexShrink: 0 }} />
          <span>{t("created")}</span>
          <Link href="/events" style={{ color: "#93C5FD", textDecoration: "underline" }}>
            {t("viewCalendar")}
          </Link>
        </div>
      ) : (
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <Btn icon={<CalendarClock size={15} />} disabled={!canApply} onClick={() => setConfirmOpen(true)}>
            {t("applyButton")}
          </Btn>
        </div>
      )}

      {confirmOpen && (
        <Modal title={t("confirmTitle")} onClose={() => setConfirmOpen(false)} width={480}>
          <p style={{ color: "#CBD5E1", fontSize: 13, lineHeight: 1.6, margin: 0 }}>
            {t("confirmBody", {
              pct: discountPct,
              products: selectedProductIds.length,
              stores: storeCountLabel,
              from: suggestion.startsAt,
              to: suggestion.endsAt,
            })}
          </p>
          {createCalendarEvent && (
            <p style={{ color: "#9CA3AF", fontSize: 12, lineHeight: 1.5, marginTop: 10, marginBottom: 0 }}>
              {t("confirmCalendarNote")}
            </p>
          )}
          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 20 }}>
            <Btn variant="ghost" onClick={() => setConfirmOpen(false)}>
              {t("cancel")}
            </Btn>
            <Btn variant="success" disabled={apply.isPending} onClick={runApply}>
              {apply.isPending ? t("applying") : t("confirmCta")}
            </Btn>
          </div>
        </Modal>
      )}
    </div>
  );
}

function clamp(n: number): number {
  if (Number.isNaN(n)) return MIN_PCT;
  return Math.min(MAX_PCT, Math.max(MIN_PCT, Math.round(n)));
}
