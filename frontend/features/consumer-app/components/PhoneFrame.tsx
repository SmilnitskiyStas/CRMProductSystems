import { useEffect, useRef, useState } from "react";
import type { CSSProperties, ReactNode } from "react";

/** The frame's decorative black bezel width (each side), in px — exported so callers computing a
 *  device-accurate inner content height (TASK-567's `AppPreviewPanel.tsx`) don't duplicate this
 *  magic number. */
export const PHONE_FRAME_BORDER_PX = 8;

/** TASK-568: leaves a small gap below the scaled frame so it never touches the viewport's bottom
 *  edge exactly. */
const FIT_BOTTOM_MARGIN_PX = 24;

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
  /** TASK-568: opt-in — when `true` (and `width`/`height` are both given), the frame is visually
   *  scaled down (CSS `transform: scale()`, true aspect ratio preserved) so its full height fits
   *  the viewport below wherever the frame is mounted, instead of forcing the page to scroll to
   *  see the bottom of a tall device preset (e.g. Pixel 8 Pro's 998px). Default `false`/omitted —
   *  byte-identical to today's unscaled behavior (`ThemeEditorSection.tsx`'s `ThemePreview` passes
   *  neither this nor `width`/`height` and must not change). */
  fitToViewport?: boolean;
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
export function PhoneFrame({ background, padding = 16, width, height, fitToViewport, children }: PhoneFrameProps) {
  const sized = width != null && height != null;
  const scale = usePhoneFrameFitScale(height, fitToViewport === true && sized);
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

  // TASK-568: unscaled path (default) is untouched below — same single `<div>` as before.
  if (!fitToViewport || !sized) {
    return <div style={style}>{children}</div>;
  }

  // `sized` (checked above) already guarantees both are numbers — cast rather than lean on
  // cross-variable control-flow narrowing, which TS doesn't reliably apply through `sized`.
  const w = width as number;
  const h = height as number;

  // TASK-568: outer box reserves the frame's SCALED footprint (so it doesn't force page scroll or
  // leave a gap in the layout), inner box renders the frame at its true 1:1 size and is scaled
  // visually — same technique Chrome DevTools/Figma device previews use. `scale.ref` measures the
  // outer box's own viewport offset (see `usePhoneFrameFitScale` below).
  return (
    <div ref={scale.ref} style={{ width: w * scale.value, height: h * scale.value, margin: "0 auto" }}>
      <div style={{ width: w, height: h, transform: `scale(${scale.value})`, transformOrigin: "top left" }}>
        <div style={style}>{children}</div>
      </div>
    </div>
  );
}

/**
 * TASK-568: computes the scale factor that shrinks a `deviceHeight`-tall frame to fit the vertical
 * room actually available below wherever it's mounted, so a tall device preset (Pixel 8 Pro,
 * 998px) never forces the outer dashboard page to scroll just to reveal the bottom of the mockup
 * (chrome + bottom nav, TASK-568 part 3). Deliberately a plain `window.innerHeight` +
 * `resize`-listener effect (no ResizeObserver) — this repo's only other window-size hook,
 * `useMediaQuery.ts`, is a boolean breakpoint check, not a continuous measurement, so this is kept
 * local to `PhoneFrame` rather than generalized into shared infrastructure nothing else needs yet.
 */
function usePhoneFrameFitScale(deviceHeight: number | undefined, enabled: boolean) {
  const ref = useRef<HTMLDivElement>(null);
  const [value, setValue] = useState(1);

  useEffect(() => {
    if (!enabled || deviceHeight == null) {
      setValue(1);
      return;
    }
    function recompute() {
      const top = ref.current?.getBoundingClientRect().top ?? 0;
      const available = window.innerHeight - top - FIT_BOTTOM_MARGIN_PX;
      setValue(Math.min(1, available / (deviceHeight as number)));
    }
    recompute();
    window.addEventListener("resize", recompute);
    return () => window.removeEventListener("resize", recompute);
  }, [enabled, deviceHeight]);

  return { ref, value };
}
