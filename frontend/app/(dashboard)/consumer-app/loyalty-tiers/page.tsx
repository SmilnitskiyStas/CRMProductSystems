"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { TierLadderSection } from "@/features/consumer-app/components/TierLadderSection";

/**
 * TASK-620: loyalty tier ladder (Bronze/Silver/Gold-style rungs — name, min composite score,
 * accrual multiplier, checkout discount) admin editor, backed by TASK-615's
 * `api/settings/loyalty/tiers` bulk-replace endpoint. Same role gate and page-shell shape as
 * every sibling route under `/consumer-app` (mirrors `/consumer-app/page.tsx` and
 * `/consumer-app/navigation/page.tsx`).
 */
export default function ConsumerAppLoyaltyTiersPage() {
  const t = useTranslations("Dashboard.consumerApp.tierLadderPage");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;

  if (roleAccess === false) {
    return <AccessDenied title={t("title")} />;
  }
  if (roleAccess === null) {
    // Still waiting on useMe() — avoid a denied-then-granted flash.
    return null;
  }

  return (
    <div style={{ padding: "28px 32px", maxWidth: 860, display: "flex", flexDirection: "column", gap: 20 }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      <TierLadderSection />
    </div>
  );
}
