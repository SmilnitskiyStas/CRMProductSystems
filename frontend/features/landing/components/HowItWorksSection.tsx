import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";

interface Step {
  number: string;
  title: string;
  text: string;
}

export async function HowItWorksSection() {
  const t = await getTranslations("Landing.howItWorks");
  const steps = t.raw("steps") as Step[];

  return (
    <section id="how-it-works" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t("heading")}
          </h2>
          <p className="mt-4 text-lg text-slate-400">{t("subheading")}</p>
        </Reveal>

        <div className="relative mt-12 grid gap-8 md:grid-cols-3 md:gap-6">
          <div
            aria-hidden="true"
            className="absolute left-0 right-0 top-7 hidden h-px bg-gradient-to-r from-transparent via-white/15 to-transparent md:block"
          />
          {steps.map((step, i) => (
            <Reveal key={step.number} delay={i * 110}>
              <div className="relative">
                <div className="inline-flex h-14 w-14 items-center justify-center rounded-full border border-[#2D7DD2]/40 bg-[#0B0F17] text-lg font-bold text-[#5EA3E8]">
                  {step.number}
                </div>
                <h3 className="mt-5 text-lg font-semibold text-white">{step.title}</h3>
                <p className="mt-2 text-[15px] leading-relaxed text-slate-400">{step.text}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
