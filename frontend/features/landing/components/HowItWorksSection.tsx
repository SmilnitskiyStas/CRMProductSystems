import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";
import { Section, SectionHeading, TeaserLink } from "./Section";

interface Step {
  number: string;
  title: string;
  text: string;
}

export async function HowItWorksSection() {
  const t = await getTranslations("Landing.howItWorks");
  const tTeasers = await getTranslations("Landing.teasers");
  const steps = t.raw("steps") as Step[];

  return (
    <Section id="how-it-works">
      <SectionHeading title={t("heading")} subtitle={t("subheading")} />

        <div className="relative mt-12 grid gap-8 md:grid-cols-3 md:gap-6">
          <div
            aria-hidden="true"
            className="absolute left-0 right-0 top-7 hidden h-px bg-gradient-to-r from-transparent via-white/15 to-transparent md:block"
          />
          {steps.map((step, i) => (
            <Reveal key={step.number} delay={i * 110}>
              <div className="relative">
                <div className="inline-flex h-14 w-14 items-center justify-center rounded-full border border-[#2D7DD2]/40 bg-[var(--l-bg)] text-lg font-bold text-[#5EA3E8]">
                  {step.number}
                </div>
                <h3 className="mt-5 text-lg font-semibold text-white">{step.title}</h3>
                <p className="mt-2 text-[15px] leading-relaxed text-slate-400">{step.text}</p>
              </div>
            </Reveal>
          ))}
        </div>

      <Reveal className="mt-10 text-center">
        <TeaserLink href="/how-it-works">{tTeasers("howItWorks")}</TeaserLink>
      </Reveal>
    </Section>
  );
}
