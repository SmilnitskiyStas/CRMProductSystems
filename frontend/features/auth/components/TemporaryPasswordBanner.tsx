"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { AlertTriangle } from "lucide-react";
import { useMe } from "../hooks/useAuth";

// Same locale->Intl-locale mapping and date/time formatting options already used by
// UserActivityLog.tsx/NotificationDetailDrawer.tsx — kept local since this is the only
// auth-feature caller so far.
function formatExpiry(iso: string, locale: string): string {
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  return new Date(iso).toLocaleString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * Persistent notice shown on every authenticated page while the signed-in account is
 * using a temporary password (forgot-password redesign, TASK-465/466) — not dismissible,
 * since it communicates a real access-expiry deadline rather than a one-off message. It
 * disappears on its own once `useMe()` reflects `passwordIsTemporary: false`, which
 * happens right after the user sets a new password (`useChangePassword` invalidates the
 * `/auth/me` cache on success) or, failing that, on the next natural refetch/login.
 *
 * Visual convention: full-bleed top bar like ImpersonationBanner.tsx, but in the amber
 * "warning" palette SessionExpiredNotice.tsx already established for this feature
 * directory (#F59E0B) — this is an actionable warning, not a neutral admin-mode signal.
 * Deliberately NOT `position: fixed` (unlike ImpersonationBanner.tsx): it's rendered as
 * the first child of the dashboard shell's content column, directly above `<TopBar />`,
 * so it takes up normal flow space instead of overlaying — no z-index/offset math needed
 * to keep it from covering the sidebar/topbar, and no conflict if ImpersonationBanner is
 * also showing (rare combo: a provider impersonating a tenant user who separately has a
 * live temp password).
 */
export function TemporaryPasswordBanner() {
  const t = useTranslations("Dashboard.auth.temporaryPasswordBanner");
  const locale = useLocale();
  const { data: user } = useMe();

  if (!user?.passwordIsTemporary || !user.temporaryPasswordExpiresAt) return null;

  return (
    <div
      role="status"
      style={{
        background: "#F59E0B1A",
        borderBottom: "1px solid #F59E0B40",
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        flexWrap: "wrap",
        gap: 12,
        padding: "10px 24px",
        flexShrink: 0,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <AlertTriangle size={16} color="#F59E0B" style={{ flexShrink: 0 }} />
        <span style={{ color: "#F59E0B", fontSize: 13, fontFamily: '"Inter", sans-serif' }}>
          {t("message", { expiresAt: formatExpiry(user.temporaryPasswordExpiresAt, locale) })}
        </span>
      </div>
      <Link
        href="/settings-user#password"
        style={{
          color: "#F59E0B",
          fontSize: 12,
          fontWeight: 600,
          fontFamily: '"Inter", sans-serif',
          textDecoration: "underline",
          textUnderlineOffset: 3,
          whiteSpace: "nowrap",
          flexShrink: 0,
        }}
      >
        {t("actionLabel")}
      </Link>
    </div>
  );
}
