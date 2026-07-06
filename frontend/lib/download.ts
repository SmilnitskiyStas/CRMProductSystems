// Authenticated file download (blob → <a download>). Used for contract PDFs
// (TASK-318) — lib/api.ts only handles JSON responses.

import { API_BASE, getToken, ApiError } from "./api";

export async function downloadFile(path: string, filename: string): Promise<void> {
  const token = getToken();
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: "include",
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!res.ok) {
    const body = (await res.json().catch(() => ({}))) as { error?: string };
    throw new ApiError(res.status, body.error ?? `HTTP ${res.status}`);
  }

  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
