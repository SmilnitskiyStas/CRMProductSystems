"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { AppBuilderCanvas } from "@/features/consumer-app/components/AppBuilderCanvas";

/**
 * TASK-539: replaces TASK-535's placeholder with the real App Builder — a drag & drop canvas for
 * the Home page's block list (Block Registry + Draft CRUD API). Same role gate and page-shell
 * shape as every sibling route here (mirrors `/consumer-app/page.tsx`), just wider to fit the
 * palette + canvas side by side, matching `/consumer-app/design`'s precedent.
 */
export default function ConsumerAppPagesPage() {
  const t = useTranslations("Dashboard.consumerApp.pagesPage");
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
    <div style={{ padding: "28px 32px", maxWidth: 1100, display: "flex", flexDirection: "column", gap: 20 }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      <AppBuilderCanvas />
    </div>
  );
}
