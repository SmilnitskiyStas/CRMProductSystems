/**
 * Detects USB barcode scanners (keyboard wedge): a scanner types the whole code as a burst of
 * keystrokes a few ms apart and ends with Enter, while a human is far slower. Feed every keydown;
 * a scan is reported on Enter when the buffered burst is long enough and fast enough.
 */
export interface ScanOptions {
  /** Max gap between two keystrokes to count as the same burst (ms). */
  maxGapMs: number;
  /** Min characters for a burst to be a scan. */
  minLength: number;
}

export const DEFAULT_SCAN_OPTIONS: ScanOptions = { maxGapMs: 60, minLength: 4 };

export type ScanEvent =
  | { type: 'scan'; code: string }
  | { type: 'burst' } // a fast burst is in progress (>= 3 chars): caller may swallow keystrokes
  | { type: 'none' };

export class ScanDetector {
  private buffer = '';
  private lastAt = 0;
  private fastChars = 0;

  constructor(private readonly opts: ScanOptions = DEFAULT_SCAN_OPTIONS) {}

  /** @param key KeyboardEvent.key  @param at monotonic ms timestamp (event.timeStamp) */
  feed(key: string, at: number): ScanEvent {
    if (key === 'Enter') {
      const code = this.buffer;
      // every char after the first must have arrived fast, and Enter must follow fast too
      const fast = this.fastChars >= code.length - 1;
      const valid = code.length >= this.opts.minLength && fast && at - this.lastAt <= this.opts.maxGapMs;
      this.reset();
      return valid ? { type: 'scan', code } : { type: 'none' };
    }
    if (key.length !== 1) return { type: 'none' }; // Shift, F-keys, arrows...: ignore, keep state

    if (this.buffer && at - this.lastAt > this.opts.maxGapMs) {
      this.reset(); // too slow: a human typing, start over
    }
    if (this.buffer) this.fastChars++;
    this.buffer += key;
    this.lastAt = at;
    return this.fastChars >= 3 ? { type: 'burst' } : { type: 'none' };
  }

  /** Characters buffered so far (used to strip leaked keystrokes from a focused input). */
  get pending(): string {
    return this.buffer;
  }

  reset(): void {
    this.buffer = '';
    this.lastAt = 0;
    this.fastChars = 0;
  }
}
