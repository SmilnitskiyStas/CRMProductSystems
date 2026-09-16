import type { Metadata } from "next";
import { getTranslations, setRequestLocale } from "next-intl/server";
import "@/features/landing/landing.css";
import { routing } from "@/i18n/routing";
import { LandingHeader } from "@/features/landing/components/LandingHeader";
import { RetailHeroSection } from "@/features/landing/components/RetailHeroSection";
import { RetailFeaturesSection } from "@/features/landing/components/RetailFeaturesSection";
import { RetailExampleSection } from "@/features/landing/components/RetailExampleSection";
import { RetailBackHomeSection } from "@/features/landing/components/RetailBackHomeSection";
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
  const t = await getTranslations({ locale, namespace: "Landing.verticals.retail.meta" });
  const tSite = await getTranslations({ locale, namespace: "Landing.meta" });

  return {
    metadataBase: new URL("https://agrusystems.pp.ua"),
    title: t("title"),
    description: t("description"),
    openGraph: {
      title: t("title"),
      description: t("description"),
      url: locale === "en" ? "/en/retail" : "/retail",
      siteName: tSite("ogSiteName"),
      locale: locale === "en" ? "en_US" : "uk_UA",
      type: "website",
      images: [
        {
          url: "/landing/dashboard-1.jpg",
          width: 1280,
          height: 574,
          alt: tSite("ogImageAlt"),
        },
      ],
    },
  };
}

// Vertical marketing page for retail chains (TASK-713) — same SSG pattern
// and visual system as the homepage (app/[locale]/page.tsx), reframed for a
// chain operator: multi-store visibility on top of the same Inventory/FEFO +
// Procurement/AI-reordering + POS/PRRO capabilities the homepage covers.
// auto-service/production/warehouses vertical pages were scoped out — only
// retail is ready enough in the product to publish a marketing page for.
export default async function RetailLandingPage({
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
        <RetailHeroSection />
        <RetailFeaturesSection />
        <RetailExampleSection />
        <RetailBackHomeSection />
        <LeadSection />
      </main>
      <LandingFooter />
    </div>
  );
}
