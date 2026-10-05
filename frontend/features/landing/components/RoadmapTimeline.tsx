import { Check, Circle } from "lucide-react";
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

type Tone = "done" | "inProgress" | "planned";

const GRID_COLS: Record<number, string> = {
  1: "lg:grid-cols-1",
  2: "lg:grid-cols-2",
  3: "lg:grid-cols-3",
};

const NODE: Record<Tone, string> = {
  done: "border-[var(--l-ok)] bg-[var(--l-ok)] text-[#06210f]",
  inProgress: "border-[var(--l-accent)] bg-[var(--l-bg)]",
  planned: "border-[var(--l-warn)] bg-[var(--l-bg)]",
};

function StageNode({ tone }: { tone: Tone }) {
  return (
    <span
      className={`absolute left-0 top-0 z-10 flex h-8 w-8 items-center justify-center rounded-full border-2 lg:left-1/2 lg:-translate-x-1/2 ${NODE[tone]}`}
      aria-hidden="true"
    >
      {tone === "done" && <Check className="h-4 w-4" strokeWidth={3} />}
      {tone === "inProgress" && (
        <span className="rm-pulse h-2.5 w-2.5 rounded-full bg-[var(--l-accent)]" />
      )}
      {tone === "planned" && <span className="h-2 w-2 rounded-full border border-[var(--l-warn)]" />}
    </span>
  );
}

function ItemMarker({ tone }: { tone: Tone }) {
  if (tone === "done") {
    return <Check className="mt-0.5 h-4 w-4 shrink-0 text-[var(--l-ok)]" aria-hidden="true" />;
  }
  if (tone === "inProgress") {
    return (
      <span className="mt-1 flex h-4 w-4 shrink-0 items-center justify-center" aria-hidden="true">
        <span className="rm-pulse h-2.5 w-2.5 rounded-full bg-[var(--l-accent)]" />
      </span>
    );
  }
  return <Circle className="mt-0.5 h-4 w-4 shrink-0 text-[var(--l-warn)]" aria-hidden="true" />;
}

/**
 * Stage track (Готово -> В роботі -> Заплановано): horizontal on desktop,
 * vertical on mobile. The rail is filled up to the in-progress stage.
 * Empty stages are skipped.
 */
export function RoadmapTimeline({
  stages,
}: {
  stages: { tone: Tone; column: RoadmapColumn }[];
}) {
  const visible = stages.filter(({ column }) => column.items.length > 0);
  if (visible.length === 0) return null;

  const progressIdx = (() => {
    const inProg = visible.findIndex((s) => s.tone === "inProgress");
    if (inProg >= 0) return inProg;
    return visible.reduce((acc, s, i) => (s.tone === "done" ? i : acc), -1);
  })();
  const last = visible.length - 1;
  const fill = "bg-[var(--l-accent)]";
  const empty = "bg-white/10";

  return (
    <ol className={`grid ${GRID_COLS[visible.length] ?? "lg:grid-cols-3"}`}>
      {visible.map(({ tone, column }, i) => {
        const beforeFilled = i <= progressIdx;
        const afterFilled = i + 1 <= progressIdx;
        return (
          <li key={tone} className="relative pb-10 pl-12 last:pb-0 lg:pb-0 lg:pl-0 lg:pt-12">
            {/* mobile rail: above + below the node */}
            {i > 0 && (
              <span
                className={`absolute left-[15px] top-0 h-4 w-0.5 lg:hidden ${beforeFilled ? fill : empty}`}
                aria-hidden="true"
              />
            )}
            {i < last && (
              <span
                className={`absolute bottom-0 left-[15px] top-4 w-0.5 lg:hidden ${afterFilled ? fill : empty}`}
                aria-hidden="true"
              />
            )}
            {/* desktop rail: left and right halves at node level */}
            {i > 0 && (
              <span
                className={`absolute left-0 top-[15px] hidden h-0.5 w-1/2 lg:block ${beforeFilled ? fill : empty}`}
                aria-hidden="true"
              />
            )}
            {i < last && (
              <span
                className={`absolute right-0 top-[15px] hidden h-0.5 w-1/2 lg:block ${afterFilled ? fill : empty}`}
                aria-hidden="true"
              />
            )}
            <StageNode tone={tone} />

            <Reveal delay={i * 90}>
              <div className="lg:px-3">
                <div className="flex min-h-8 flex-col justify-center lg:items-center lg:text-center">
                  <h2 className="text-lg font-semibold text-white">
                    {column.title}{" "}
                    <span className="text-sm font-normal text-[var(--l-muted)]">
                      · {column.items.length}
                    </span>
                  </h2>
                  <p className="text-sm text-[var(--l-muted)]">{column.subtitle}</p>
                </div>
                <ul className="mt-5 space-y-3">
                  {column.items.map((item) => (
                    <li
                      key={item.title}
                      className="flex gap-3 rounded-lg border border-[var(--l-border)] bg-[var(--l-surface)] p-3.5"
                    >
                      <ItemMarker tone={tone} />
                      <div>
                        <p className="text-[15px] font-medium text-white">{item.title}</p>
                        {item.text && (
                          <p className="mt-1 text-sm leading-relaxed text-[var(--l-muted)]">
                            {item.text}
                          </p>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            </Reveal>
          </li>
        );
      })}
    </ol>
  );
}
