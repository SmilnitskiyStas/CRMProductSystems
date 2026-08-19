"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { VersionHistorySection } from "@/features/consumer-app/components/VersionHistorySection";

/**
 * TASK-546: replaces TASK-535's placeholder with the real Version History screen — the tenant's
 * publish/rollback history (TASK-545), a rollback action per historical version, and the first
 * in-app "Publish draft" trigger anywhere in Retailer Admin. Same role gate and page-shell shape
 * as every sibling route here (mirrors `/consumer-app/page.tsx`).
 */
export default function ConsumerAppVersionsPage() {
  const t = useTranslations("Dashboard.consumerApp.versionsPage");
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

      <VersionHistorySection />
    </div>
  );
}
