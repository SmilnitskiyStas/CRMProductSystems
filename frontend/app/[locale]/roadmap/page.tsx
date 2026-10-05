import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { routing } from "@/i18n/routing";
import { buildPageMetadata } from "@/features/landing/page-meta";
import { LandingShell } from "@/features/landing/components/LandingShell";
import { PageHero, Section } from "@/features/landing/components/Section";
import { RoadmapTimeline } from "@/features/landing/components/RoadmapTimeline";
import { RoadmapProgress, getRoadmapColumns } from "@/features/landing/components/RoadmapProgress";
import { PageCtaStrip } from "@/features/landing/components/PageCtaStrip";
import { LeadSection } from "@/features/landing/components/LeadSection";

export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ locale: string }>;
}): Promise<Metadata> {
  const { locale } = await params;
  return buildPageMetadata(locale, "Landing.pages.roadmap.meta", "/roadmap");
}

// Roadmap: "Done" is filled from shipped capabilities; "In progress" and
// "Planned" are driven by the `items` arrays in messages (Landing.pages.
// roadmap.columns.*) and are not rendered while empty. No dates on purpose.
export default async function RoadmapPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations("Landing.pages.roadmap");
  const common = await getTranslations("Landing.pages.common");
  const { done, inProgress, planned } = await getRoadmapColumns();

  return (
    <LandingShell>
      <PageHero
        eyebrow={t("hero.eyebrow")}
        title={t("hero.title")}
        description={t("hero.description")}
      />
      <Section className="!pt-4">
        <RoadmapProgress className="mb-12" />
        <RoadmapTimeline
          stages={[
            { tone: "done", column: done },
            { tone: "inProgress", column: inProgress },
            { tone: "planned", column: planned },
          ]}
        />
        <p className="mx-auto mt-8 max-w-2xl text-center text-sm text-slate-500">
          {t("disclaimer")}
        </p>
      </Section>
      <PageCtaStrip
        heading={t("cta.heading")}
        text={t("cta.text")}
        cta={common("ctaLead")}
      />
      <LeadSection />
    </LandingShell>
  );
}
