// Locale resolution/persistence for the authenticated dashboard + auth screens
// (i18n rollout Block 1, TASK-376). Deliberately independent of `i18n/routing.ts` /
// `i18n/request.ts`, which are landing-only and URL-prefixed (`/`, `/en`) — the
// dashboard stays unprefixed and picks its locale client-side instead, per the
// approved plan in `.claude/docs/i18n-rollout-plan.md`.

import { getStoredUser } from "@/features/auth/store";

export type DashboardLocale = "uk" | "en";

/** Cookie the dashboard/auth shell reads and writes to persist the chosen UI locale. */
export const DASHBOARD_LOCALE_COOKIE = "sg_locale";

/** Dispatched on `window` right after the cookie is written, so any mounted
 * `DashboardIntlProvider` (and anything else interested) can re-resolve immediately
 * without a full page reload. */
export const LOCALE_CHANGE_EVENT = "sg-locale-changed";

const SUPPORTED_LOCALES: readonly DashboardLocale[] = ["uk", "en"];

export function isSupportedLocale(value: string | null | undefined): value is DashboardLocale {
  return !!value && (SUPPORTED_LOCALES as readonly string[]).includes(value);
}

function readLocaleCookie(): DashboardLocale | null {
  if (typeof document === "undefined") return null;
  const match = document.cookie.match(/(?:^|;\s*)sg_locale=([^;]+)/);
  const value = match?.[1] ? decodeURIComponent(match[1]) : null;
  return isSupportedLocale(value) ? value : null;
}

/** "uk" if the browser reports a uk-* language, "en" for any other determined
 * language, "uk" if nothing could be determined at all (no `navigator`/SSR). */
function resolveBrowserLocale(): DashboardLocale {
  if (typeof navigator === "undefined" || !navigator.language) return "uk";
  return navigator.language.toLowerCase().startsWith("uk") ? "uk" : "en";
}

/**
 * cookie -> cached user.preferredLocale -> browser language -> "uk".
 * Client-only — reads `document`/`navigator`/localStorage, so only call this after
 * mount (e.g. from a `useEffect` or an event handler), never during SSR.
 */
export function resolveDashboardLocale(): DashboardLocale {
  const cookieLocale = readLocaleCookie();
  if (cookieLocale) return cookieLocale;

  const userLocale = getStoredUser()?.preferredLocale;
  if (isSupportedLocale(userLocale)) return userLocale;

  return resolveBrowserLocale();
}

/**
 * Persists the chosen locale (~1 year cookie) and notifies mounted providers.
 * Does NOT itself talk to the backend — the language switcher separately
 * best-effort-saves `user.preferredLocale` via the existing profile-update endpoint,
 * so a failed/missing backend field never blocks the switch itself.
 */
export function setDashboardLocale(locale: DashboardLocale): void {
  if (typeof document === "undefined") return;
  const maxAge = 60 * 60 * 24 * 365; // ~1 year
  document.cookie = `${DASHBOARD_LOCALE_COOKIE}=${locale}; path=/; max-age=${maxAge}; SameSite=Lax`;
  window.dispatchEvent(new Event(LOCALE_CHANGE_EVENT));
}
