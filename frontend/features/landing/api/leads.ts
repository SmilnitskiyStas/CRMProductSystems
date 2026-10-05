// Public lead submission: plain fetch on purpose: lib/api attaches
// Authorization headers, and /api/public/leads is an anonymous endpoint.

import { API_BASE } from "@/lib/api";
import { sourceFromPathname } from "../lib/analytics";

export interface LeadPayload {
  name: string;
  phone: string;
  company: string;
  message: string;
  /** Honeypot: real users never fill it. */
  website: string;
}

/** Everything sent to the API: form fields + attribution (no IP, collected client-side only). */
export interface LeadBody extends LeadPayload {
  source: string;
  pageUrl: string;
  locale: string;
  referrer: string;
  utmSource: string;
  utmMedium: string;
  utmCampaign: string;
}

export type LeadResult =
  | { ok: true }
  | { ok: false; error: string; /** true for network / 5xx failures: the payload was kept for a retry. */ retryable: boolean };

const PENDING_KEY = "sg_pending_lead";

function buildLeadBody(payload: LeadPayload): LeadBody {
  const url = new URL(window.location.href);
  const first = url.pathname.split("/").filter(Boolean)[0];
  const locale = first === "en" ? "en" : "uk";
  const utm = (key: string) => (url.searchParams.get(key) ?? "").slice(0, 150);
  return {
    ...payload,
    source: sourceFromPathname(url.pathname),
    pageUrl: url.pathname.slice(0, 300),
    locale,
    referrer: (document.referrer ?? "").slice(0, 300),
    utmSource: utm("utm_source"),
    utmMedium: utm("utm_medium"),
    utmCampaign: utm("utm_campaign"),
  };
}

async function postLead(body: LeadBody): Promise<LeadResult> {
  try {
    const res = await fetch(`${API_BASE}/api/public/leads`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (res.status === 204) return { ok: true };
    if (res.status === 429) {
      return { ok: false, retryable: false, error: "Забагато запитів, спробуйте за хвилину." };
    }
    if (res.status === 400) {
      const data = (await res.json().catch(() => null)) as { error?: string } | null;
      return { ok: false, retryable: false, error: data?.error ?? "Перевірте правильність даних." };
    }
    return { ok: false, retryable: true, error: "Не вдалося надіслати заявку. Спробуйте ще раз." };
  } catch {
    return { ok: false, retryable: true, error: "Немає з'єднання із сервером. Спробуйте ще раз." };
  }
}

export function loadPendingLead(): LeadBody | null {
  try {
    const raw = window.localStorage.getItem(PENDING_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<LeadBody> | null;
    if (!parsed || typeof parsed.name !== "string" || typeof parsed.phone !== "string") return null;
    return parsed as LeadBody;
  } catch {
    return null;
  }
}

export function clearPendingLead(): void {
  try {
    window.localStorage.removeItem(PENDING_KEY);
  } catch {
    // storage unavailable: nothing to clear
  }
}

function savePendingLead(body: LeadBody): void {
  try {
    window.localStorage.setItem(PENDING_KEY, JSON.stringify(body));
  } catch {
    // storage unavailable: the form still holds the data for an in-page retry
  }
}

export interface SubmitLeadOutcome {
  result: LeadResult;
  /** Lead source (page section) used for analytics. */
  source: string;
}

/** Sends a new lead. On a retryable failure the payload is also parked in localStorage. */
export async function submitLead(payload: LeadPayload): Promise<SubmitLeadOutcome> {
  const body = buildLeadBody(payload);
  const result = await postLead(body);
  if (result.ok) clearPendingLead();
  else if (result.retryable) savePendingLead(body);
  return { result, source: body.source };
}

/** Re-sends the lead saved after an earlier failure. Clears it on success. */
export async function resendPendingLead(): Promise<SubmitLeadOutcome | null> {
  const body = loadPendingLead();
  if (!body) return null;
  const result = await postLead(body);
  if (result.ok) clearPendingLead();
  return { result, source: body.source ?? "landing" };
}
