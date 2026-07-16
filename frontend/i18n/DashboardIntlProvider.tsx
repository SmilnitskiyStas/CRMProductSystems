"use client";

import { useEffect, useState } from "react";
import { NextIntlClientProvider } from "next-intl";
import ukMessages from "@/messages/uk.json";
import enMessages from "@/messages/en.json";
import { LOCALE_CHANGE_EVENT, resolveDashboardLocale, type DashboardLocale } from "./dashboard-locale";

// Messages live in the SAME frontend/messages/{uk,en}.json files as the landing's
// `Landing.*` namespace (single source file per locale, per the approved i18n rollout
// plan) — but only the `Dashboard`/`Common` slices are ever passed to
// NextIntlClientProvider here, so dashboard components never see (or can accidentally
// reference) `Landing.*` strings. Both locale files are statically imported so real
// messages are available on the very first render — no async gap, so no risk of a
// child `useTranslations()` call throwing before messages arrive. The trade-off: both
// locale bundles end up in the dashboard's client chunk regardless of which one is
// active; accepted for this foundation block in favor of simplicity/correctness over
// marginal bundle size (a later block could split files if this ever measurably matters).
const MESSAGES_BY_LOCALE: Record<DashboardLocale, { Common: unknown; Dashboard: unknown }> = {
  uk: { Common: ukMessages.Common, Dashboard: ukMessages.Dashboard },
  en: { Common: enMessages.Common, Dashboard: enMessages.Dashboard },
};

/**
 * Client-only i18n provider for the authenticated dashboard + auth screens (i18n
 * rollout Block 1, TASK-376). Mounted in `app/(dashboard)/layout.tsx` and
 * `app/(auth)/layout.tsx` — every descendant page/component can call
 * `useTranslations`/`useLocale` regardless of whether that specific feature has been
 * translated yet (untranslated pages just render their existing hardcoded strings
 * alongside translated shell chrome).
 *
 * Locale resolution is cookie -> cached user.preferredLocale -> browser language ->
 * "uk" (see `resolveDashboardLocale`), which needs `document`/`navigator`/localStorage
 * and so only runs client-side after mount. "uk" is used for the very first
 * (server-rendered) pass — matches the app's hardcoded `<html lang="uk">` root — then
 * `resolveDashboardLocale()` runs in a `useEffect` and may flip the locale once, exactly
 * like the existing `mounted` gate pattern already used in the dashboard layout.
 */
export function DashboardIntlProvider({ children }: { children: React.ReactNode }) {
  const [locale, setLocale] = useState<DashboardLocale>("uk");

  useEffect(() => {
    setLocale(resolveDashboardLocale());

    function handleLocaleChanged() {
      setLocale(resolveDashboardLocale());
    }
    window.addEventListener(LOCALE_CHANGE_EVENT, handleLocaleChanged);
    return () => window.removeEventListener(LOCALE_CHANGE_EVENT, handleLocaleChanged);
  }, []);

  return (
    <NextIntlClientProvider locale={locale} messages={MESSAGES_BY_LOCALE[locale]}>
      {children}
    </NextIntlClientProvider>
  );
}
