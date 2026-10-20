import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { routing } from "@/i18n/routing";
import { buildPageMetadata } from "@/features/landing/page-meta";
import { JsonLd } from "@/features/landing/components/JsonLd";
import { SITE_URL } from "@/lib/site";
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
  return buildPageMetadata(locale, "Landing.meta", "/");
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
  const t = await getTranslations({ locale, namespace: "Landing.meta" });
  const inLanguage = locale === "en" ? "en" : "uk";
  const homeUrl = locale === "en" ? `${SITE_URL}/en` : `${SITE_URL}/`;

  return (
    <LandingShell>
      <JsonLd
        data={{
          "@context": "https://schema.org",
          "@graph": [
            {
              "@type": "Organization",
              "@id": `${SITE_URL}/#organization`,
              name: "AgruSystems",
              url: `${SITE_URL}/`,
              logo: `${SITE_URL}/icon.svg`,
            },
            {
              "@type": "WebSite",
              "@id": `${SITE_URL}/#website`,
              name: "ShelfGuard",
              url: homeUrl,
              inLanguage,
              publisher: { "@id": `${SITE_URL}/#organization` },
            },
            {
              "@type": "SoftwareApplication",
              "@id": `${SITE_URL}/#software`,
              name: "ShelfGuard",
              description: t("description"),
              applicationCategory: "BusinessApplication",
              operatingSystem: "Web",
              url: homeUrl,
              inLanguage,
              publisher: { "@id": `${SITE_URL}/#organization` },
            },
          ],
        }}
      />
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
