import { Brain, CalendarClock, Receipt, Thermometer, type LucideIcon } from "lucide-react";
import { getTranslations } from "next-intl/server";

const ICONS: LucideIcon[] = [CalendarClock, Receipt, Brain, Thermometer];

// Strip of real, already-shipped capabilities shown under the hero.
export async function TrustStrip() {
  const t = await getTranslations("Landing.trust");
  const items = t.raw("items") as { title: string; text: string }[];

  return (
    <div className="hero-fade hero-fade-4 mx-auto mt-14 max-w-5xl">
      <p className="text-center text-xs font-semibold uppercase tracking-wider text-slate-500">
        {t("label")}
      </p>
      <ul className="mt-4 grid grid-cols-2 gap-3 lg:grid-cols-4">
        {items.map((item, i) => {
          const Icon = ICONS[i];
          return (
            <li
              key={item.title}
              className="flex items-start gap-3 rounded-xl border border-[var(--l-border)] bg-[var(--l-surface)] p-3.5 sm:p-4"
            >
              <Icon className="mt-0.5 h-5 w-5 shrink-0 text-[var(--l-accent)]" aria-hidden="true" />
              <div>
                <p className="text-sm font-semibold text-white">{item.title}</p>
                <p className="mt-0.5 text-xs leading-snug text-[var(--l-muted)]">{item.text}</p>
              </div>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
