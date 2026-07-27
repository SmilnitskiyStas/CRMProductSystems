"use client";

import { useTranslations } from "next-intl";
import { Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import {
  useExportPriceAudience,
  useExportAllTime,
  useExportFrequencyAudience,
} from "../hooks/usePriceSegments";
import type { PriceAudienceKey, PriceSegmentKey, FrequencyAudienceKey } from "../types";

// Same pattern as `features/marketing-analytics/components/ExportButtons.tsx`
// (downloadFilePost under the hood via the hooks, `canExportMarketingAnalyticsPii` gate reused
// from `@/lib/roles` by callers) — 3 export buttons (one per mode) + one shared PII toggle.

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/** "Вивантажити Excel" for a comparison-mode audience table. */
export function ExportPriceAudienceButton({
  audience,
  from,
  to,
  storeIds,
  unmaskPii,
}: {
  audience: PriceAudienceKey;
  from: string;
  to: string;
  storeIds: string[];
  unmaskPii: boolean;
}) {
  const t = useTranslations("Dashboard.priceSegments.export");
  const { mutate, isPending } = useExportPriceAudience();

  function handleClick() {
    mutate(
      { audience, from, to, storeIds: storeIds.length > 0 ? storeIds : null, unmaskPii },
      {
        onSuccess: () => toast.success(t("successToast")),
        onError: (e) => toast.error(t("errorToast", { message: errorMessage(e) })),
      },
    );
  }

  return (
    <Btn
      variant="ghost"
      size="sm"
      disabled={isPending}
      icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
      onClick={handleClick}
    >
      {isPending ? t("downloading") : t("exportButton")}
    </Btn>
  );
}

/** "Вивантажити Excel" for the all-time customer table (whole base or one segment). */
export function ExportAllTimeButton({
  segment,
  storeIds,
  unmaskPii,
}: {
  segment: PriceSegmentKey | null;
  storeIds: string[];
  unmaskPii: boolean;
}) {
  const t = useTranslations("Dashboard.priceSegments.export");
  const { mutate, isPending } = useExportAllTime();

  function handleClick() {
    mutate(
      { segment, storeIds: storeIds.length > 0 ? storeIds : null, unmaskPii },
      {
        onSuccess: () => toast.success(t("successToast")),
        onError: (e) => toast.error(t("errorToast", { message: errorMessage(e) })),
      },
    );
  }

  return (
    <Btn
      variant="ghost"
      size="sm"
      disabled={isPending}
      icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
      onClick={handleClick}
    >
      {isPending ? t("downloading") : t("exportButton")}
    </Btn>
  );
}

/** "Вивантажити Excel" for a frequency-mode audience table — carries the same filter fields
 * (decline threshold / min-max spend / price segment) the table itself is currently using, so
 * the export always matches what's on screen. */
export function ExportFrequencyAudienceButton({
  audience,
  from,
  to,
  storeIds,
  declineThresholdPercent,
  minSpend,
  maxSpend,
  priceSegmentFilter,
  unmaskPii,
}: {
  audience: FrequencyAudienceKey;
  from: string;
  to: string;
  storeIds: string[];
  declineThresholdPercent?: number;
  minSpend?: number;
  maxSpend?: number;
  priceSegmentFilter?: PriceSegmentKey;
  unmaskPii: boolean;
}) {
  const t = useTranslations("Dashboard.priceSegments.export");
  const { mutate, isPending } = useExportFrequencyAudience();

  function handleClick() {
    mutate(
      {
        audience,
        from,
        to,
        storeIds: storeIds.length > 0 ? storeIds : null,
        declineThresholdPercent: declineThresholdPercent ?? null,
        minSpend: minSpend ?? null,
        maxSpend: maxSpend ?? null,
        priceSegmentFilter: priceSegmentFilter ?? null,
        unmaskPii,
      },
      {
        onSuccess: () => toast.success(t("successToast")),
        onError: (e) => toast.error(t("errorToast", { message: errorMessage(e) })),
      },
    );
  }

  return (
    <Btn
      variant="ghost"
      size="sm"
      disabled={isPending}
      icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
      onClick={handleClick}
    >
      {isPending ? t("downloading") : t("exportButton")}
    </Btn>
  );
}

/**
 * "Show full phone" toggle shared by all 3 export buttons. `allowed` mirrors the backend's own
 * gate (store_manager+ OR `marketing_analytics.export_pii` capability — reused as-is via
 * `canExportMarketingAnalyticsPii` in `@/lib/roles`, same as RFM). Per task log 420, a caller who
 * lacks it just silently gets the masked file rather than a 403 — the UI hides the checkbox
 * below that rank rather than let it quietly do nothing. Note: this toggle governs ONLY the
 * downloaded Excel file — the on-screen table always shows whatever the GET endpoint returns
 * (the backend does not mask phone for on-screen viewing at all, confirmed in
 * PriceSegmentsService.cs; export-time masking is the only PII control that exists today).
 */
export function PiiUnmaskToggle({
  checked,
  onChange,
  allowed,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  allowed: boolean;
}) {
  const t = useTranslations("Dashboard.priceSegments.export");

  if (!allowed) return null;

  return (
    <label style={{ display: "flex", alignItems: "center", gap: 7, cursor: "pointer" }}>
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
      />
      <span style={{ color: "#9CA3AF", fontSize: 12 }}>{t("unmaskPiiLabel")}</span>
    </label>
  );
}
