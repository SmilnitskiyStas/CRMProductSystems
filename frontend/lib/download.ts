// Authenticated file download/view (blob → <a download> or new tab).
// Used for contract PDFs (TASK-318) — lib/api.ts only handles JSON responses.

import { API_BASE, getToken, ApiError } from "./api";

function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

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
  saveBlob(blob, filename);
}

/**
 * POST variant of `downloadFile` (TASK-409, marketing-analytics exports) — the plan assumed
 * the existing GET-only `downloadFile()` would cover Excel exports, but the actual backend
 * contract (task log 406) has all 3 export endpoints as POST with a JSON body (segment/store
 * filters + unmaskPii don't fit in a URL). Shares the same blob→<a download> logic as
 * `downloadFile` above rather than duplicating it — this is the one new bit of file-handling
 * code, added here (the shared lib) rather than inside the feature.
 */
export async function downloadFilePost(path: string, body: unknown, filename: string): Promise<void> {
  const token = getToken();
  const res = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify(body),
  });

  if (!res.ok) {
    const errBody = (await res.json().catch(() => ({}))) as { error?: string };
    throw new ApiError(res.status, errBody.error ?? `HTTP ${res.status}`);
  }

  const blob = await res.blob();
  saveBlob(blob, filename);
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
