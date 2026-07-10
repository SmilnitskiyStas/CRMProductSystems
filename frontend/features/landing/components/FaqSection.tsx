import { ChevronDown } from "lucide-react";
import { Reveal } from "./Reveal";

// Native <details>/<summary> — accessible, zero JS.

const FAQ = [
  {
    q: "Чи потрібне спеціальне обладнання?",
    a: "Ні. Система працює в браузері на будь-якому комп'ютері, а мобільний застосунок перетворює звичайний смартфон на сканер штрихкодів. IoT-датчики температури — опційний модуль, який підключається за бажанням.",
  },
  {
    q: "Скільки триває запуск?",
    a: "Зазвичай кілька робочих днів: налаштовуємо систему під ваш магазин, імпортуємо асортимент і проводимо навчання команди. Для мережі з кількох точок терміни узгоджуємо окремо.",
  },
  {
    q: "Чи працює система з ПРРО?",
    a: "Так. Вбудована інтеграція з Checkbox: фіскальні чеки, відкриття та закриття змін, Z-звіти. Кожен продаж автоматично списує залишок за принципом FEFO.",
  },
  {
    q: "У мене кілька магазинів. Це підтримується?",
    a: "Так, система з самого початку побудована для мереж: кожна точка ведеться окремо, переміщення між магазинами — документами, а зведена аналітика показує всю мережу на одному екрані.",
  },
  {
    q: "Мої дані в безпеці?",
    a: "Так. З'єднання шифрується (HTTPS/TLS), дані кожного клієнта ізольовані на рівні бази даних, доступ захищений двофакторною автентифікацією (2FA), а резервні копії створюються регулярно.",
  },
  {
    q: "Чи можна працювати з телефона?",
    a: "Так. Мобільний застосунок покриває щоденні операції: сканування, приймання поставок, списання, інвентаризація і навіть каса. Керівник бачить дашборд і сповіщення прямо в телефоні.",
  },
];

export function FaqSection() {
  return (
    <section id="faq" className="scroll-mt-20 py-16 sm:py-24">
      <div className="mx-auto max-w-3xl px-4 sm:px-6 lg:px-8">
        <Reveal className="text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Часті питання
          </h2>
        </Reveal>

        <Reveal className="mt-10" delay={80}>
          <div className="divide-y divide-white/[0.07] rounded-xl border border-white/[0.08] bg-white/[0.02]">
            {FAQ.map((item) => (
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
