"use client";

import { useEffect } from "react";

/**
 * TASK-546: warns before losing unsaved edits on the three consumer-app editor screens
 * (AppBuilderCanvas / ThemeEditorSection / NavigationBuilderSection). Next.js App Router has no
 * first-class in-app route-block API (no `useBlocker`/`usePrompt` equivalent — checked; the
 * Pages Router's old `router.events`-based approach doesn't exist here) so this covers what's
 * actually reachable from a client component:
 *
 * 1. Tab close / refresh / typed-URL navigation — native `beforeunload`. The browser owns the
 *    confirmation text in every modern browser (a custom `message` is ignored); this only
 *    controls whether the prompt appears at all.
 * 2. In-app navigation triggered by clicking an `<a>` element — every in-app link in this
 *    codebase (Sidebar, breadcrumbs, etc.) renders through `next/link`, which is a real `<a>`
 *    with next/link's own navigation wired to a normal bubble-phase `onClick`. A CAPTURE-phase
 *    listener on `document` runs before that (capture fires top-down before the target's own
 *    bubble-phase handlers), so `preventDefault`+`stopPropagation` here reliably blocks the
 *    navigation — including links rendered by Sidebar.tsx, which lives outside these editor
 *    components' own subtree, not just clicks inside the editor screen itself.
 *
 * NOT covered (documented honestly, not silently missing): browser Back/Forward (`popstate`) and
 * any programmatic `router.push()` call that isn't the result of an `<a>` click — neither exists
 * inside these three screens today, but a future one would bypass this guard.
 *
 * @param dirty Whether the screen currently has unsaved edits.
 * @param confirmMessage Translated confirmation text for the in-app link-click guard (case 2
 *   above) — the caller supplies this via `useTranslations` so the prompt is localized like every
 *   other string in this feature area; `beforeunload`'s own prompt (case 1) can't be customized by
 *   any modern browser, so this text is never shown there.
 */
export function useUnsavedChangesGuard(dirty: boolean, confirmMessage: string): void {
  useEffect(() => {
    if (!dirty) return;

    function handleBeforeUnload(e: BeforeUnloadEvent) {
      e.preventDefault();
      e.returnValue = "";
    }

    function handleClick(e: MouseEvent) {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;

      const anchor = (e.target as HTMLElement | null)?.closest("a");
      if (!anchor) return;
      if (anchor.target && anchor.target !== "_self") return; // new-tab links navigate away, current page stays put

      const href = anchor.getAttribute("href");
      if (!href || href.startsWith("#") || href.startsWith("mailto:") || href.startsWith("tel:")) return;

      let destination: URL;
      try {
        destination = new URL(href, window.location.href);
      } catch {
        return;
      }
      if (destination.origin !== window.location.origin) return; // external link — browser handles it, nothing to guard
      if (destination.pathname === window.location.pathname && destination.search === window.location.search) return;

      // eslint-disable-next-line no-alert -- deliberate confirm() gate, matches BannersSection's window.confirm convention
      if (!window.confirm(confirmMessage)) {
        e.preventDefault();
        e.stopPropagation();
      }
    }

    window.addEventListener("beforeunload", handleBeforeUnload);
    document.addEventListener("click", handleClick, true);
    return () => {
      window.removeEventListener("beforeunload", handleBeforeUnload);
      document.removeEventListener("click", handleClick, true);
    };
  }, [dirty, confirmMessage]);
}
