// Authenticated file download/view (blob → <a download> or new tab).
// Used for contract PDFs (TASK-318) — lib/api.ts only handles JSON responses.

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

/** Fetch a file and open it in a new tab (e.g. contract PDFs) instead of
 * forcing a save-to-disk. Browsers render `application/pdf` blobs inline via
 * their native PDF viewer. */
export async function viewFile(path: string): Promise<void> {
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
  window.open(url, "_blank");
  // Deliberately not revoking immediately — the new tab needs the blob URL to
  // stay alive while it loads/renders the PDF. Let the browser GC it on tab close.
}
