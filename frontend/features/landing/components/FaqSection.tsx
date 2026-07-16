import { ChevronDown } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";

// Native <details>/<summary> — accessible, zero JS.

interface FaqItem {
  q: string;
  a: string;
}

export async function FaqSection() {
  const t = await getTranslations("Landing.faq");
  const items = t.raw("items") as FaqItem[];

  return (
    <section id="faq" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <Reveal className="text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            {t("heading")}
          </h2>
        </Reveal>

        <Reveal className="mt-10" delay={80}>
          <div className="divide-y divide-white/[0.07] rounded-xl border border-white/[0.08] bg-white/[0.02]">
            {items.map((item) => (
              <details key={item.q} className="group px-6 py-5">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-4 text-left font-medium text-white [&::-webkit-details-marker]:hidden">
                  {item.q}
                  <ChevronDown
                    className="h-5 w-5 shrink-0 text-slate-500 transition-transform duration-200 group-open:rotate-180"
                    aria-hidden="true"
                  />
                </summary>
                <p className="mt-3 pr-9 text-[15px] leading-relaxed text-slate-400">{item.a}</p>
              </details>
            ))}
          </div>
        </Reveal>
      </div>
    </section>
  );
}
