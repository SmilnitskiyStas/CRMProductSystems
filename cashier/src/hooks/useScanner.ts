import { useEffect, useRef } from 'react';
import { ScanDetector } from '@shared/scanBuffer';

/** The first chars of a scan reach the focused text field before the burst is recognised: remove them. */
const LEAK_CHARS = 3;
function stripLeakedCode(code: string): void {
  const el = document.activeElement;
  if (!(el instanceof HTMLInputElement) || el.type === 'password') return;
  const leaked = code.slice(0, LEAK_CHARS);
  if (!el.value.endsWith(leaked)) return;
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  setter?.call(el, el.value.slice(0, -leaked.length));
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

/**
 * Focus-independent USB scanner capture (keyboard wedge). Listens on window in the capture phase,
 * so a scan works whichever element has focus; the scanner's Enter is swallowed so it cannot
 * submit a focused form, and characters that leaked into a text input are removed.
 */
export function useScanner(enabled: boolean, onScan: (code: string) => void): void {
  const cb = useRef(onScan);
  cb.current = onScan;

  useEffect(() => {
    if (!enabled) return;
    const detector = new ScanDetector();
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.altKey || e.metaKey) return;
      const ev = detector.feed(e.key, e.timeStamp);
      if (ev.type === 'burst') e.preventDefault(); // swallow the rest of the burst
      if (ev.type === 'scan') {
        e.preventDefault();
        e.stopPropagation();
        stripLeakedCode(ev.code);
        cb.current(ev.code);
      }
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [enabled]);
}
