"use client";

import { X } from "lucide-react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canViewAnalyticsMargin } from "@/lib/roles";
import { ProductAnalyticsTab } from "@/features/inventory/components/ProductAnalyticsTab";

interface Props {
  productId: string;
  productName: string;
  /**
   * Accepted for prop-shape parity with PosDayDetailPanel (and the page's own store filter),
   * but deliberately NOT threaded into ProductAnalyticsTab below: its movement/stock series
   * (useProductMovements) has no store_id filter at all — it's whole-tenant by design, same as
   * the existing /inventory/{id}?tab=analytics view — so a store-scoped revenue line next to a
   * store-agnostic stock line would silently misrepresent the chart. Product-level trend here is
   * intentionally scoped the same way (all stores), regardless of the page's current store filter.
   */
  storeId?: string;
  onClose: () => void;
}

/**
 * Row-click drill-down from PosTopProductsTable (interactive analytics + margin plan, TASK-484)
 * — thin wrapper rendering the now-extended ProductAnalyticsTab inline on /analytics/pos, instead
 * of navigating to /inventory/{id}?tab=analytics (that destination is untouched by this task).
 * Margin visibility resolved with the exact same mechanism CategoryDetailPanel.tsx (TASK-483)
 * uses — role-or-capability via canViewAnalyticsMargin, never re-implemented here.
 */
export function PosProductTrendPanel({ productId, productName, onClose }: Props) {
  const t = useTranslations("Dashboard.analytics.pos.productTrendPanel");

  const { data: me } = useMe();
  const canViewMargin = canViewAnalyticsMargin(me?.role, me?.permissions);

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

      <ProductAnalyticsTab productId={productId} showRevenueSeries canViewMargin={canViewMargin} />
    </div>
  );
}
