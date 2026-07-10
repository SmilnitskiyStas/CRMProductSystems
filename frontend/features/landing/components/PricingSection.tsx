import { Check } from "lucide-react";
import { Reveal } from "./Reveal";

const INCLUDED = [
  "Усі модулі системи: терміни, залишки, каса, аналітика, маркетплейс",
  "Мобільний застосунок зі сканером штрихкодів",
  "Налаштування під ваш магазин та імпорт товарів",
  "Навчання команди — від касира до керівника",
  "Технічна підтримка й оновлення",
];

export function PricingSection() {
  return (
    <section id="pricing" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">Тарифи</h2>
          <p className="mt-4 text-lg text-slate-400">
            Вартість залежить від кількості магазинів і потрібних модулів — тому рахуємо
            індивідуально.
          </p>
        </Reveal>

        <Reveal className="mx-auto mt-12 max-w-2xl" delay={100}>
          <div className="relative overflow-hidden rounded-2xl border border-[#2D7DD2]/30 bg-white/[0.03] p-8 sm:p-10">
            <div
              aria-hidden="true"
              className="pointer-events-none absolute -top-24 left-1/2 h-48 w-96 -translate-x-1/2 rounded-full bg-[#2D7DD2]/15 blur-3xl"
            />
            <div className="relative">
              <p className="text-sm font-semibold uppercase tracking-wider text-[#5EA3E8]">
                Індивідуальна пропозиція
              </p>
              <p className="mt-3 text-3xl font-bold text-white">Вартість — за запитом</p>
              <p className="mt-2 text-slate-400">
                Залиште заявку — підготуємо розрахунок під ваш формат протягом одного дня.
              </p>

              <ul className="mt-8 space-y-3.5">
                {INCLUDED.map((item) => (
                  <li key={item} className="flex items-start gap-3 text-[15px] text-slate-300">
                    <Check className="mt-0.5 h-5 w-5 shrink-0 text-[#22c55e]" aria-hidden="true" />
                    {item}
                  </li>
                ))}
              </ul>

              <a
                href="#lead-form"
                className="mt-9 inline-block w-full rounded-md bg-[#2D7DD2] px-7 py-3 text-center text-[15px] font-semibold text-white transition-colors hover:bg-[#3E8CDD] sm:w-auto"
              >
                Отримати розрахунок
              </a>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
