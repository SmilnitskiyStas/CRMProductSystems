"use client";

import { Suspense, useEffect, useSyncExternalStore } from "react";
import Script from "next/script";
import { usePathname, useSearchParams } from "next/navigation";
import {
  GA_ID,
  getStoredConsent,
  subscribeConsent,
  type ConsentChoice,
} from "../lib/analytics";

function useConsent(): ConsentChoice | null {
  return useSyncExternalStore(subscribeConsent, getStoredConsent, () => null);
}

/** SPA page_view on route change; only after the visitor accepted analytics cookies. */
function PageViewTracker() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const consent = useConsent();
  const search = searchParams.toString();

  useEffect(() => {
    if (consent !== "granted" || typeof window.gtag !== "function") return;
    window.gtag("event", "page_view", {
      page_path: search ? `${pathname}?${search}` : pathname,
      page_location: window.location.href,
      page_title: document.title,
    });
  }, [pathname, search, consent]);

  return null;
}

/**
 * GA4 loader for the public marketing pages. Consent Mode v2 defaults are all "denied" and set
 * BEFORE gtag("config"); a previously accepted choice is applied right after the defaults.
 * Renders nothing when NEXT_PUBLIC_GA_ID is not configured.
 */
export function GoogleAnalytics() {
  if (!GA_ID) return null;

  const init = `
window.dataLayer = window.dataLayer || [];
function gtag(){dataLayer.push(arguments);}
window.gtag = gtag;
gtag("consent", "default", {
  analytics_storage: "denied",
  ad_storage: "denied",
  ad_user_data: "denied",
  ad_personalization: "denied",
  wait_for_update: 500
});
try {
  if (window.localStorage.getItem("sg_cookie_consent") === "granted") {
    gtag("consent", "update", { analytics_storage: "granted" });
  }
} catch (e) {}
gtag("js", new Date());
gtag("config", ${JSON.stringify(GA_ID)}, { send_page_view: false, anonymize_ip: true });
`;

  return (
    <>
      <Script id="ga-init" strategy="afterInteractive">
        {init}
      </Script>
      <Script
        id="ga-src"
        strategy="afterInteractive"
        src={`https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(GA_ID)}`}
      />
      <Suspense fallback={null}>
        <PageViewTracker />
      </Suspense>
    </>
  );
}
