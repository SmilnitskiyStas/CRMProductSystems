import { ClipboardList, PackageX, ShoppingCart } from "lucide-react";
import { Reveal } from "./Reveal";

const PROBLEMS = [
  {
    icon: PackageX,
    color: "text-[#ef4444]",
    bg: "bg-[#ef4444]/10",
    title: "Списання з'їдають маржу",
    text: "Прострочений товар помічають, коли він уже стоїть на полиці — або коли його повертає покупець. Кожна така партія — це гроші, які магазин просто викидає.",
  },
  {
    icon: ClipboardList,
    color: "text-[#f59e0b]",
    bg: "bg-[#f59e0b]/10",
    title: "Терміни — у зошитах і Excel",
    text: "Дати придатності записують вручну, перевіряють обходом залу «коли є час». Людський фактор гарантує: щось обов'язково пропустять.",
  },
  {
    icon: ShoppingCart,
    color: "text-[#fb923c]",
    bg: "bg-[#fb923c]/10",
    title: "Замовлення «на око»",
    text: "Без даних про реальні продажі замовляють за відчуттями. Результат — або надлишки, які зависають і псуються, або порожні полиці й втрачені продажі.",
  },
];

export function ProblemSection() {
  return (
    <section className="py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Знайомі проблеми?
          </h2>
          <p className="mt-4 text-lg text-slate-400">
            Три причини, чому продуктовий рітейл втрачає гроші щодня.
          </p>
        </Reveal>

        <div className="mt-12 grid gap-5 md:grid-cols-3">
          {PROBLEMS.map((p, i) => (
            <Reveal key={p.title} delay={i * 90}>
              <div className="h-full rounded-xl border border-white/[0.08] bg-white/[0.03] p-6 transition-colors hover:border-white/[0.14]">
                <div className={`inline-flex rounded-lg p-2.5 ${p.bg}`}>
                  <p.icon className={`h-5 w-5 ${p.color}`} aria-hidden="true" />
                </div>
                <h3 className="mt-4 text-lg font-semibold text-white">{p.title}</h3>
                <p className="mt-2 text-[15px] leading-relaxed text-slate-400">{p.text}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
