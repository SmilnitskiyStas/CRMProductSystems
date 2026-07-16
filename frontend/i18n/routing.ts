import { defineRouting } from "next-intl/routing";

// Landing-only i18n routing. `uk` is the default locale and has no URL prefix
// ("/"), `en` is prefixed ("/en"). Only the landing page + its components use
// this — the rest of the app (dashboard, auth) is untouched.
export const routing = defineRouting({
  locales: ["uk", "en"],
  defaultLocale: "uk",
  localePrefix: "as-needed",
});
