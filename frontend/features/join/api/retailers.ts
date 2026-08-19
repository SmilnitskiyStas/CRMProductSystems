// Public retailer lookup for the QR/deep-link onboarding page (TASK-549). Plain
// fetch on purpose: lib/api attaches Authorization headers and drives the
// dashboard's 401 → refresh → redirect flow, neither of which applies here —
// GET /api/v1/retailers/{slug}/public is anonymous by design (see
// .claude/logs/tasks/549_2026-08-18_qr-onboarding-backend_backend-developer.md).

import { API_BASE } from "@/lib/api";

export interface RetailerPublicInfo {
  name: string;
  slug: string;
  logoUrl: string | null;
  joinable: boolean;
}

/**
 * Resolves a retailer slug for the public join page.
 *
 * Returns `null` for every failure case — unknown slug, inactive tenant,
 * missing loyalty module, and a paused program are all indistinguishable 404s
 * by backend design (enumeration-safety), and this page must not try to tell
 * them apart either. Network errors and non-OK responses collapse into the
 * same `null` result so the page always renders the one friendly "not
 * available" state instead of throwing during server-side rendering.
 */
export async function getRetailerPublicInfo(slug: string): Promise<RetailerPublicInfo | null> {
  try {
    const res = await fetch(`${API_BASE}/api/v1/retailers/${encodeURIComponent(slug)}/public`, {
      cache: "no-store",
    });
    if (!res.ok) return null;
    return (await res.json()) as RetailerPublicInfo;
  } catch {
    return null;
  }
}
