// Public lead submission — plain fetch on purpose: lib/api attaches
// Authorization headers, and /api/public/leads is an anonymous endpoint.

import { API_BASE } from "@/lib/api";

export interface LeadPayload {
  name: string;
  phone: string;
  company: string;
  message: string;
  /** Honeypot — real users never fill it. */
  website: string;
}

export type LeadResult = { ok: true } | { ok: false; error: string };

export async function submitLead(payload: LeadPayload): Promise<LeadResult> {
  try {
    const res = await fetch(`${API_BASE}/api/public/leads`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (res.status === 204) return { ok: true };
    if (res.status === 429) {
      return { ok: false, error: "Забагато запитів, спробуйте за хвилину." };
    }
    if (res.status === 400) {
      const body = (await res.json().catch(() => null)) as { error?: string } | null;
      return { ok: false, error: body?.error ?? "Перевірте правильність даних." };
    }
    return { ok: false, error: "Не вдалося надіслати заявку. Спробуйте пізніше." };
  } catch {
    return { ok: false, error: "Немає з'єднання із сервером. Спробуйте пізніше." };
  }
}
