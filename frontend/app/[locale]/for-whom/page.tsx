import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { routing } from "@/i18n/routing";
import { buildPageMetadata } from "@/features/landing/page-meta";
import { LandingShell } from "@/features/landing/components/LandingShell";
import { PageHero, Section } from "@/features/landing/components/Section";
import { VerticalCard, type VerticalData } from "@/features/landing/components/VerticalCard";
import { LeadSection } from "@/features/landing/components/LeadSection";
import { Reveal } from "@/features/landing/components/Reveal";

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  return buildPageMetadata(locale, "Landing.pages.forWhom.meta", "/for-whom");
}

// Honest per-vertical status: only Retail is ready; the rest are "coming soon".
export default async function ForWhomPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations("Landing.pages.forWhom");
  const common = await getTranslations("Landing.pages.common");
  const verticals = t.raw("verticals") as VerticalData[];

  return (
    <LandingShell>
      <PageHero
        eyebrow={t("hero.eyebrow")}
        title={t("hero.title")}
        description={t("hero.description")}
      />

      <Section className="!pt-4">
        <div className="grid gap-5 md:grid-cols-2">
          {verticals.map((v, i) => (
            <VerticalCard
              key={v.id}
              vertical={v}
              readyLabel={common("ready")}
              soonLabel={common("soon")}
              moreLabel={t("moreLabel")}
              index={i}
            />
          ))}
        </div>
      </Section>

      <Section id="pricing" className="!py-8 sm:!py-12">
        <Reveal>
          <div className="rounded-2xl border border-[var(--l-accent-strong)]/30 bg-[var(--l-accent-strong)]/[0.06] px-6 py-8 text-center sm:px-10">
            <h2 className="text-2xl font-bold text-white">{t("pricing.heading")}</h2>
            <p className="mx-auto mt-3 max-w-2xl text-[var(--l-muted)]">{t("pricing.text")}</p>
          </div>
        </Reveal>
      </Section>

      <LeadSection />
    </LandingShell>
  );
}
