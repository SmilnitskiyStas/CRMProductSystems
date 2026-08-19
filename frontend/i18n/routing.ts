import { defineRouting } from "next-intl/routing";

// i18n routing for the public app/[locale]/ pages (landing + the QR/deep-link
// retailer join page, TASK-549). `uk` is the default locale and has no URL
// prefix ("/"), `en` is prefixed ("/en"). The rest of the app (dashboard,
// auth) is untouched.
export const routing = defineRouting({
  locales: ["uk", "en"],
  defaultLocale: "uk",
  localePrefix: "as-needed",
});
