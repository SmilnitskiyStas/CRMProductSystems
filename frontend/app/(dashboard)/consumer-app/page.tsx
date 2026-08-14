"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { BonusProgramSection } from "@/features/consumer-app/components/BonusProgramSection";
import { BannersSection } from "@/features/consumer-app/components/BannersSection";
import { PromoProductsSection } from "@/features/consumer-app/components/PromoProductsSection";
import { CatalogSection } from "@/features/consumer-app/components/CatalogSection";

/**
 * TASK-500: standalone "Consumer App" management page (product decision — deliberately its own
 * page, not a Settings tab or a modal, since it is meant to grow into a general area for
 * consumer-facing mobile-app content: this bonus/loyalty section today, news/promos/etc. later).
 * Only the loyalty section is built now — the page is just a simple vertical stack of cards, so
 * adding a sibling `<...Section />` under `BonusProgramSection` later is the entire integration
 * cost; no placeholder sections are scaffolded ahead of that work.
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
    <div style={{ padding: "28px 32px", maxWidth: 720, display: "flex", flexDirection: "column", gap: 20 }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      <BonusProgramSection />
      <BannersSection />
      <PromoProductsSection />
      <CatalogSection />
    </div>
  );
}
