import { ChefHat, ClipboardList, Layers, PackageCheck } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";

const ICONS = [ChefHat, ClipboardList, Layers, PackageCheck];

export async function ProductionSection() {
  const t = await getTranslations("Landing.production");
  const items = t.raw("items") as { title: string; text: string }[];

  return (
    <section id="production" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t("heading")}
          </h2>
          <p className="mt-4 text-lg text-slate-400">{t("subheading")}</p>
        </Reveal>

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
      </div>
    </section>
  );
}
