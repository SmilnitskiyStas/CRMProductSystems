import type { Metadata } from "next";
import { AlertTriangle, ArrowLeft, Smartphone, Store } from "lucide-react";
import { setRequestLocale } from "next-intl/server";
import { Button } from "@/components/ui/button";
import { Link } from "@/i18n/navigation";
import { LogoMark } from "@/features/landing/components/Logo";
import { getRetailerPublicInfo } from "@/features/join/api/retailers";

// Public, unauthenticated QR/deep-link onboarding fallback (TASK-549).
// `https://<domain>/join/{slug}` — reached by scanning an in-store QR code or
// tapping a shared link before the visitor has the mobile app installed or a
// consumer session. See docs/integration/deep-link-onboarding.md for the full
// deep-link contract this page implements the web half of.
//
// Always rendered fresh: retailer join-ability (active tenant, loyalty module,
// paused program) can change at any time, and this page must never serve a
// stale/cached answer about whether a retailer is currently joinable.
export const dynamic = "force-dynamic";

// Proposed custom-scheme deep link — see docs/integration/deep-link-onboarding.md.
// No native handler is registered yet, so this link intentionally does not
// resolve to anything today; it documents the contract the mobile app will
// register against.
function buildDeepLink(slug: string): string {
  return `shelfguard://join/${encodeURIComponent(slug)}`;
}

const COPY = {
  uk: {
    eyebrow: "Приєднання до магазину",
    heading: (name: string) => `Приєднайтесь до ${name}`,
    subheading:
      "Відскануйте QR-код у магазині або скористайтесь цим посиланням, щоб приєднатися до програми лояльності.",
    openInApp: "Відкрити в застосунку",
    openInAppHint:
      "Якщо застосунок ShelfGuard уже встановлено на телефоні, ця кнопка відкриє його автоматично.",
    downloadHeading: "Завантажте застосунок ShelfGuard",
    comingSoon: "Скоро",
    comingSoonNote:
      "Мобільний застосунок ще не опубліковано в App Store та Google Play. Ми повідомимо, щойно він стане доступний.",
    notFoundEyebrow: "Посилання недоступне",
    notFoundTitle: "Це посилання недійсне",
    notFoundBody:
      "Такого магазину не знайдено, або він зараз недоступний для приєднання. Перевірте посилання чи QR-код і спробуйте ще раз.",
    backHome: "На головну",
    poweredBy: "Платформа ShelfGuard",
  },
  en: {
    eyebrow: "Store onboarding",
    heading: (name: string) => `Join ${name}`,
    subheading:
      "Scan the QR code in-store or use this link to join the loyalty program.",
    openInApp: "Open in app",
    openInAppHint:
      "If the ShelfGuard app is already installed on your phone, this button will open it automatically.",
    downloadHeading: "Get the ShelfGuard app",
    comingSoon: "Coming soon",
    comingSoonNote:
      "The mobile app isn't published on the App Store or Google Play yet. We'll let you know as soon as it's available.",
    notFoundEyebrow: "Link unavailable",
    notFoundTitle: "This link isn't valid",
    notFoundBody:
      "We couldn't find this retailer, or it isn't available to join right now. Please check the link or QR code and try again.",
    backHome: "Back to home",
    poweredBy: "Powered by ShelfGuard",
  },
} as const;

function resolveCopy(locale: string) {
  return locale === "en" ? COPY.en : COPY.uk;
}

type PageParams = { locale: string; slug: string };

export async function generateMetadata({
  params,
}: {
  params: Promise<PageParams>;
}): Promise<Metadata> {
  const { locale, slug } = await params;
  const t = resolveCopy(locale);
  const retailer = await getRetailerPublicInfo(slug);

  return {
    title: retailer ? `${t.heading(retailer.name)} — ShelfGuard` : `${t.notFoundTitle} — ShelfGuard`,
    description: retailer ? t.subheading : t.notFoundBody,
    // Onboarding links are meant to be shared/scanned, not indexed.
    robots: { index: false, follow: false },
  };
}

function StoreBadge({ label, comingSoon }: { label: string; comingSoon: string }) {
  return (
    <div
      aria-disabled="true"
      className="flex items-center gap-2.5 rounded-lg border border-white/10 bg-white/[0.03] px-4 py-2.5 text-slate-400"
    >
      <Store className="h-5 w-5 shrink-0 text-slate-500" aria-hidden="true" />
      <span className="flex flex-col leading-tight">
        <span className="text-[13px] font-medium text-slate-300">{label}</span>
        <span className="text-[11px] text-slate-500">{comingSoon}</span>
      </span>
    </div>
  );
}

export default async function JoinRetailerPage({
  params,
}: {
  params: Promise<PageParams>;
}) {
  const { locale, slug } = await params;
  setRequestLocale(locale);
  const t = resolveCopy(locale);

  const retailer = await getRetailerPublicInfo(slug);

  return (
    <div className="flex min-h-screen flex-col bg-[#0B0F17] text-slate-200 antialiased">
      <div
        aria-hidden="true"
        className="pointer-events-none fixed left-1/2 top-0 -z-10 h-[480px] w-[900px] -translate-x-1/2 rounded-full bg-[#2D7DD2]/[0.12] blur-[140px]"
      />

      <main className="flex flex-1 items-center justify-center px-4 py-16 sm:px-6">
        <div className="w-full max-w-md">
          {retailer ? (
            <div className="rounded-2xl border border-white/10 bg-white/[0.03] p-8 text-center shadow-xl shadow-black/20">
              <p className="text-[13px] font-medium uppercase tracking-wide text-[#5EA3E8]">
                {t.eyebrow}
              </p>

              {retailer.logoUrl ? (
                // External tenant-supplied logo URL — same next/image opt-out as
                // ThemeEditorSection.tsx's identical image-preview pattern.
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  src={retailer.logoUrl}
                  alt={retailer.name}
                  className="mx-auto mt-5 h-[72px] w-[72px] rounded-xl object-cover"
                />
              ) : (
                <div className="mx-auto mt-5 flex h-[72px] w-[72px] items-center justify-center rounded-xl border border-white/10 bg-white/[0.04]">
                  <Store className="h-8 w-8 text-slate-500" aria-hidden="true" />
                </div>
              )}

              <h1 className="mt-5 text-2xl font-bold leading-tight tracking-tight text-white">
                {t.heading(retailer.name)}
              </h1>
              <p className="mx-auto mt-3 max-w-sm text-[15px] leading-relaxed text-slate-400">
                {t.subheading}
              </p>

              <div className="mt-7">
                <Button asChild size="lg" className="w-full">
                  <a href={buildDeepLink(retailer.slug)}>
                    <Smartphone className="h-4 w-4" aria-hidden="true" />
                    {t.openInApp}
                  </a>
                </Button>
                <p className="mt-2.5 text-[12px] leading-relaxed text-slate-500">
                  {t.openInAppHint}
                </p>
              </div>

              <div className="mt-8 border-t border-white/10 pt-6">
                <p className="text-[13px] font-medium text-slate-300">{t.downloadHeading}</p>
                <div className="mt-3 grid grid-cols-2 gap-2.5">
                  <StoreBadge label="Google Play" comingSoon={t.comingSoon} />
                  <StoreBadge label="App Store" comingSoon={t.comingSoon} />
                </div>
                <p className="mt-3 text-[12px] leading-relaxed text-slate-500">
                  {t.comingSoonNote}
                </p>
              </div>
            </div>
          ) : (
            <div className="rounded-2xl border border-white/10 bg-white/[0.03] p-8 text-center shadow-xl shadow-black/20">
              <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full border border-white/10 bg-white/[0.04]">
                <AlertTriangle className="h-6 w-6 text-amber-400" aria-hidden="true" />
              </div>
              <p className="mt-4 text-[13px] font-medium uppercase tracking-wide text-slate-500">
                {t.notFoundEyebrow}
              </p>
              <h1 className="mt-2 text-2xl font-bold leading-tight tracking-tight text-white">
                {t.notFoundTitle}
              </h1>
              <p className="mx-auto mt-3 max-w-sm text-[15px] leading-relaxed text-slate-400">
                {t.notFoundBody}
              </p>
              <div className="mt-7">
                <Button asChild variant="outline" size="lg">
                  <Link href="/">
                    <ArrowLeft className="h-4 w-4" aria-hidden="true" />
                    {t.backHome}
                  </Link>
                </Button>
              </div>
            </div>
          )}

          <div className="mt-8 flex items-center justify-center gap-2 text-slate-600">
            <LogoMark className="h-4 w-4 shrink-0" />
            <span className="text-[12px]">{t.poweredBy}</span>
          </div>
        </div>
      </main>
    </div>
  );
}
