import { useState } from "react";
import type { AddShiftPayload } from "../types";

export interface BulkFailure {
  item: AddShiftPayload;
  /** Raw server message (English) — only used as a fallback; see {@link parseOverlap}. */
  message: string;
}

export interface BulkResult {
  ok: AddShiftPayload[];
  failed: BulkFailure[];
}

/**
 * Backend (ScheduleService.AddShiftAsync) rejects an overlapping shift with
 * "User already has a shift on yyyy-MM-dd from HH:mm:ss to HH:mm:ss that overlaps." — an
 * English-only string. Extract the conflicting window so the UI can render it localized.
 */
export function parseOverlap(message: string): { start: string; end: string } | null {
  const m = /from (\d{2}:\d{2})(?::\d{2})? to (\d{2}:\d{2})(?::\d{2})? that overlaps/.exec(message);
  return m ? { start: m[1], end: m[2] } : null;
}

/**
 * Creates one shift per (worker x day) item through the existing validated add-shift endpoint,
 * one request at a time, never aborting on a failure: the caller gets both the created and the
 * rejected items so it can report them compactly instead of a single opaque error.
 */
export function useBulkAddShifts(add: (item: AddShiftPayload) => Promise<unknown>, locationId: string) {
  const [pending, setPending] = useState(false);
  const [result, setResult] = useState<BulkResult | null>(null);

  async function run(items: AddShiftPayload[]): Promise<BulkResult> {
    setPending(true);
    const res: BulkResult = { ok: [], failed: [] };
    for (const item of items) {
      const payload = { ...item, locationId };
      try {
        await add(payload);
        res.ok.push(payload);
      } catch (err) {
        res.failed.push({ item: payload, message: (err as Error).message });
      }
    }
    setPending(false);
    setResult(res);
    return res;
  }

  return { run, pending, result, clear: () => setResult(null) };
}
