import { getTranslations } from "next-intl/server";
import type { RoadmapColumn } from "./RoadmapTimeline";

export type RoadmapTone = "done" | "inProgress" | "planned";

export const ROADMAP_TONE_BG: Record<RoadmapTone, string> = {
  done: "bg-[var(--l-ok)]",
  inProgress: "bg-[var(--l-accent)]",
  planned: "bg-[var(--l-warn)]",
};

/** Reads the three roadmap columns from messages (single source for page + homepage). */
export async function getRoadmapColumns(): Promise<Record<RoadmapTone, RoadmapColumn>> {
  const t = await getTranslations("Landing.pages.roadmap");
  return {
    done: t.raw("columns.done") as RoadmapColumn,
    inProgress: t.raw("columns.inProgress") as RoadmapColumn,
    planned: t.raw("columns.planned") as RoadmapColumn,
  };
}

/**
 * Segmented overall progress bar: segment widths are proportional to item
 * counts. Counts only — no percentages, no dates.
 */
export async function RoadmapProgress({ className = "" }: { className?: string }) {
  const t = await getTranslations("Landing.pages.roadmap");
  const columns = await getRoadmapColumns();

  const counts = {
    done: columns.done.items.length,
    inProgress: columns.inProgress.items.length,
    planned: columns.planned.items.length,
  };
  const total = counts.done + counts.inProgress + counts.planned;
  if (total === 0) return null;

  const tones: RoadmapTone[] = ["done", "inProgress", "planned"];
  const ariaLabel = t("progress.ariaLabel", { ...counts, total });

  return (
    <div
      className={`rounded-xl border border-[var(--l-border)] bg-[var(--l-surface)] p-5 sm:p-6 ${className}`}
    >
      <p className="text-base font-semibold text-white sm:text-lg">
        {t("progress.summary", { done: counts.done, total })}
      </p>

      <div
        role="img"
        aria-label={ariaLabel}
        className="mt-4 flex h-3 w-full gap-0.5 overflow-hidden rounded-full bg-white/[0.04]"
      >
        {tones
          .filter((tone) => counts[tone] > 0)
          .map((tone) => (
            <span
              key={tone}
              className={`h-full ${ROADMAP_TONE_BG[tone]} ${tone === "inProgress" ? "rm-shimmer" : ""}`}
              style={{ flexGrow: counts[tone], flexBasis: 0 }}
            />
          ))}
      </div>

      <ul className="mt-4 flex flex-wrap gap-x-6 gap-y-2 text-sm text-[var(--l-muted)]">
        {tones
          .filter((tone) => counts[tone] > 0)
          .map((tone) => (
            <li key={tone} className="flex items-center gap-2">
              <span
                className={`h-2.5 w-2.5 rounded-full ${ROADMAP_TONE_BG[tone]}`}
                aria-hidden="true"
              />
              <span>
                {columns[tone].title}:{" "}
                <span className="font-medium text-white">{counts[tone]}</span>
              </span>
            </li>
          ))}
      </ul>
    </div>
  );
}
