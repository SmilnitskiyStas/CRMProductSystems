import { Factory, Store, Warehouse, Wrench, type LucideIcon } from "lucide-react";
import { Link } from "@/i18n/navigation";
import { Reveal } from "./Reveal";
import { StatusBadge } from "./Section";

const ICONS: Record<string, LucideIcon> = {
  retail: Store,
  autoService: Wrench,
  production: Factory,
  warehouses: Warehouse,
};

export interface VerticalData {
  id: string;
  title: string;
  status: "ready" | "soon";
  text: string;
  href?: string;
}

export function VerticalCard({
  vertical,
  readyLabel,
  soonLabel,
  moreLabel,
  index,
}: {
  vertical: VerticalData;
  readyLabel: string;
  soonLabel: string;
  moreLabel: string;
  index: number;
}) {
  const Icon = ICONS[vertical.id] ?? Store;
  const ready = vertical.status === "ready";
  return (
    <Reveal delay={index * 80}>
      <div
        className={`flex h-full flex-col rounded-xl border p-6 ${
          ready
            ? "border-[var(--l-ok)]/25 bg-[var(--l-ok)]/[0.05]"
            : "border-[var(--l-border)] bg-[var(--l-surface)]"
        }`}
      >
        <div className="flex items-center justify-between gap-3">
          <div
            className={`inline-flex rounded-lg p-3 ${ready ? "bg-[var(--l-ok)]/15" : "bg-white/[0.05]"}`}
          >
            <Icon
              className={`h-6 w-6 ${ready ? "text-[var(--l-ok)]" : "text-slate-300"}`}
              aria-hidden="true"
            />
          </div>
          <StatusBadge tone={vertical.status}>{ready ? readyLabel : soonLabel}</StatusBadge>
        </div>
        <h3 className="mt-5 text-xl font-semibold text-white">{vertical.title}</h3>
        <p className="mt-3 flex-1 leading-relaxed text-[var(--l-muted)]">{vertical.text}</p>
        {ready && vertical.href && (
          <Link
            href={vertical.href}
            className="mt-5 text-[15px] font-medium text-[var(--l-accent)] transition-colors hover:text-white"
          >
            {moreLabel} →
          </Link>
        )}
      </div>
    </Reveal>
  );
}
