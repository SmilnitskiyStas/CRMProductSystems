"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  getStoredConsent,
  isAnalyticsEnabled,
  setConsent,
  subscribeOpenCookieSettings,
  type ConsentChoice,
} from "../lib/analytics";

/**
 * Cookie consent banner for the public marketing pages. Accept and Decline carry equal
 * prominence; nothing is stored or loaded until the visitor chooses. Can be reopened from the
 * footer ("Cookie settings"). Renders nothing when GA is not configured.
 */
export function CookieBanner() {
  const t = useTranslations("Landing.cookies");
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!isAnalyticsEnabled()) return;
    // localStorage is client-only: read after mount to avoid a hydration mismatch.
    if (getStoredConsent() === null) setOpen(true);
    return subscribeOpenCookieSettings(() => setOpen(true));
  }, []);

  if (!open) return null;

  const choose = (choice: ConsentChoice) => {
    setConsent(choice);
    setOpen(false);
  };

  return (
    <div
      role="dialog"
      aria-label={t("ariaLabel")}
      className="fixed inset-x-3 bottom-3 z-[60] mx-auto max-w-3xl rounded-xl border border-white/10 bg-[#0B1220]/95 p-4 shadow-2xl backdrop-blur sm:p-5"
    >
      <p className="text-sm font-semibold text-white">{t("title")}</p>
      <p className="mt-1.5 text-sm leading-relaxed text-slate-300">{t("text")}</p>
      <p className="mt-1.5 text-xs leading-relaxed text-slate-500">{t("privacyNote")}</p>
      <div className="mt-4 flex flex-col gap-2 sm:flex-row">
        <Button type="button" variant="outline" className="sm:min-w-32" onClick={() => choose("denied")}>
          {t("decline")}
        </Button>
        <Button type="button" variant="outline" className="sm:min-w-32" onClick={() => choose("granted")}>
          {t("accept")}
        </Button>
      </div>
    </div>
  );
}
