import {
  BarChart3,
  Brain,
  CalendarClock,
  CalendarDays,
  Receipt,
  ScanLine,
  Thermometer,
  Truck,
} from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";
import { Section, SectionHeading, TeaserLink } from "./Section";

const ICONS = [
  CalendarClock,
  Brain,
  Receipt,
  BarChart3,
  Truck,
  Thermometer,
  ScanLine,
  CalendarDays,
];

export async function FeaturesSection() {
  const t = await getTranslations("Landing.features");
  const tTeasers = await getTranslations("Landing.teasers");
  const items = t.raw("items") as { title: string; text: string }[];

  return (
    <Section id="features">
      <SectionHeading title={t("heading")} subtitle={t("subheading")} />

        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {items.map((item, i) => {
            const Icon = ICONS[i];
            return (
              <Reveal key={item.title} delay={(i % 4) * 70}>
                <div className="h-full rounded-xl border border-white/[0.08] bg-white/[0.03] p-5 transition-colors hover:border-[#2D7DD2]/40">
                  <div className="inline-flex rounded-lg bg-[#2D7DD2]/10 p-2.5">
                    <Icon className="h-5 w-5 text-[#5EA3E8]" aria-hidden="true" />
                  </div>
                  <h3 className="mt-4 text-[15px] font-semibold text-white">{item.title}</h3>
                  <p className="mt-2 text-sm leading-relaxed text-slate-400">{item.text}</p>
                </div>
              </Reveal>
            );
          })}
        </div>

      <Reveal className="mt-10 text-center">
        <TeaserLink href="/features">{tTeasers("features")}</TeaserLink>
      </Reveal>
    </Section>
  );
}
