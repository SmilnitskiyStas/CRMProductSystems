import type { Metadata } from "next";
import "@/features/landing/landing.css";
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

const TITLE = "ShelfGuard — контроль термінів придатності та залишків для магазинів";
const DESCRIPTION =
  "Система обліку для продуктових магазинів і мереж: FEFO-контроль термінів придатності, AI-автозамовлення, POS-каса з ПРРО (Checkbox), аналітика та мобільний застосунок зі сканером.";

export const metadata: Metadata = {
  metadataBase: new URL("https://agrusystems.pp.ua"),
  title: TITLE,
  description: DESCRIPTION,
  openGraph: {
    title: TITLE,
    description: DESCRIPTION,
    url: "/",
    siteName: "ShelfGuard",
    locale: "uk_UA",
    type: "website",
    images: [
      {
        url: "/landing/dashboard-1.jpg",
        width: 1280,
        height: 574,
        alt: "Дашборд ShelfGuard",
      },
    ],
  },
};

// Public marketing landing. Server component — prerendered for SEO;
// client islands: header, scroll-reveal, lead form.
export default function LandingPage() {
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
