import { Factory, Store, Warehouse, Wrench } from "lucide-react";
import { Reveal } from "./Reveal";

const SECONDARY = [
  {
    icon: Factory,
    title: "Виробництво",
    text: "Рецептури, виробничі партії, терміни готової продукції.",
  },
  {
    icon: Warehouse,
    title: "Склади",
    text: "Партійний облік, переміщення, контроль умов зберігання.",
  },
  {
    icon: Wrench,
    title: "Автосервіси",
    text: "Запчастини, роботи, замовлення-наряди — окремий модуль.",
  },
];

export function AudienceSection() {
  return (
    <section className="py-16 sm:py-24">
      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <Reveal className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-4xl">
            Для кого ця система
          </h2>
        </Reveal>

        <div className="mt-12 grid gap-5 lg:grid-cols-2">
          <Reveal>
            <div className="flex h-full flex-col rounded-xl border border-[#22c55e]/25 bg-[#22c55e]/[0.05] p-7">
              <div className="flex items-center gap-3">
                <div className="inline-flex rounded-lg bg-[#22c55e]/15 p-3">
                  <Store className="h-6 w-6 text-[#22c55e]" aria-hidden="true" />
                </div>
                <span className="rounded-full border border-[#22c55e]/30 bg-[#22c55e]/10 px-3 py-1 text-xs font-semibold text-[#22c55e]">
                  Основний профіль
                </span>
              </div>
              <h3 className="mt-5 text-xl font-semibold text-white">
                Продуктові магазини та мережі
              </h3>
              <p className="mt-3 leading-relaxed text-slate-400">
                ShelfGuard створений насамперед для продуктового рітейлу: короткі терміни
                придатності, щоденні поставки, десятки категорій. Один магазин чи мережа —
                система показує кожну точку окремо і всі разом.
              </p>
            </div>
          </Reveal>

          <div className="grid gap-5 sm:grid-cols-3 lg:grid-cols-1">
            {SECONDARY.map((item, i) => (
              <Reveal key={item.title} delay={i * 90}>
                <div className="flex h-full items-start gap-4 rounded-xl border border-white/[0.08] bg-white/[0.03] p-5">
                  <div className="inline-flex shrink-0 rounded-lg bg-white/[0.05] p-2.5">
                    <item.icon className="h-5 w-5 text-slate-300" aria-hidden="true" />
                  </div>
                  <div>
                    <h3 className="font-semibold text-white">{item.title}</h3>
                    <p className="mt-1 text-sm leading-relaxed text-slate-400">{item.text}</p>
                  </div>
                </div>
              </Reveal>
            ))}
          </div>
        </div>

        <Reveal className="mt-8">
          <p className="text-center text-sm text-slate-500">
            Система модульна: вмикаємо лише те, що потрібно вашому бізнесу.
          </p>
        </Reveal>
      </div>
    </section>
  );
}
