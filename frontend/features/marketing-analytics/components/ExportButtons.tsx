"use client";

import { useTranslations } from "next-intl";
import { Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import {
  useExportSegment,
  useExportProductBuyers,
  useExportProductPairBuyers,
} from "../hooks/useMarketingAnalytics";
import type { RfmSegmentKey } from "../types";

// All 3 export types share the same resolved base context: the segment's CONCRETE date
// range (from/to — resolved server-side even when the UI filter is a preset like "6m", via
// the already-loaded overview's periodFrom/periodTo — never re-derived client-side), the
// active store selection, and whether PII should be unmasked. Callers assemble this once per
// segment-detail view and pass it down; see SegmentDetailPanel.tsx.
export interface ExportBaseContext {
  segmentKey: RfmSegmentKey;
  from: string;
  to: string;
  storeIds: string[];
  unmaskPii: boolean;
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/** "Вивантажити Excel" — whole segment audience (RFM_ANALYSIS.md §15.1). Rendered in the segment detail header. */
export function ExportSegmentButton({ segmentKey, from, to, storeIds, unmaskPii }: ExportBaseContext) {
  const t = useTranslations("Dashboard.marketingAnalytics.export");
  const { mutate, isPending } = useExportSegment();

  function handleClick() {
    mutate(
      { key: segmentKey, from, to, storeIds: storeIds.length > 0 ? storeIds : null, unmaskPii },
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
      {isPending ? t("downloading") : t("segmentButton")}
    </Btn>
  );
}

/** "Покупці у файл" — buyers of one product within this segment (RFM_ANALYSIS.md §15.2). Rendered per top-product row. */
export function ExportProductBuyersButton({
  segmentKey,
  from,
  to,
  storeIds,
  unmaskPii,
  productName,
}: ExportBaseContext & { productName: string }) {
  const t = useTranslations("Dashboard.marketingAnalytics.export");
  const { mutate, isPending } = useExportProductBuyers();

  function handleClick() {
    mutate(
      { key: segmentKey, from, to, storeIds: storeIds.length > 0 ? storeIds : null, unmaskPii, productName },
      {
        onSuccess: () => toast.success(t("successToast")),
        onError: (err) => toast.error(t("errorToast", { message: errorMessage(err) })),
      },
    );
  }

  // Wrapped in a span that stops propagation — this button lives inside a clickable row
  // (selects the product); without this, clicking it would also fire the row's own onClick.
  return (
    <span onClick={(e) => e.stopPropagation()}>
      <Btn
        variant="ghost"
        size="sm"
        disabled={isPending}
        icon={isPending ? <Loader2 size={12} className="animate-spin" /> : <Download size={12} />}
        onClick={handleClick}
      >
        {isPending ? t("downloading") : t("productBuyersButton")}
      </Btn>
    </span>
  );
}

/** "Покупці обох → файл" — buyers of both the anchor and a cross-sell product (RFM_ANALYSIS.md §15.3). Rendered per affinity/basket row (both tabs use the same export). */
export function ExportProductPairBuyersButton({
  segmentKey,
  from,
  to,
  storeIds,
  unmaskPii,
  productName,
  pairedProductName,
}: ExportBaseContext & { productName: string; pairedProductName: string }) {
  const t = useTranslations("Dashboard.marketingAnalytics.export");
  const { mutate, isPending } = useExportProductPairBuyers();

  function handleClick() {
    mutate(
      {
        key: segmentKey,
        from,
        to,
        storeIds: storeIds.length > 0 ? storeIds : null,
        unmaskPii,
        productName,
        pairedProductName,
      },
      {
        onSuccess: () => toast.success(t("successToast")),
        onError: (err) => toast.error(t("errorToast", { message: errorMessage(err) })),
      },
    );
  }

  // Same stopPropagation wrapper as ExportProductBuyersButton above — lives inside a
  // clickable affinity/basket row.
  return (
    <span onClick={(e) => e.stopPropagation()}>
      <Btn
        variant="ghost"
        size="sm"
        disabled={isPending}
        icon={isPending ? <Loader2 size={12} className="animate-spin" /> : <Download size={12} />}
        onClick={handleClick}
      >
        {isPending ? t("downloading") : t("pairBuyersButton")}
      </Btn>
    </span>
  );
}

/**
 * "Show full phone" toggle shared by all 3 export buttons for a segment. `allowed` mirrors
 * the backend's own gate (store_manager+ OR `marketing_analytics.export_pii` capability —
 * see lib/roles.ts's canExportMarketingAnalyticsPii): per task log 406, a caller who lacks it
 * just silently gets the masked file rather than a 403, so the UI hides/disables the
 * checkbox below that rank rather than let it quietly do nothing.
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
  const t = useTranslations("Dashboard.marketingAnalytics.export");

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
