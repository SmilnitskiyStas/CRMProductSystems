import type { CSSProperties, ReactNode } from "react";

/** The frame's decorative black bezel width (each side), in px — exported so callers computing a
 *  device-accurate inner content height (TASK-567's `AppPreviewPanel.tsx`) don't duplicate this
 *  magic number. */
export const PHONE_FRAME_BORDER_PX = 8;

interface PhoneFrameProps {
  /** The phone screen's background color (e.g. the tenant's `MobileThemeDto.backgroundColor`). */
  background: string;
  /** Inner padding of the screen area, in px. */
  padding?: number;
  /** TASK-567: exact device screen width, in CSS px (e.g. a `devicePresets.ts` entry). When given
   *  together with `height`, the frame renders at that literal fixed size — border-box, so the
   *  rendered box (border included) is pixel-exact to the device's real screen — instead of the
   *  default fluid `width: 100%, maxWidth: 320` box. Omit to keep today's fluid behavior exactly
   *  (`ThemeEditorSection.tsx`'s `ThemePreview` relies on this omission and must not change). */
  width?: number;
  /** TASK-567: exact device screen height, in CSS px, paired with `width`. The frame itself never
   *  grows past this — `children` overflowing it are clipped at the frame boundary (the same way a
   *  real phone's screen doesn't get taller; content scrolls). Callers own their own inner
   *  `overflowY: auto` wrapper sized to fit inside this height (see `PHONE_FRAME_BORDER_PX`
   *  above). Omit to keep today's unconstrained-height behavior. */
  height?: number;
  children: ReactNode;
}

/**
 * TASK-563: shared phone-chrome mockup, extracted verbatim from `ThemeEditorSection.tsx`'s
 * `ThemePreview` (the outer bordered device frame it used to inline) so `AppPreviewPanel`
 * (TASK-564) can render the App Builder's live block preview inside the identical device frame —
 * same 320px max-width, 28px radius, 8px black border, drop shadow.
 *
 * TASK-567: gained optional `width`/`height` for an accurate fixed-size device stand-in (see prop
 * docs above) — purely additive, the no-args fluid path is byte-identical to before.
 *
 * Deliberately does NOT impose a `display: flex` / `gap` layout on its own children (the original
 * inlined div did, driven by `ThemeEditorSection`'s spacing-preset metric) — that was specific to
 * ThemePreview's own 4-element mock. Callers that need a gapped vertical stack wrap their own
 * content in an inner flex div and pass that as `children`, keeping this component a pure chrome
 * shell reusable for any content (a handful of static mock elements, or a scrollable block list).
 */
export function PhoneFrame({ background, padding = 16, width, height, children }: PhoneFrameProps) {
  const sized = width != null && height != null;
  const style: CSSProperties = {
    width: sized ? width : "100%",
    maxWidth: sized ? undefined : 320,
    height: sized ? height : undefined,
    boxSizing: sized ? "border-box" : undefined,
    overflow: sized ? "hidden" : undefined,
    margin: "0 auto",
    background,
    borderRadius: 28,
    border: `${PHONE_FRAME_BORDER_PX}px solid #000`,
    padding,
    boxShadow: "0 12px 30px rgba(0,0,0,0.35)",
  };

  return <div style={style}>{children}</div>;
}
