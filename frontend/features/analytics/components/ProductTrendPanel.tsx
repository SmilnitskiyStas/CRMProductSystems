"use client";

import { useMemo } from "react";
import { X } from "lucide-react";
import { useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canViewAnalyticsMargin } from "@/lib/roles";
import { ProductAnalyticsTab } from "@/features/inventory/components/ProductAnalyticsTab";
import { stockApi } from "@/features/shelf/api/stock";
import { useAdu } from "../hooks/useAdu";

interface Props {
  productId: string;
  productName: string;
  /**
   * Accepted for prop-shape parity with PosDayDetailPanel (and the page's own store filter).
   * Deliberately NOT threaded into ProductAnalyticsTab's own movement/stock series
   * (useProductMovements has no store_id filter at all — it's whole-tenant by design, same as
   * the existing /inventory/{id}?tab=analytics view — so a store-scoped revenue line next to a
   * store-agnostic stock line would silently misrepresent the chart). Product-level trend there
   * is intentionally scoped the same way (all stores), regardless of the page's current store
   * filter.
   *
   * Used locally in THIS component instead (TASK-494) to compute daysOfStockRemaining — see
   * below. When undefined (network-wide context — no store selected in the header, or none picked
   * on /analytics/pos's own local dropdown), no ADU/stock fetch happens at all and no
   * days-of-stock card renders on the tab, matching ProductAnalyticsTab's own "absent, not empty"
   * contract for that prop. Both /analytics (TASK-514, via the header's global store selector) and
   * /analytics/pos (TASK-484, via its own local dropdown, falling back to the header's selection —
   * TASK-514) thread their current store filter through here.
   */
  storeId?: string;
  onClose: () => void;
}

/**
 * Product sales-trend drill-down — thin wrapper rendering the now-extended ProductAnalyticsTab
 * inline, instead of navigating to /inventory/{id}?tab=analytics (that destination is untouched
 * by either task that built/reused this). Originally built (TASK-484) as PosTopProductsTable's
 * row-click drill-down on /analytics/pos; renamed from PosProductTrendPanel and reused unmodified
 * (TASK-488) as the product-row drill-down from CategoryDetailPanel/LossesProductBreakdownPanel on
 * /analytics — it was already generic (productId/productName/storeId?/onClose, no POS-specific
 * coupling), so no logic changes were needed, only the name. Margin visibility resolved with the
 * exact same mechanism CategoryDetailPanel.tsx (TASK-483) uses — role-or-capability via
 * canViewAnalyticsMargin, never re-implemented here.
 *
 * TASK-494: also the sole place in the app that computes a single-product days-of-stock-
 * remaining figure (currentStock / ADU aduEffective) — ProductAnalyticsTab itself stays a dumb
 * renderer of whatever's passed in (see its daysOfStockRemaining prop doc), same posture as
 * canViewMargin above.
 */
export function ProductTrendPanel({ productId, productName, storeId, onClose }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.productTrendPanel");

  const { data: me } = useMe();
  const canViewMargin = canViewAnalyticsMargin(me?.role, me?.permissions);

  // ── Days-of-stock-remaining (TASK-494) ─────────────────────────────────────────────────────
  // Only meaningful with a concrete store — ADU (frontend/features/analytics/hooks/useAdu.ts) is
  // per-(product, store), same reason the by-category table's equivalent column
  // (CategoryProductRowDto.daysOfStockRemaining) goes null on a network-wide request. No fetch
  // at all when storeId is undefined, matching that same "network-wide = no signal" rule instead
  // of asking the ADU endpoint a question it can't answer.
  const hasStore = !!storeId;

  const aduQuery = useAdu(storeId, productId, hasStore);

  // Reuses the existing features/shelf stockApi (GET /api/stock, filters via the `store_id` TS
  // param — sent on the wire as `storeIds` since the backend rename) directly via useQuery here
  // rather than the shelf feature's own useStock
  // hook — that hook has no `enabled` param, and adding one is out of this task's scope (a
  // shared hook used across many unrelated features). Default page size (50) is plenty for one
  // product's batches at one store.
  const stockQuery = useQuery({
    queryKey: ["stock", { store_id: storeId, product_id: productId }],
    queryFn: () => stockApi.getAll({ store_id: storeId, product_id: productId }),
    enabled: hasStore,
  });

  const aduSettled = aduQuery.isSuccess || aduQuery.isError;

  const daysOfStockRemaining = useMemo<number | null | undefined>(() => {
    if (!hasStore) return undefined; // network-wide — no card at all (ProductAnalyticsTab contract)
    if (!aduSettled || stockQuery.isLoading) return undefined; // still resolving — avoid a "—" flash before the real value lands

    // A 404 (no ProductAdu row yet) leaves aduQuery.data undefined, which collapses into the
    // same null branch as a successful-but-zero/null aduEffective — both are "no usage signal
    // yet" per TASK-491's own two-case doc comment on CategoryProductRowDto.daysOfStockRemaining
    // in ../types.ts, and this component doesn't need to tell them apart either.
    const aduEffective = aduQuery.data?.aduEffective ?? null;
    if (aduEffective == null || aduEffective === 0) return null;

    // Same on-hand definition AnalyticsRepository.cs uses everywhere else (Quantity > 0,
    // excluding sold_out/archived batches) — see GetByCategoryAsync/GetWorstProductsAsync.
    const currentStock = (stockQuery.data ?? [])
      .filter((s) => s.quantity > 0 && s.status !== "sold_out" && s.status !== "archived")
      .reduce((sum, s) => sum + s.quantity, 0);

    // Same rounding as the backend (Math.Round(x, 1)).
    return Math.round((currentStock / aduEffective) * 10) / 10;
  }, [hasStore, aduSettled, aduQuery.data, stockQuery.data, stockQuery.isLoading]);

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "20px 16px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>
            {t("title", { product: productName })}
          </div>
          {canViewMargin && (
            <div style={{ color: "#6B7280", fontSize: 11.5, marginTop: 4, fontStyle: "italic", maxWidth: 620 }}>
              {t("marginDisclaimer")}
            </div>
          )}
        </div>
        <button
          onClick={onClose}
          title={t("closeButton")}
          style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 8,
            width: 30,
            height: 30,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            cursor: "pointer",
            color: "#9CA3AF",
            flexShrink: 0,
          }}
        >
          <X size={15} />
        </button>
      </div>

      <ProductAnalyticsTab
        productId={productId}
        showRevenueSeries
        canViewMargin={canViewMargin}
        daysOfStockRemaining={daysOfStockRemaining}
      />
    </div>
  );
}
