"use client";

import { useEffect, useRef } from "react";
import { getToken } from "@/lib/api";
import { useLogout } from "./useAuth";

/**
 * Hardcoded, not a tenant setting — 5 minutes with no mouse/keyboard/scroll/touch
 * activity anywhere in the authenticated app force-logs-out the user. No warning
 * modal by design: the logout at the threshold is silent and immediate.
 */
export const IDLE_TIMEOUT_MS =
  Number(process.env.NEXT_PUBLIC_IDLE_TIMEOUT_MS) > 0
    ? Number(process.env.NEXT_PUBLIC_IDLE_TIMEOUT_MS) // override for local/QA verification only
    : 5 * 60 * 1000;

/** Pure idle check — kept separate so the threshold logic is unit-testable. */
export function isIdle(now: number, lastActivity: number, timeoutMs: number = IDLE_TIMEOUT_MS): boolean {
  return now - lastActivity >= timeoutMs;
}

// Cross-tab sync channel. The access token and the refresh cookie are shared across
// every tab of this origin, so activity in one tab must reset the idle clock in all
// of them, and an idle logout triggered in one tab must log out every tab. Writing
// to `localStorage` fires a `storage` event in every OTHER open tab (never the tab
// that wrote it), which is exactly the cross-tab signal we need here.
const LAST_ACTIVITY_KEY = "sg_last_activity";

// Avoid hammering localStorage on every mousemove/scroll tick.
const ACTIVITY_WRITE_THROTTLE_MS = 5000;

// How often each tab re-checks whether the idle threshold has been crossed.
const CHECK_INTERVAL_MS = 5000;

const ACTIVITY_EVENTS = [
  "mousemove",
  "mousedown",
  "click",
  "keydown",
  "wheel",
  "scroll",
  "touchstart",
] as const;

/**
 * Mount once, high in the authenticated tree (DashboardChrome), so it stays active
 * across every dashboard/provider/supplier/analytics route. Tracks last-activity
 * time in a ref (never triggers a re-render) and only attaches listeners while a
 * token is present.
 */
export function useIdleLogout(): void {
  const logout = useLogout();
  const lastActivityRef = useRef<number>(Date.now());
  const lastWriteRef = useRef<number>(0);
  const loggedOutRef = useRef(false);

  useEffect(() => {
    if (!getToken()) return;

    const recordActivity = (at: number) => {
      lastActivityRef.current = at;
      if (at - lastWriteRef.current >= ACTIVITY_WRITE_THROTTLE_MS) {
        lastWriteRef.current = at;
        try {
          localStorage.setItem(LAST_ACTIVITY_KEY, String(at));
        } catch {
          // localStorage unavailable (private mode / quota) — this tab still
          // tracks its own activity locally, it just won't sync cross-tab.
        }
      }
    };

    const handleActivity = () => recordActivity(Date.now());

    // Another tab reported activity (or, in principle, an idle logout — but a
    // logout clears the token, so this tab's own next check picks that up too
    // once it re-reads getToken() indirectly via the redirect from that tab).
    const handleStorage = (e: StorageEvent) => {
      // Another tab logged out (idle or manual) and cleared the shared token.
      if (e.key === "sg_token" && e.newValue === null && !loggedOutRef.current) {
        loggedOutRef.current = true;
        window.location.href = "/login";
        return;
      }
      if (e.key !== LAST_ACTIVITY_KEY || !e.newValue) return;
      const at = Number(e.newValue);
      if (Number.isFinite(at) && at > lastActivityRef.current) {
        lastActivityRef.current = at;
      }
    };

    for (const event of ACTIVITY_EVENTS) {
      window.addEventListener(event, handleActivity, { passive: true });
    }
    window.addEventListener("storage", handleStorage);

    const interval = setInterval(() => {
      if (loggedOutRef.current) return;
      if (isIdle(Date.now(), lastActivityRef.current)) {
        loggedOutRef.current = true;
        logout.mutate("idle_timeout");
      }
    }, CHECK_INTERVAL_MS);

    // Timers are throttled in background tabs — re-check as soon as the tab is shown
    // again. Deliberately does NOT count as activity: only real input resets the clock.
    const handleVisibility = () => {
      if (document.visibilityState !== "visible" || loggedOutRef.current) return;
      if (isIdle(Date.now(), lastActivityRef.current)) {
        loggedOutRef.current = true;
        logout.mutate("idle_timeout");
      }
    };
    document.addEventListener("visibilitychange", handleVisibility);

    return () => {
      for (const event of ACTIVITY_EVENTS) {
        window.removeEventListener(event, handleActivity);
      }
      window.removeEventListener("storage", handleStorage);
      document.removeEventListener("visibilitychange", handleVisibility);
      clearInterval(interval);
    };
    // Intentionally mount-once: `logout.mutate` closes over the router/queryClient
    // instances from first render, both stable for the lifetime of this component.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}
