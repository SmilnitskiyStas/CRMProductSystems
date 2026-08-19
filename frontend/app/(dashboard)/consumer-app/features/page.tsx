"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { FeatureFlagsSection } from "@/features/consumer-app/components/FeatureFlagsSection";

/**
 * TASK-557: replaces TASK-535's placeholder with the real Feature Flags UI — 8 fixed toggles
 * (one per `MobileConfigWhitelists.FeatureKeys`), backed by the same whole-document draft
 * AppBuilderCanvas.tsx (TASK-539/541), ThemeEditorSection.tsx (TASK-537) and
 * NavigationBuilderSection.tsx (TASK-542) already read/write. No task in the original Stage D
 * breakdown ever scheduled this screen (only TASK-543's backend service, explicitly scoped
 * backend-only) — discovered as a gap and filled here. Same role gate and page-shell shape as
 * every sibling route here (mirrors `/consumer-app/page.tsx`).
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

      <FeatureFlagsSection />
    </div>
  );
}
