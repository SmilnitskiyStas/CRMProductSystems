import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { routing } from "@/i18n/routing";
import { LandingShell } from "@/features/landing/components/LandingShell";
import { MiniRoadmapSection } from "@/features/landing/components/MiniRoadmapSection";
import { HeroSection } from "@/features/landing/components/HeroSection";
import { ProblemSection } from "@/features/landing/components/ProblemSection";
import { FeaturesSection } from "@/features/landing/components/FeaturesSection";
import { ShowcaseSection } from "@/features/landing/components/ShowcaseSection";
import { AiAssistantSection } from "@/features/landing/components/AiAssistantSection";
import { HowItWorksSection } from "@/features/landing/components/HowItWorksSection";
import { AudienceSection } from "@/features/landing/components/AudienceSection";
import { PricingSection } from "@/features/landing/components/PricingSection";
import { FaqSection } from "@/features/landing/components/FaqSection";
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
  const t = await getTranslations({ locale, namespace: "Landing.meta" });

  return {
    metadataBase: new URL("https://agrusystems.pp.ua"),
    title: t("title"),
    description: t("description"),
    openGraph: {
      title: t("title"),
      description: t("description"),
      url: locale === "en" ? "/en" : "/",
      siteName: t("ogSiteName"),
      locale: locale === "en" ? "en_US" : "uk_UA",
      type: "website",
      images: [
        {
          url: "/landing/dashboard-1.jpg",
          width: 1280,
          height: 574,
          alt: t("ogImageAlt"),
        },
      ],
    },
  };
}

// Public marketing landing. Server component — prerendered for SEO;
// client islands: header, scroll-reveal, lead form.
export default async function LandingPage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  setRequestLocale(locale);

  return (
    <LandingShell>
      <HeroSection />
      <ProblemSection />
      <FeaturesSection />
      <ShowcaseSection />
      <AiAssistantSection />
      <HowItWorksSection />
      <AudienceSection />
      <MiniRoadmapSection />
      <PricingSection />
      <FaqSection />
      <LeadSection />
    </LandingShell>
  );
}
