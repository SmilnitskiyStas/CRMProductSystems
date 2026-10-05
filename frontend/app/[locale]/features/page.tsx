import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { routing } from "@/i18n/routing";
import { buildPageMetadata } from "@/features/landing/page-meta";
import { LandingShell } from "@/features/landing/components/LandingShell";
import { PageHero, Section } from "@/features/landing/components/Section";
import {
  FeatureGroup,
  type FeatureGroupData,
} from "@/features/landing/components/FeatureGroup";
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
  return buildPageMetadata(locale, "Landing.pages.features.meta", "/features");
}

// Public "Features" page — only capabilities that exist in the product.
export default async function FeaturesPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations("Landing.pages.features");
  const groups = t.raw("groups") as FeatureGroupData[];

  return (
    <LandingShell>
      <PageHero
        eyebrow={t("hero.eyebrow")}
        title={t("hero.title")}
        description={t("hero.description")}
      />
      <Section className="!pt-4">
        <div className="grid gap-5 lg:grid-cols-2">
          {groups.map((group, i) => (
            <FeatureGroup key={group.id} group={group} solvesLabel={t("solvesLabel")} index={i} />
          ))}
        </div>
      </Section>
      <PageCtaStrip
        heading={t("cta.heading")}
        text={t("cta.text")}
        cta={(await getTranslations("Landing.pages.common"))("ctaLead")}
      />
      <LeadSection />
    </LandingShell>
  );
}
