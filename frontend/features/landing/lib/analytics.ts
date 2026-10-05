// Google Analytics 4 helpers for the PUBLIC marketing pages only (app/[locale]/**).
// Everything is a no-op when NEXT_PUBLIC_GA_ID is unset (baked at build time) or when the
// visitor has not accepted analytics cookies (Google Consent Mode v2, default = denied).

export const GA_ID = (process.env.NEXT_PUBLIC_GA_ID ?? "").trim();

export const CONSENT_STORAGE_KEY = "sg_cookie_consent";
const CONSENT_EVENT = "sg-consent-change";
const OPEN_SETTINGS_EVENT = "sg-open-cookie-settings";

export type ConsentChoice = "granted" | "denied";

type GtagFn = (...args: unknown[]) => void;

declare global {
  interface Window {
    dataLayer?: unknown[];
    gtag?: GtagFn;
  }
}

export function isAnalyticsEnabled(): boolean {
  return GA_ID.length > 0;
}

export function getStoredConsent(): ConsentChoice | null {
  try {
    const v = window.localStorage.getItem(CONSENT_STORAGE_KEY);
    return v === "granted" || v === "denied" ? v : null;
  } catch {
    return null;
  }
}

/** Persists the choice, updates Consent Mode and notifies subscribers. */
export function setConsent(choice: ConsentChoice): void {
  try {
    window.localStorage.setItem(CONSENT_STORAGE_KEY, choice);
  } catch {
    // storage blocked (private mode): the choice only lives for this page view
  }
  applyConsentToGtag(choice);
  window.dispatchEvent(new CustomEvent<ConsentChoice>(CONSENT_EVENT, { detail: choice }));
}

export function applyConsentToGtag(choice: ConsentChoice): void {
  if (!isAnalyticsEnabled() || typeof window.gtag !== "function") return;
  window.gtag("consent", "update", {
    analytics_storage: choice,
    // Ads signals are never used on this site: they stay denied regardless of the choice.
    ad_storage: "denied",
    ad_user_data: "denied",
    ad_personalization: "denied",
  });
}

export function subscribeConsent(listener: () => void): () => void {
  window.addEventListener(CONSENT_EVENT, listener);
  window.addEventListener("storage", listener);
  return () => {
    window.removeEventListener(CONSENT_EVENT, listener);
    window.removeEventListener("storage", listener);
  };
}

export function openCookieSettings(): void {
  window.dispatchEvent(new Event(OPEN_SETTINGS_EVENT));
}

export function subscribeOpenCookieSettings(listener: () => void): () => void {
  window.addEventListener(OPEN_SETTINGS_EVENT, listener);
  return () => window.removeEventListener(OPEN_SETTINGS_EVENT, listener);
}

/** Sends a GA4 event. No-op without GA id, without gtag or without consent. Never pass PII in params. */
export function trackEvent(name: string, params?: Record<string, string | number | boolean>): void {
  if (typeof window === "undefined" || !isAnalyticsEnabled()) return;
  if (getStoredConsent() !== "granted") return;
  if (typeof window.gtag !== "function") return;
  window.gtag("event", name, params ?? {});
}

/** Lead source for a pathname: "/uk/retail" or "/retail" -> "retail", "/" or "/en" -> "landing". */
export function sourceFromPathname(pathname: string): string {
  const segments = pathname.split("/").filter(Boolean);
  if (segments[0] === "uk" || segments[0] === "en") segments.shift();
  return segments[0] ?? "landing";
}
