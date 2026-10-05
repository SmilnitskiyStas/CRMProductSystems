"use client";

import { useTranslations } from "next-intl";
import { isAnalyticsEnabled, openCookieSettings } from "../lib/analytics";

/** Footer link that reopens the cookie banner. Hidden when analytics is not configured. */
export function CookieSettingsLink() {
  const t = useTranslations("Landing.footer");
  if (!isAnalyticsEnabled()) return null;
  return (
    <button
      type="button"
      onClick={openCookieSettings}
      className="text-[var(--l-muted)] transition-colors hover:text-white"
    >
      {t("cookieSettings")}
    </button>
  );
}
