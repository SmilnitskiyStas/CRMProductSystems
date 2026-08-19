"use client";

import { useTranslations } from "next-intl";
import { ToggleLeft } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { PlaceholderSection } from "@/features/consumer-app/components/PlaceholderSection";

/**
 * TASK-535: Retailer Admin shell scaffolding — routing/nav only, no Feature Flags UI
 * yet (Stage D, not yet scheduled in detail). Same role gate and page-shell shape as
 * every sibling route here (mirrors `/consumer-app/page.tsx` exactly, just a placeholder
 * body instead of a real section component).
 */
export default function ConsumerAppFeaturesPage() {
  const t = useTranslations("Dashboard.consumerApp.featuresPage");
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

      <PlaceholderSection icon={ToggleLeft} />
    </div>
  );
}
