import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import "@/features/landing/landing.css";
import { routing } from "@/i18n/routing";
import { LandingHeader } from "@/features/landing/components/LandingHeader";
import { HeroSection } from "@/features/landing/components/HeroSection";
import { ProblemSection } from "@/features/landing/components/ProblemSection";
import { FeaturesSection } from "@/features/landing/components/FeaturesSection";
import { ShowcaseSection } from "@/features/landing/components/ShowcaseSection";
import { HowItWorksSection } from "@/features/landing/components/HowItWorksSection";
import { AudienceSection } from "@/features/landing/components/AudienceSection";
import { PricingSection } from "@/features/landing/components/PricingSection";
import { FaqSection } from "@/features/landing/components/FaqSection";
import { LeadSection } from "@/features/landing/components/LeadSection";
import { LandingFooter } from "@/features/landing/components/LandingFooter";

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
    <div data-landing className="min-h-screen bg-[#0B0F17] text-slate-200 antialiased">
      <LandingHeader />
      <main>
        <HeroSection />
        <ProblemSection />
        <FeaturesSection />
        <ShowcaseSection />
        <HowItWorksSection />
        <AudienceSection />
        <PricingSection />
        <FaqSection />
        <LeadSection />
      </main>
      <LandingFooter />
    </div>
  );
}
