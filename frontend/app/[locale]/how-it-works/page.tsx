import type { Metadata } from "next";
import { Check } from "lucide-react";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { routing } from "@/i18n/routing";
import { buildPageMetadata } from "@/features/landing/page-meta";
import { LandingShell } from "@/features/landing/components/LandingShell";
import { PageHero, Section, SectionHeading } from "@/features/landing/components/Section";
import { StepsTimeline, type TimelineStep } from "@/features/landing/components/StepsTimeline";
import { PageCtaStrip } from "@/features/landing/components/PageCtaStrip";
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
  return buildPageMetadata(locale, "Landing.pages.howItWorks.meta", "/how-it-works");
}

export default async function HowItWorksPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations("Landing.pages.howItWorks");
  const common = await getTranslations("Landing.pages.common");
  const steps = t.raw("steps") as TimelineStep[];
  const roles = t.raw("roles.items") as { title: string; text: string }[];
  const mobileItems = t.raw("mobile.items") as string[];

  return (
    <LandingShell>
      <PageHero
        eyebrow={t("hero.eyebrow")}
        title={t("hero.title")}
        description={t("hero.description")}
      />

      <Section className="!pt-4">
        <StepsTimeline steps={steps} />
      </Section>

      <Section>
        <SectionHeading title={t("roles.heading")} subtitle={t("roles.subheading")} />
        <div className="mt-12 grid gap-5 md:grid-cols-3">
          {roles.map((role, i) => (
            <Reveal key={role.title} delay={i * 90}>
              <div className="h-full rounded-xl border border-[var(--l-border)] bg-[var(--l-surface)] p-6">
                <h3 className="text-lg font-semibold text-white">{role.title}</h3>
                <p className="mt-2 text-[15px] leading-relaxed text-[var(--l-muted)]">
                  {role.text}
                </p>
              </div>
            </Reveal>
          ))}
        </div>
      </Section>

      <Section>
        <Reveal>
          <div className="grid gap-8 rounded-2xl border border-[var(--l-border)] bg-[var(--l-surface)] p-6 sm:p-10 lg:grid-cols-2 lg:gap-14">
            <div>
              <h2 className="text-3xl font-bold tracking-tight text-white">
                {t("mobile.heading")}
              </h2>
              <p className="mt-4 text-lg text-[var(--l-muted)]">{t("mobile.text")}</p>
            </div>
            <ul className="space-y-3.5">
              {mobileItems.map((item) => (
                <li key={item} className="flex items-start gap-3 text-[15px] text-slate-300">
                  <Check className="mt-0.5 h-5 w-5 shrink-0 text-[var(--l-ok)]" aria-hidden="true" />
                  {item}
                </li>
              ))}
            </ul>
          </div>
        </Reveal>
      </Section>

      <PageCtaStrip heading={t("cta.heading")} text={t("cta.text")} cta={common("ctaLead")} />
      <LeadSection />
    </LandingShell>
  );
}
