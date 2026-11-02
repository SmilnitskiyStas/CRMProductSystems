import { requestMfaCode } from "@/features/auth/mfaPrompt";
// Shared API client. Adds Authorization header, handles 401 → refresh → retry.

export const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    /**
     * Raw parsed error response body, when the server returned one (TASK-537). Most endpoints
     * only ever send `{ error: string }`, already folded into `message` above — this exists for
     * the few endpoints (e.g. `MobileThemeController`'s `{ errors: [{ field, message }] }`) that
     * return a structured, per-field validation shape a caller needs to inspect instead of the
     * flattened message. `undefined` when the body was empty/unparsable or the request succeeded.
     */
    public readonly body?: unknown,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/**
 * True when the backend reports that an OPTIONAL dependency (AI provider, weather, geocoder) is
 * down or its circuit breaker is open: 503 `{ code: "service_unavailable" }`. Callers use it to
 * degrade gracefully (hide the block, show a soft notice) instead of a hard error.
 */
export function isServiceUnavailable(err: unknown): boolean {
  return (
    err instanceof ApiError &&
    err.status === 503 &&
    (err.body as { code?: string } | undefined)?.code === "service_unavailable"
  );
}

// Token is kept in memory; localStorage is used only to survive page reloads.
const TOKEN_KEY = "sg_token";
let _token: string | null = null;

// Set when the user logs out on purpose. In-flight requests (polling widgets,
// notification badge) that 401 during logout must NOT trigger the
// "session expired" redirect — the logout flow does its own clean redirect.
let _loggedOut = false;
let _sessionEpoch = 0;
export function getSessionEpoch(): number { return _sessionEpoch; }

/** Call BEFORE authApi.logout()/clearToken() so racing 401s stay silent. */
export function markLoggedOut(): void {
  _sessionEpoch++;
  _loggedOut = true;
}

export function getToken(): string | null {
  if (_token) return _token;
  if (typeof window === "undefined") return null;
  _token = localStorage.getItem(TOKEN_KEY);
  return _token;
}

export function setToken(token: string): void {
  _sessionEpoch++;
  storeToken(token);
}

function storeToken(token: string): void {
  _token = token;
  _loggedOut = false; // a fresh login/refresh clears the manual-logout flag
  if (typeof window !== "undefined") {
    localStorage.setItem(TOKEN_KEY, token);
    document.cookie = "sg_session=1; path=/; SameSite=Lax";
  }
}

export function clearToken(): void {
  _sessionEpoch++;
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
  responseType: "json" | "blob" = "json",
  mfaRetried = false,
): Promise<T> {
  const requestEpoch = _sessionEpoch;
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

  // Anonymous auth endpoints: a 401 here means bad credentials / bad 2FA code,
  // not an expired session — never trigger refresh or the login redirect.
  const isAnonAuthPath = ["/api/auth/login", "/api/auth/2fa/verify", "/api/auth/mfa-enrollment/setup", "/api/auth/mfa-enrollment/enable", "/api/auth/2fa/step-up", "/api/auth/forgot-password", "/api/auth/reset-password"].includes(path);

  // 401 on a protected request — try refreshing once
  if (res.status === 401 && !isRetry && !isAnonAuthPath) {
    if (requestEpoch !== _sessionEpoch) throw new ApiError(401, "Session changed.");
    // Manual logout in progress: refresh cookie is already revoked, so don't
    // attempt refresh or redirect — useLogout handles the clean navigation.
    if (_loggedOut) throw new ApiError(401, "Logged out.");
    const refreshed = await tryRefresh();
    if (requestEpoch !== _sessionEpoch) throw new ApiError(401, "Session changed.");
    if (refreshed === "ok") return apiFetch<T>(path, options, true, responseType, mfaRetried);
    if (_loggedOut) throw new ApiError(401, "Logged out.");
    // Transient refresh failure (network/5xx): keep the session, surface the error.
    if (refreshed === "error") throw new ApiError(401, "Session refresh failed, try again.");
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
    const body = await res.json().catch(() => ({})) as { error?: string; code?: string };
    if (res.status === 403 && body.code === "mfa_step_up_required" && !mfaRetried && token) {
      if (requestEpoch !== _sessionEpoch) throw new ApiError(401, "Session changed.");
      await confirmRecentMfa();
      if (requestEpoch !== _sessionEpoch) throw new ApiError(401, "Session changed.");
      return apiFetch<T>(path, options, isRetry, responseType, true);
    }
    throw new ApiError(res.status, body.error ?? `HTTP ${res.status}`, body);
  }

  if (res.status === 204) return undefined as T;
  if (responseType === "blob") return await res.blob() as T;
  return res.json() as Promise<T>;
}

let mfaInFlight: Promise<void> | null = null;
function confirmRecentMfa(): Promise<void> {
  if (!mfaInFlight) {
    const epoch = _sessionEpoch;
    mfaInFlight = (async () => {
      const code = await requestMfaCode();
      if (!code) throw new ApiError(403, "MFA confirmation cancelled.");
      if (epoch !== _sessionEpoch) throw new ApiError(401, "Session changed.");
      const result = await apiFetch<{ accessToken: string }>("/api/auth/2fa/step-up", { method: "POST", body: JSON.stringify({ code }) });
      if (epoch !== _sessionEpoch) throw new ApiError(401, "Session changed.");
      storeToken(result.accessToken);
    })().finally(() => { mfaInFlight = null; });
  }
  return mfaInFlight;
}

type RefreshResult = "ok" | "denied" | "error";

// Single-flight: the refresh token ROTATES and a replayed (already-revoked) token makes the
// backend revoke the whole session family. When an access token expires, many parallel requests
// 401 at once — each firing its own /refresh with the same cookie would log the user out.
let _refreshInFlight: Promise<RefreshResult> | null = null;

function tryRefresh(): Promise<RefreshResult> {
  if (!_refreshInFlight) {
    _refreshInFlight = withCrossTabLock(() => doRefresh()).finally(() => {
      _refreshInFlight = null;
    });
  }
  return _refreshInFlight;
}

// Serialises refreshes across tabs (they share one refresh cookie). A tab that waited on the
// lock re-uses the token another tab already refreshed instead of rotating again.
async function withCrossTabLock(fn: () => Promise<RefreshResult>): Promise<RefreshResult> {
  const before = _token;
  const run = () => {
    const stored = typeof window !== "undefined" ? localStorage.getItem(TOKEN_KEY) : null;
    if (stored && stored !== before) {
      _token = stored; // another tab refreshed while we waited
      return Promise.resolve<RefreshResult>("ok");
    }
    return fn();
  };
  if (typeof navigator !== "undefined" && navigator.locks) {
    return await navigator.locks.request("sg_refresh", run);
  }
  return run();
}

async function doRefresh(isRetry = false): Promise<RefreshResult> {
  const refreshEpoch = _sessionEpoch;
  try {
    const res = await fetch(`${API_BASE}/api/auth/refresh`, {
      method: "POST",
      credentials: "include",
    });
    if (res.status === 409) {
      if (!isRetry && refreshEpoch === _sessionEpoch) {
        await new Promise((r) => setTimeout(r, 400));
        if (refreshEpoch !== _sessionEpoch) return "error";
        return doRefresh(true);
      }
      return "error";
    }
    if (res.status === 401 || res.status === 403) {
      // A concurrent refresh (other tab/device race) may have just rotated the cookie —
      // retry once with the fresh cookie before concluding the session is really gone.
      if (!isRetry) {
        await new Promise((r) => setTimeout(r, 400));
        if (refreshEpoch !== _sessionEpoch) return "error";
        return doRefresh(true);
      }
      return "denied";
    }
    if (!res.ok) return "error"; // 5xx / 429 — transient, do not drop the session
    const data = (await res.json()) as { accessToken: string };
    if (refreshEpoch !== _sessionEpoch || _loggedOut) return "error";
    storeToken(data.accessToken);
    return "ok";
  } catch {
    return "error"; // network blip
  }
}

export const api = {
  getBlob: (path: string, opts?: RequestInit) => apiFetch<Blob>(path, { ...opts, method: "GET" }, false, "blob"),
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
