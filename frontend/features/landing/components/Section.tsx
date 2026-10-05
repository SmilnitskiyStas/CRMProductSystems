import { ArrowRight } from "lucide-react";
import { Link } from "@/i18n/navigation";
import { Reveal } from "./Reveal";

// Shared layout primitives for the public landing pages. Palette comes from
// the --l-* variables in landing.css.

export function Container({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`mx-auto max-w-6xl px-4 sm:px-6 lg:px-8 ${className}`}>{children}</div>
  );
}

export function Section({
  id,
  children,
  className = "",
}: {
  id?: string;
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <section id={id} className={`scroll-mt-20 py-16 sm:py-24 ${className}`}>
      <Container>{children}</Container>
    </section>
  );
}

export function SectionHeading({
  title,
  subtitle,
  eyebrow,
  align = "center",
}: {
  title: string;
  subtitle?: string;
  eyebrow?: string;
  align?: "center" | "left";
}) {
  return (
    <Reveal className={align === "center" ? "mx-auto max-w-2xl text-center" : "max-w-2xl"}>
      {eyebrow && (
        <p className="text-sm font-semibold uppercase tracking-wider text-[var(--l-accent)]">
          {eyebrow}
        </p>
      )}
      <h2 className="mt-1 text-3xl font-bold tracking-tight text-white sm:text-4xl">{title}</h2>
      {subtitle && <p className="mt-4 text-lg text-[var(--l-muted)]">{subtitle}</p>}
    </Reveal>
  );
}

export function PageHero({
  eyebrow,
  title,
  description,
  children,
}: {
  eyebrow: string;
  title: string;
  description: string;
  children?: React.ReactNode;
}) {
  return (
    <section id="top" className="relative overflow-hidden pb-12 pt-32 sm:pb-16 sm:pt-40">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 -z-10 bg-[linear-gradient(to_right,rgba(255,255,255,0.025)_1px,transparent_1px),linear-gradient(to_bottom,rgba(255,255,255,0.025)_1px,transparent_1px)] bg-[size:56px_56px] [mask-image:radial-gradient(ellipse_60%_50%_at_50%_0%,black,transparent)]"
      />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute left-1/2 top-0 -z-10 h-[400px] w-[800px] max-w-full -translate-x-1/2 rounded-full bg-[var(--l-accent-strong)]/[0.12] blur-[140px]"
      />
      <Container>
        <div className="mx-auto max-w-3xl text-center">
          <p className="hero-fade hero-fade-1 inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-4 py-1.5 text-[13px] text-slate-300">
            <span className="h-1.5 w-1.5 rounded-full bg-[var(--l-ok)]" />
            {eyebrow}
          </p>
          <h1 className="hero-fade hero-fade-2 mt-6 text-4xl font-bold leading-[1.1] tracking-tight text-white sm:text-5xl">
            {title}
          </h1>
          <p className="hero-fade hero-fade-3 mx-auto mt-6 max-w-2xl text-lg leading-relaxed text-[var(--l-muted)]">
            {description}
          </p>
          {children && <div className="hero-fade hero-fade-4 mt-8">{children}</div>}
        </div>
      </Container>
    </section>
  );
}

/** "Learn more" arrow link used by homepage teasers. */
export function TeaserLink({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <Link
      href={href}
      className="group inline-flex items-center gap-1.5 text-[15px] font-medium text-[var(--l-accent)] transition-colors hover:text-white"
    >
      {children}
      <ArrowRight
        className="h-4 w-4 transition-transform group-hover:translate-x-0.5"
        aria-hidden="true"
      />
    </Link>
  );
}

/** Small status pill: green for ready, amber for coming soon. */
export function StatusBadge({
  tone,
  children,
}: {
  tone: "ready" | "soon";
  children: React.ReactNode;
}) {
  const cls =
    tone === "ready"
      ? "border-[var(--l-ok)]/30 bg-[var(--l-ok)]/10 text-[var(--l-ok)]"
      : "border-[var(--l-warn)]/30 bg-[var(--l-warn)]/10 text-[var(--l-warn)]";
  return (
    <span className={`rounded-full border px-3 py-1 text-xs font-semibold ${cls}`}>{children}</span>
  );
}
