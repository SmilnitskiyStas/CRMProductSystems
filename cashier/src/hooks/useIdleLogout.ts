import { useEffect, useRef } from 'react';

const ACTIVITY_EVENTS = ['keydown', 'mousedown', 'mousemove', 'touchstart', 'wheel'] as const;

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
