import { Check, Compass } from "lucide-react";
import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";
import { Section, SectionHeading, TeaserLink } from "./Section";

// Homepage "what exists / what's next" block. Content is only shipped
// capabilities plus a pointer to /roadmap — no invented plans.
export async function MiniRoadmapSection() {
  const t = await getTranslations("Landing.miniRoadmap");
  const tTeasers = await getTranslations("Landing.teasers");
  const done = t.raw("done") as string[];

  return (
    <Section id="roadmap">
      <SectionHeading title={t("heading")} subtitle={t("subheading")} />
      <div className="mt-12 grid gap-5 lg:grid-cols-5">
        <Reveal className="lg:col-span-3">
          <div className="h-full rounded-xl border border-[var(--l-ok)]/25 bg-[var(--l-ok)]/[0.05] p-6 sm:p-7">
            <h3 className="flex items-center gap-2.5 text-lg font-semibold text-white">
              <span className="h-2.5 w-2.5 rounded-full bg-[var(--l-ok)]" aria-hidden="true" />
              {t("doneTitle")}
            </h3>
            <ul className="mt-5 grid gap-3 sm:grid-cols-2">
              {done.map((item) => (
                <li key={item} className="flex items-start gap-3 text-[15px] text-slate-300">
                  <Check className="mt-0.5 h-5 w-5 shrink-0 text-[var(--l-ok)]" aria-hidden="true" />
                  {item}
                </li>
              ))}
            </ul>
          </div>
        </Reveal>
        <Reveal className="lg:col-span-2" delay={100}>
          <div className="flex h-full flex-col rounded-xl border border-[var(--l-border)] bg-[var(--l-surface)] p-6 sm:p-7">
            <h3 className="flex items-center gap-2.5 text-lg font-semibold text-white">
              <Compass className="h-5 w-5 text-[var(--l-accent)]" aria-hidden="true" />
              {t("nextTitle")}
            </h3>
            <p className="mt-4 flex-1 text-[15px] leading-relaxed text-[var(--l-muted)]">
              {t("nextText")}
            </p>
            <div className="mt-5">
              <TeaserLink href="/roadmap">{tTeasers("roadmap")}</TeaserLink>
            </div>
          </div>
        </Reveal>
      </div>
    </Section>
  );
}
