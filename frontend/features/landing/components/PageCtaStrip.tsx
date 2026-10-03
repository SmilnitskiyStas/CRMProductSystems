import { Button } from "@/components/ui/button";
import { Reveal } from "./Reveal";
import { Section } from "./Section";

// Short closing call-to-action used at the bottom of the content pages,
// above the full lead form.
export function PageCtaStrip({
  heading,
  text,
  cta,
}: {
  heading: string;
  text: string;
  cta: string;
}) {
  return (
    <Section className="!py-8 sm:!py-12">
      <Reveal>
        <div className="flex flex-col items-center justify-between gap-5 rounded-2xl border border-[var(--l-accent-strong)]/30 bg-[var(--l-accent-strong)]/[0.06] px-6 py-8 text-center sm:flex-row sm:px-10 sm:text-left">
          <div>
            <h2 className="text-2xl font-bold text-white">{heading}</h2>
            <p className="mt-2 text-[var(--l-muted)]">{text}</p>
          </div>
          <Button asChild size="lg" className="w-full shrink-0 sm:w-auto">
            <a href="#lead-form">{cta}</a>
          </Button>
        </div>
      </Reveal>
    </Section>
  );
}
