import { useEffect, useRef } from 'react';

/** 'cashier-activity' is dispatched for input that is not a DOM event (e.g. a camera barcode scan). */
export const CASHIER_ACTIVITY_EVENT = 'cashier-activity';
const ACTIVITY_EVENTS = ['keydown', 'mousedown', 'mousemove', 'touchstart', 'wheel', CASHIER_ACTIVITY_EVENT] as const;

/** Calls onIdle after `timeoutMs` without any user input while `enabled`. */
export function useIdleLogout(enabled: boolean, timeoutMs: number, onIdle: () => void): void {
  const cb = useRef(onIdle);
  cb.current = onIdle;

  useEffect(() => {
    if (!enabled) return;
    let timer = setTimeout(() => cb.current(), timeoutMs);
    const reset = () => {
      clearTimeout(timer);
      timer = setTimeout(() => cb.current(), timeoutMs);
    };
    for (const ev of ACTIVITY_EVENTS) window.addEventListener(ev, reset, { passive: true });
    return () => {
      clearTimeout(timer);
      for (const ev of ACTIVITY_EVENTS) window.removeEventListener(ev, reset);
    };
  }, [enabled, timeoutMs]);
}
