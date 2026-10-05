import { Reveal } from "./Reveal";

export interface RoadmapItem {
  title: string;
  text?: string;
}

export interface RoadmapColumn {
  title: string;
  subtitle: string;
  items: RoadmapItem[];
}

const TONES = {
  done: "bg-[var(--l-ok)]",
  inProgress: "bg-[var(--l-accent)]",
  planned: "bg-[var(--l-warn)]",
} as const;

export type RoadmapTone = keyof typeof TONES;

/** Renders only the columns that have items — empty ones are skipped. */
export function RoadmapColumns({
  columns,
}: {
  columns: { tone: RoadmapTone; column: RoadmapColumn }[];
}) {
  const visible = columns.filter(({ column }) => column.items.length > 0);
  const grid =
    visible.length >= 3 ? "lg:grid-cols-3" : visible.length === 2 ? "md:grid-cols-2" : "";
  const single = visible.length === 1;

  return (
    <div className={`grid gap-5 ${grid}`}>
      {visible.map(({ tone, column }, i) => (
        <Reveal key={tone} delay={i * 90}>
          <div className="h-full rounded-xl border border-[var(--l-border)] bg-[var(--l-surface)] p-6">
            <div className="flex items-center gap-2.5">
              <span className={`h-2.5 w-2.5 rounded-full ${TONES[tone]}`} aria-hidden="true" />
              <h2 className="text-lg font-semibold text-white">{column.title}</h2>
            </div>
            <p className="mt-1 text-sm text-[var(--l-muted)]">{column.subtitle}</p>
            <ul className={`mt-5 gap-3 ${single ? "grid sm:grid-cols-2 lg:grid-cols-3" : "space-y-3"}`}>
              {column.items.map((item) => (
                <li
                  key={item.title}
                  className="rounded-lg border border-white/[0.06] bg-white/[0.02] p-3.5"
                >
                  <p className="text-[15px] font-medium text-white">{item.title}</p>
                  {item.text && (
                    <p className="mt-1 text-sm leading-relaxed text-[var(--l-muted)]">{item.text}</p>
                  )}
                </li>
              ))}
            </ul>
          </div>
        </Reveal>
      ))}
    </div>
  );
}
