import type { CSSProperties, ReactNode } from "react";

interface PhoneFrameProps {
  /** The phone screen's background color (e.g. the tenant's `MobileThemeDto.backgroundColor`). */
  background: string;
  /** Inner padding of the screen area, in px. */
  padding?: number;
  children: ReactNode;
}

/**
 * TASK-563: shared phone-chrome mockup, extracted verbatim from `ThemeEditorSection.tsx`'s
 * `ThemePreview` (the outer bordered device frame it used to inline) so `AppPreviewPanel`
 * (TASK-564) can render the App Builder's live block preview inside the identical device frame —
 * same 320px max-width, 28px radius, 8px black border, drop shadow.
 *
 * Deliberately does NOT impose a `display: flex` / `gap` layout on its own children (the original
 * inlined div did, driven by `ThemeEditorSection`'s spacing-preset metric) — that was specific to
 * ThemePreview's own 4-element mock. Callers that need a gapped vertical stack wrap their own
 * content in an inner flex div and pass that as `children`, keeping this component a pure chrome
 * shell reusable for any content (a handful of static mock elements, or a scrollable block list).
 */
export function PhoneFrame({ background, padding = 16, children }: PhoneFrameProps) {
  const style: CSSProperties = {
    width: "100%",
    maxWidth: 320,
    margin: "0 auto",
    background,
    borderRadius: 28,
    border: "8px solid #000",
    padding,
    boxShadow: "0 12px 30px rgba(0,0,0,0.35)",
  };

  return <div style={style}>{children}</div>;
}
