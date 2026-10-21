import type { Metadata } from "next";
import { setRequestLocale } from "next-intl/server";
import "@/features/landing/landing.css";
import { routing } from "@/i18n/routing";
import { buildPageMetadata } from "@/features/landing/page-meta";
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
  return buildPageMetadata(locale, "Landing.verticals.retail.meta", "/retail");
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
