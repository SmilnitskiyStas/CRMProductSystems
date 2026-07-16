import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";
import { LeadForm } from "./LeadForm";

export async function LeadSection() {
  const t = await getTranslations("Landing.leadSection");
  const bullets = t.raw("bullets") as string[];

  return (
    <section id="lead-form" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <div className="relative overflow-hidden rounded-2xl border border-white/[0.08] bg-white/[0.02] px-5 py-10 sm:px-10 sm:py-14">
          <div
            aria-hidden="true"
            className="pointer-events-none absolute -top-32 left-1/2 h-64 w-[600px] -translate-x-1/2 rounded-full bg-[#2D7DD2]/10 blur-3xl"
          />
          <div className="relative grid gap-10 lg:grid-cols-2 lg:gap-16">
            <Reveal>
              <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
                {t("heading")}
              </h2>
              <p className="mt-4 text-lg leading-relaxed text-slate-400">{t("text")}</p>
              <ul className="mt-8 space-y-3 text-[15px] text-slate-300">
                {bullets.map((bullet) => (
                  <li key={bullet} className="flex items-center gap-3">
                    <span aria-hidden="true" className="h-1.5 w-1.5 rounded-full bg-[#22c55e]" />
                    {bullet}
                  </li>
                ))}
              </ul>
            </Reveal>

            <Reveal delay={100}>
              <LeadForm />
            </Reveal>
          </div>
        </div>
      </div>
    </section>
  );
}
