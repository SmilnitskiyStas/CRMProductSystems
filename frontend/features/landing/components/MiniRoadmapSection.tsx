import { getTranslations } from "next-intl/server";
import { Reveal } from "./Reveal";
import { RoadmapProgress } from "./RoadmapProgress";
import { Section, SectionHeading, TeaserLink } from "./Section";

// Homepage "what exists / what's next" block: the same segmented progress bar
// as /roadmap (counts derived from the roadmap messages) plus a link.
export async function MiniRoadmapSection() {
  const t = await getTranslations("Landing.miniRoadmap");
  const tTeasers = await getTranslations("Landing.teasers");

  return (
    <Section id="roadmap">
      <SectionHeading title={t("heading")} subtitle={t("subheading")} />
      <Reveal className="mx-auto mt-12 max-w-3xl">
        <RoadmapProgress />
        <p className="mt-5 text-center text-[15px] leading-relaxed text-[var(--l-muted)]">
          {t("nextText")}
        </p>
        <div className="mt-4 text-center">
          <TeaserLink href="/roadmap">{tTeasers("roadmap")}</TeaserLink>
        </div>
      </Reveal>
    </Section>
  );
}
