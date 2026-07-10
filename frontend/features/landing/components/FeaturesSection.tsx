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
import { Reveal } from "./Reveal";

const FEATURES = [
  {
    icon: CalendarClock,
    title: "Контроль термінів (FEFO)",
    text: "Кожна партія має свій термін придатності та статус: норма, попередження, критично, прострочено. Система сама підказує, що продати першим.",
  },
  {
    icon: Brain,
    title: "AI-автозамовлення",
    text: "Штучний інтелект аналізує продажі, залишки та сезонність і щоранку готує пропозицію замовлення. Вам лишається перевірити й підтвердити.",
  },
  {
    icon: Receipt,
    title: "POS-каса з ПРРО",
    text: "Повноцінна каса з фіскалізацією через Checkbox: чеки, зміни, Z-звіти. Продаж одразу списує залишок — партія за партією, за FEFO.",
  },
  {
    icon: BarChart3,
    title: "Аналітика і звіти",
    text: "Стан залишків за зонами й категоріями, збитки від списань, динаміка продажів. Цифри по кожному магазину і по мережі загалом.",
  },
  {
    icon: Truck,
    title: "B2B-маркетплейс постачальників",
    text: "Каталог постачальників з цінами й умовами. Замовлення надсилаються постачальнику прямо із системи — без дзвінків і месенджерів.",
  },
  {
    icon: Thermometer,
    title: "IoT-моніторинг температури",
    text: "Датчики в холодильних вітринах передають температуру в реальному часі. Відхилення — миттєве сповіщення, поки товар ще можна врятувати.",
  },
  {
    icon: ScanLine,
    title: "Мобільний застосунок зі сканером",
    text: "Приймання, списання, інвентаризація та продаж — з телефона. Сканер штрихкодів працює через камеру, без додаткового обладнання.",
  },
  {
    icon: CalendarDays,
    title: "Графіки персоналу",
    text: "Змінні графіки для команди магазину в тій самій системі: хто, де і коли працює — видно і менеджеру, і працівникам у застосунку.",
  },
];

export function FeaturesSection() {
  return (
    <section id="features" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Все, що потрібно магазину, — в одній системі
          </h2>
          <p className="mt-4 text-lg text-slate-400">
            Від приймання товару до фіскального чека. Модулі вмикаються під ваш формат бізнесу.
          </p>
        </Reveal>

        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          {FEATURES.map((f, i) => (
            <Reveal key={f.title} delay={(i % 4) * 70}>
              <div className="h-full rounded-xl border border-white/[0.08] bg-white/[0.03] p-5 transition-colors hover:border-[#2D7DD2]/40">
                <div className="inline-flex rounded-lg bg-[#2D7DD2]/10 p-2.5">
                  <f.icon className="h-5 w-5 text-[#5EA3E8]" aria-hidden="true" />
                </div>
                <h3 className="mt-4 text-[15px] font-semibold text-white">{f.title}</h3>
                <p className="mt-2 text-sm leading-relaxed text-slate-400">{f.text}</p>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
