import { notFound } from "next/navigation";
import { NextIntlClientProvider } from "next-intl";
import { getMessages, setRequestLocale } from "next-intl/server";
import { hasLocale } from "next-intl";
import { routing } from "@/i18n/routing";
import { LocaleHtmlLang } from "./locale-html-lang";

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
  // TASK-376) for the authenticated dashboard's own client-side provider — the landing
  // only ever used (and should only ever ship) `Landing.*`, so scope it down explicitly
  // rather than passing the whole file to the client.
  const landingMessages = { Landing: messages.Landing };

  return (
    <NextIntlClientProvider locale={locale} messages={landingMessages}>
      {/* Root layout (app/layout.tsx) is shared with dashboard/auth and keeps
          `lang="uk"` hardcoded — it can't be changed structurally here.
          This client-side effect keeps <html lang> in sync for the landing. */}
      <LocaleHtmlLang locale={locale} />
      {children}
    </NextIntlClientProvider>
  );
}
