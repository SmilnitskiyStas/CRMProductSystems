import type { DemandEvent } from "./types";

/** Recurring events match by month/day each year, incl. windows wrapping over New Year. */
export function isEventActiveOnDate(ev: DemandEvent, dateIso: string): boolean {
  if (!ev.isRecurring) return dateIso >= ev.startsAt && dateIso <= ev.endsAt;

  const md = dateIso.slice(5); // "MM-dd"
  const s = ev.startsAt.slice(5);
  const e = ev.endsAt.slice(5);
  return s <= e ? md >= s && md <= e : md >= s || md <= e;
}

/**
 * Projects ev.startsAt/endsAt onto the year of referenceDateIso when ev.isRecurring
 * (mirrors isEventActiveOnDate's MM-dd wraparound), returns stored dates verbatim otherwise.
 *
 * For a wrapping window (e.g. Dec 20 – Jan 5) there are two candidate placements around
 * referenceDateIso's year: the window that starts in that year and ends in the next one, or
 * the window that started the previous year and ends in that year. Which one actually
 * contains referenceDateIso depends on which side of the wrap the reference date falls on.
 */
export function resolveEventWindowForYear(ev: DemandEvent, referenceDateIso: string): { from: string; to: string } {
  if (!ev.isRecurring) return { from: ev.startsAt, to: ev.endsAt };

  const year = Number(referenceDateIso.slice(0, 4));
  const startMd = ev.startsAt.slice(5);
  const endMd = ev.endsAt.slice(5);

  if (startMd <= endMd) {
    // Non-wrapping window (e.g. Jun 1 – Jun 10) always falls entirely within `year`.
    return { from: `${year}-${startMd}`, to: `${year}-${endMd}` };
  }

  // Wraps over New Year. referenceDateIso sits on either the Dec-side (>= startMd) or the
  // Jan-side (<= endMd) of the window — pick the placement that contains it.
  const refMd = referenceDateIso.slice(5);
  if (refMd >= startMd) {
    return { from: `${year}-${startMd}`, to: `${year + 1}-${endMd}` };
  }
  return { from: `${year - 1}-${startMd}`, to: `${year}-${endMd}` };
}
