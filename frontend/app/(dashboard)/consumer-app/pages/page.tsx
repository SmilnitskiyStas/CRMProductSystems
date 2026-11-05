"use client";

import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { AppBuilderCanvas } from "@/features/consumer-app/components/AppBuilderCanvas";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";

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
    // TASK-567: maxWidth bumped 1100 → 1360. `AppBuilderCanvas.tsx`'s 3-column row (palette 300 +
    // canvas 420 + preview 500, TASK-567 widened the preview column for the device picker + 2×20
    // gaps = 1260px combined) never had room here — this wrapper capped content at 1100px
    // (1036px inside its own padding) regardless of how wide the browser window was, so the row
    // fell back to wrapping the preview column below the canvas on every screen size, not just
    // narrow ones. That was the actual bug behind "there's a lot of room on the right, but the
    // preview shows up at the bottom" — the empty room the user saw was real screen space sitting
    // outside this now-too-narrow cap. 1360 gives the row's combined basis (1260) headroom to fit
    // without shrinking once the browser window is wide enough (see `AppBuilderCanvas.tsx`'s
    // `canFitThreeColumns` remarks for the matching breakpoint).
    <PageContainer style={{ maxWidth: 1360 }}>
      <PageHeader title={t("title")} subtitle={t("subtitle")} />

      <AppBuilderCanvas />
    </PageContainer>
  );
}
