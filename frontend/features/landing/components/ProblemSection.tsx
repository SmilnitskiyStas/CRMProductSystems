import { ClipboardList, PackageX, ShoppingCart } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";

const ICONS = [PackageX, ClipboardList, ShoppingCart];
const STYLES = [
  { color: "text-[#ef4444]", bg: "bg-[#ef4444]/10" },
  { color: "text-[#f59e0b]", bg: "bg-[#f59e0b]/10" },
  { color: "text-[#fb923c]", bg: "bg-[#fb923c]/10" },
];

export async function ProblemSection() {
  const t = await getTranslations("Landing.problem");
  const items = t.raw("items") as { title: string; text: string }[];

  return (
    <section className="py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t("heading")}
          </h2>
          <p className="mt-4 text-lg text-slate-400">{t("subheading")}</p>
        </Reveal>

        <div className="mt-12 grid gap-5 md:grid-cols-3">
          {items.map((item, i) => {
            const Icon = ICONS[i];
            const style = STYLES[i];
            return (
              <Reveal key={item.title} delay={i * 90}>
                <div className="h-full rounded-xl border border-white/[0.08] bg-white/[0.03] p-6 transition-colors hover:border-white/[0.14]">
                  <div className={`inline-flex rounded-lg p-2.5 ${style.bg}`}>
                    <Icon className={`h-5 w-5 ${style.color}`} aria-hidden="true" />
                  </div>
                  <h3 className="mt-4 text-lg font-semibold text-white">{item.title}</h3>
                  <p className="mt-2 text-[15px] leading-relaxed text-slate-400">{item.text}</p>
                </div>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}
