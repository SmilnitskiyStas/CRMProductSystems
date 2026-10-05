import {
  Boxes,
  Brain,
  CalendarClock,
  Check,
  Factory,
  Receipt,
  Smartphone,
  Thermometer,
  Truck,
  Users,
  type LucideIcon,
} from "lucide-react";
import { Reveal } from "./Reveal";

const ICONS: Record<string, LucideIcon> = {
  stock: CalendarClock,
  sales: Receipt,
  ai: Brain,
  marketplace: Truck,
  iot: Thermometer,
  loyalty: Users,
  production: Factory,
  mobile: Smartphone,
  team: Boxes,
};

export interface FeatureGroupData {
  id: string;
  title: string;
  solves: string;
  text: string;
  items: string[];
}

export function FeatureGroup({
  group,
  solvesLabel,
  index,
}: {
  group: FeatureGroupData;
  solvesLabel: string;
  index: number;
}) {
  const Icon = ICONS[group.id] ?? Boxes;
  return (
    <Reveal delay={(index % 2) * 80}>
      <article
        id={group.id}
        className="h-full scroll-mt-24 rounded-xl border border-[var(--l-border)] bg-[var(--l-surface)] p-6 transition-colors hover:border-[var(--l-accent-strong)]/40 sm:p-7"
      >
        <div className="flex items-center gap-3">
          <div className="inline-flex rounded-lg bg-[var(--l-accent-strong)]/10 p-2.5">
            <Icon className="h-5 w-5 text-[var(--l-accent)]" aria-hidden="true" />
          </div>
          <h2 className="text-xl font-semibold text-white">{group.title}</h2>
        </div>
        <p className="mt-4 text-[15px] leading-relaxed text-slate-300">{group.text}</p>
        <p className="mt-3 text-sm text-[var(--l-muted)]">
          <span className="font-medium text-slate-300">{solvesLabel}:</span> {group.solves}
        </p>
        <ul className="mt-5 space-y-2.5">
          {group.items.map((item) => (
            <li key={item} className="flex items-start gap-3 text-sm text-slate-300">
              <Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--l-ok)]" aria-hidden="true" />
              {item}
            </li>
          ))}
        </ul>
      </article>
    </Reveal>
  );
}
