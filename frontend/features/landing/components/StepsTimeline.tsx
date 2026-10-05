import { Reveal } from "./Reveal";

export interface TimelineStep {
  number: string;
  title: string;
  text: string;
}

export function StepsTimeline({ steps }: { steps: TimelineStep[] }) {
  return (
    <ol className="relative mx-auto max-w-3xl space-y-8">
      <div
        aria-hidden="true"
        className="absolute bottom-4 left-7 top-4 w-px bg-gradient-to-b from-transparent via-white/15 to-transparent"
      />
      {steps.map((step, i) => (
        <li key={step.number}>
          <Reveal delay={i * 70}>
            <div className="relative flex gap-5">
              <div className="relative z-10 inline-flex h-14 w-14 shrink-0 items-center justify-center rounded-full border border-[var(--l-accent-strong)]/40 bg-[var(--l-bg)] text-lg font-bold text-[var(--l-accent)]">
                {step.number}
              </div>
              <div className="pt-2">
                <h3 className="text-lg font-semibold text-white">{step.title}</h3>
                <p className="mt-1.5 text-[15px] leading-relaxed text-[var(--l-muted)]">
                  {step.text}
                </p>
              </div>
            </div>
          </Reveal>
        </li>
      ))}
    </ol>
  );
}
