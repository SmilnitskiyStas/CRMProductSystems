import { notFound } from "next/navigation";
import { NextIntlClientProvider } from "next-intl";
import { getMessages, setRequestLocale } from "next-intl/server";
import { hasLocale } from "next-intl";
import { routing } from "@/i18n/routing";
import { LocaleHtmlLang } from "./locale-html-lang";
import { GoogleAnalytics } from "@/features/landing/components/GoogleAnalytics";
import { CookieBanner } from "@/features/landing/components/CookieBanner";

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export default async function LocaleLayout({
  children,
  params,
}: {
  children: React.ReactNode;
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;

  if (!hasLocale(routing.locales, locale)) {
    notFound();
  }

  // Enables static rendering for this locale segment.
  setRequestLocale(locale);

  const messages = await getMessages();
  // frontend/messages/{locale}.json also carries `Dashboard`/`Common` (i18n Block 1,
  // TASK-376) for the authenticated dashboard's own client-side provider. The landing
  // only ever used (and should only ever ship) `Landing.*`. The join page
  // (app/[locale]/join/[slug]/page.tsx, TASK-549) also renders under this layout but
  // keeps its copy inline in the page module and reads it server-side, so it needs
  // nothing added here — scope stays down to `Landing.*` rather than passing the
  // whole file to the client.
  const landingMessages = { Landing: messages.Landing };

  return (
    <NextIntlClientProvider locale={locale} messages={landingMessages}>
      {/* Root layout (app/layout.tsx) is shared with dashboard/auth and keeps
          `lang="uk"` hardcoded — it can't be changed structurally here.
          This client-side effect keeps <html lang> in sync for the landing. */}
      <LocaleHtmlLang locale={locale} />
      {children}
      {/* GA4 + consent banner: public marketing pages only (this layout is not shared with
          the dashboard/auth). Both are no-ops when NEXT_PUBLIC_GA_ID is unset. */}
      <GoogleAnalytics />
      <CookieBanner />

    </NextIntlClientProvider>
  );
}
