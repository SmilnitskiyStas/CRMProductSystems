"use client";

import { useEffect, useState } from "react";

/**
 * TASK-567: small reusable media-query hook. Returns whether `query` currently matches and stays
 * in sync via the `MediaQueryList`'s own `change` event — no manual `resize` listener + width
 * math needed, and it only re-renders on an actual breakpoint crossing rather than every pixel of
 * a drag-resize.
 *
 * Used by `AppBuilderCanvas.tsx` to decide whether its palette/canvas/preview row has real room
 * to stay on one line before falling back to `flexWrap: "wrap"` — see that component's remarks
 * for why a plain CSS `flex-wrap` threshold wraps prematurely on a real dashboard viewport
 * (sidebar nav + content padding eat into the browser window's raw width).
 *
 * SSR-safe default (`false` until the effect runs on mount) — this repo's other window-dependent
 * UI (e.g. `Sidebar.tsx`'s collapsed-group reposition-on-resize effect) follows the same
 * effect-only-touches-`window` pattern.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(false);

  useEffect(() => {
    const mql = window.matchMedia(query);
    setMatches(mql.matches);
    const handleChange = (e: MediaQueryListEvent) => setMatches(e.matches);
    mql.addEventListener("change", handleChange);
    return () => mql.removeEventListener("change", handleChange);
  }, [query]);

  return matches;
}
