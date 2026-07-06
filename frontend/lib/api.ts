// Shared API client. Adds Authorization header, handles 401 → refresh → retry.

export const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

// Token is kept in memory; localStorage is used only to survive page reloads.
const TOKEN_KEY = "sg_token";
let _token: string | null = null;

// Set when the user logs out on purpose. In-flight requests (polling widgets,
// notification badge) that 401 during logout must NOT trigger the
// "session expired" redirect — the logout flow does its own clean redirect.
let _loggedOut = false;

/** Call BEFORE authApi.logout()/clearToken() so racing 401s stay silent. */
export function markLoggedOut(): void {
  _loggedOut = true;
}

export function getToken(): string | null {
  if (_token) return _token;
  if (typeof window === "undefined") return null;
  _token = localStorage.getItem(TOKEN_KEY);
  return _token;
}

export function setToken(token: string): void {
  _token = token;
  _loggedOut = false; // a fresh login/refresh clears the manual-logout flag
  if (typeof window !== "undefined") {
    localStorage.setItem(TOKEN_KEY, token);
    document.cookie = "sg_session=1; path=/; SameSite=Lax";
  }
}

export function clearToken(): void {
  _token = null;
  if (typeof window !== "undefined") {
    localStorage.removeItem(TOKEN_KEY);
    document.cookie = "sg_session=; path=/; max-age=0";
  }
}

// apiFetch — base request function. Handles JSON, empty 204, and 401 retry.
async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
  isRetry = false,
): Promise<T> {
  const token = getToken();

  // FormData bodies set their own multipart Content-Type (with boundary) —
  // forcing application/json would break uploads.
  const isFormData = options.body instanceof FormData;

  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    credentials: "include", // sends HttpOnly refreshToken cookie
    headers: {
      ...(isFormData ? {} : { "Content-Type": "application/json" }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
  });

  // 401 on a protected request — try refreshing once
  if (res.status === 401 && !isRetry && path !== "/api/auth/login") {
    // Manual logout in progress: refresh cookie is already revoked, so don't
    // attempt refresh or redirect — useLogout handles the clean navigation.
    if (_loggedOut) throw new ApiError(401, "Logged out.");
    const refreshed = await tryRefresh();
    if (refreshed) return apiFetch<T>(path, options, true);
    if (_loggedOut) throw new ApiError(401, "Logged out.");
    clearToken();
    if (typeof window !== "undefined") {
      // Hard navigation on purpose — resets all in-memory state (React Query cache, Zustand).
      // Only show the "session expired" notice when the user actually HAD a
      // session that lapsed; with no token this is just "not signed in".
      window.location.href = token ? "/login?reason=session_expired" : "/login";
    }
    throw new ApiError(401, "Session expired.");
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({})) as { error?: string };
    throw new ApiError(res.status, body.error ?? `HTTP ${res.status}`);
  }

  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

async function tryRefresh(): Promise<boolean> {
  try {
    const res = await fetch(`${API_BASE}/api/auth/refresh`, {
      method: "POST",
      credentials: "include",
    });
    if (!res.ok) return false;
    const data = (await res.json()) as { accessToken: string };
    setToken(data.accessToken);
    return true;
  } catch {
    return false;
  }
}

export const api = {
  get:    <T>(path: string, opts?: RequestInit) => apiFetch<T>(path, { ...opts, method: "GET" }),
  post:   <T>(path: string, body?: unknown, opts?: RequestInit) =>
    apiFetch<T>(path, { ...opts, method: "POST", body: body != null ? JSON.stringify(body) : undefined }),
  // Multipart upload: apiFetch detects the FormData body and skips Content-Type
  // so the browser adds the multipart boundary itself.
  postForm: <T>(path: string, form: FormData) =>
    apiFetch<T>(path, { method: "POST", body: form }),
  put:    <T>(path: string, body?: unknown, opts?: RequestInit) =>
    apiFetch<T>(path, { ...opts, method: "PUT", body: body != null ? JSON.stringify(body) : undefined }),
  patch:  <T>(path: string, body?: unknown, opts?: RequestInit) =>
    apiFetch<T>(path, { ...opts, method: "PATCH", body: body != null ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string, opts?: RequestInit) => apiFetch<T>(path, { ...opts, method: "DELETE" }),
};
