import { Award, Gift, History, Smartphone } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";

const SECONDARY_ICONS = [Award, Gift, History];

export async function LoyaltySection() {
  const t = await getTranslations("Landing.loyalty");
  const secondary = t.raw("secondary") as { title: string; text: string }[];

  return (
    <section id="loyalty" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t("heading")}
          </h2>
          <p className="mt-4 text-lg text-slate-400">{t("subheading")}</p>
        </Reveal>

        <div className="mt-12 grid gap-5 lg:grid-cols-2">
          <Reveal>
            <div className="flex h-full flex-col rounded-xl border border-[#2D7DD2]/25 bg-[#2D7DD2]/[0.05] p-7">
              <div className="flex items-center gap-3">
                <div className="inline-flex rounded-lg bg-[#2D7DD2]/15 p-3">
                  <Smartphone className="h-6 w-6 text-[#5EA3E8]" aria-hidden="true" />
                </div>
                <span className="rounded-full border border-[#2D7DD2]/30 bg-[#2D7DD2]/10 px-3 py-1 text-xs font-semibold text-[#5EA3E8]">
                  {t("mainBadge")}
                </span>
              </div>
              <h3 className="mt-5 text-xl font-semibold text-white">{t("mainTitle")}</h3>
              <p className="mt-3 leading-relaxed text-slate-400">{t("mainText")}</p>
            </div>
          </Reveal>

          <div className="grid gap-5 sm:grid-cols-3 lg:grid-cols-1">
            {secondary.map((item, i) => {
              const Icon = SECONDARY_ICONS[i];
              return (
                <Reveal key={item.title} delay={i * 90}>
                  <div className="flex h-full items-start gap-4 rounded-xl border border-white/[0.08] bg-white/[0.03] p-5">
                    <div className="inline-flex shrink-0 rounded-lg bg-white/[0.05] p-2.5">
                      <Icon className="h-5 w-5 text-slate-300" aria-hidden="true" />
                    </div>
                    <div>
                      <h3 className="font-semibold text-white">{item.title}</h3>
                      <p className="mt-1 text-sm leading-relaxed text-slate-400">{item.text}</p>
                    </div>
                  </div>
                </Reveal>
              );
            })}
          </div>
        </div>

        <Reveal className="mt-8">
          <p className="text-center text-sm text-slate-500">{t("footer")}</p>
        </Reveal>
      </div>
    </section>
  );
}
