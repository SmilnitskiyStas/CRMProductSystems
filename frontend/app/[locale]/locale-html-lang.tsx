"use client";

import { useEffect } from "react";

// Root layout (app/layout.tsx) is shared across the whole app and hardcodes
// `<html lang="uk">`. Since it isn't locale-aware, this small client effect
// keeps `document.documentElement.lang` correct on the landing's `/en` pages
// without touching the shared root layout.
export function LocaleHtmlLang({ locale }: { locale: string }) {
  useEffect(() => {
    document.documentElement.lang = locale;
    return () => {
      document.documentElement.lang = "uk";
    };
  }, [locale]);

  return null;
}
