/**
 * Pure logic for the webcam barcode scanner: check-digit validation and the read gate
 * (consecutive-read confirmation + sliding cooldown). No DOM, so it is unit-testable.
 */

/** GS1 check digit: digits weighted 3,1,3,1... from the right of the payload (EAN-13 / EAN-8 / UPC-A). */
export function gs1CheckDigitValid(code: string): boolean {
  if (!/^\d+$/.test(code) || ![8, 12, 13].includes(code.length)) return false;
  const digits = [...code].map(Number);
  const check = digits.pop()!;
  let sum = 0;
  for (let i = 0; i < digits.length; i++) {
    const fromRight = digits.length - i; // 1-based position from the right of the payload
    sum += digits[i] * (fromRight % 2 === 1 ? 3 : 1);
  }
  return (10 - (sum % 10)) % 10 === check;
}

/**
 * Whether a decoded string is plausible enough to feed the cart. Numeric retail codes (8/12/13
 * digits) must pass their check digit - this is what filters camera misreads. Anything else
 * (Code128 internal codes) must be 4..40 printable ASCII characters.
 */
export function isAcceptableCode(code: string): boolean {
  if (/^\d+$/.test(code) && [8, 12, 13].includes(code.length)) return gs1CheckDigitValid(code);
  if (/^\d+$/.test(code) && code.length >= 8) return false; // looks like EAN/UPC of a wrong length: misread
  return /^[\x21-\x7e]{4,40}$/.test(code);
}

export interface ScanGateOptions {
  /** Identical consecutive reads needed before a code is accepted. */
  confirmReads: number;
  /** Max gap between two reads to count as consecutive (ms). */
  confirmWindowMs: number;
  /** Same code is ignored this long after it was accepted; the timer slides while it keeps being read. */
  cooldownMs: number;
}

export const DEFAULT_GATE_OPTIONS: ScanGateOptions = { confirmReads: 2, confirmWindowMs: 1200, cooldownMs: 1500 };

export class ScanGate {
  private candidate: string | null = null;
  private count = 0;
  private lastReadAt = 0;
  private accepted: { code: string; at: number } | null = null;

  constructor(private readonly opts: ScanGateOptions = DEFAULT_GATE_OPTIONS) {}

  /** Feed one decoder result (call only when something was decoded). Returns the code once, when accepted. */
  feed(code: string, now: number): string | null {
    if (!isAcceptableCode(code)) return null;

    // Cooldown: the product is still in front of the camera - keep the timer alive, add nothing.
    if (this.accepted && this.accepted.code === code && now - this.accepted.at < this.opts.cooldownMs) {
      this.accepted.at = now;
      return null;
    }

    if (code === this.candidate && now - this.lastReadAt <= this.opts.confirmWindowMs) this.count++;
    else {
      this.candidate = code;
      this.count = 1;
    }
    this.lastReadAt = now;

    if (this.count >= this.opts.confirmReads) {
      this.accepted = { code, at: now };
      this.candidate = null;
      this.count = 0;
      return code;
    }
    return null;
  }

  reset(): void {
    this.candidate = null;
    this.count = 0;
    this.accepted = null;
  }
}
