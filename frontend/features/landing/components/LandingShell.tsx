import "@/features/landing/landing.css";
import { LandingHeader } from "./LandingHeader";
import { LandingFooter } from "./LandingFooter";

// Common frame for every public landing page: palette scope ([data-landing]),
// header and footer. Pages supply <main> content.
export function LandingShell({ children }: { children: React.ReactNode }) {
  return (
    <div
      data-landing
      className="min-h-screen bg-[var(--l-bg)] text-slate-200 antialiased"
    >
      <LandingHeader />
      <main>{children}</main>
      <LandingFooter />
    </div>
  );
}
