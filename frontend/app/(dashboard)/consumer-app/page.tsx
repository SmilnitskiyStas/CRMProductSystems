"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { BonusProgramSection } from "@/features/consumer-app/components/BonusProgramSection";
import { TierLadderSection } from "@/features/consumer-app/components/TierLadderSection";

/**
 * TASK-500: standalone "Consumer App" management area (product decision — deliberately its own
 * sidebar group, not a Settings tab or a modal). Originally a single page stacking every section
 * (bonus program, banners, promo products, catalog); TASK-525 split it into one page per section
 * — see Sidebar.tsx's "consumer_app" group and the sibling `banners/`, `promotions/`, `catalog/`
 * routes next to this file. This route keeps the bonus/loyalty program section only.
 *
 * Gated to AT_LEAST_ENTERPRISE_ADMIN — mirrors the backend's own
 * `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]` on LoyaltySettingsController
 * exactly (provider, enterprise_admin). No module-key gating: the backend endpoint itself has no
 * `[RequireModule]` guard, so none is added here either.
 */
export default function ConsumerAppPage() {
  const t = useTranslations("Dashboard.consumerApp.page");
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
    <div style={{ padding: "28px 32px", maxWidth: 1040, display: "flex", flexDirection: "column", gap: 20 }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      <BonusProgramSection />
      <TierLadderSection />
    </div>
  );
}
