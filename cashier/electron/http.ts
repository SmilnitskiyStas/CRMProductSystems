import type { ApiError, ErrorKind, Result } from '@shared/types';

export interface HttpResponse<T> {
  status: number;
  body: T | null;
  /** True when no HTTP response was received at all (DNS, refused, timeout). */
  networkError: boolean;
  errorMessage?: string;
  transportError?: boolean;
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  headers?: Record<string, string>;
  body?: unknown;
  timeoutMs?: number;
  /** Connectivity probe: do not report its network errors back to the monitor. */
  probe?: boolean;
}

let networkErrorListener: (() => void) | null = null;
/** The connectivity monitor subscribes here: a failed API call triggers an immediate re-probe. */
export function setNetworkErrorListener(fn: (() => void) | null): void {
  networkErrorListener = fn;
}

const DEFAULT_TIMEOUT_MS = 15_000;
export const SERVER_URL_ERROR = 'Потрібна HTTPS-адреса сервера. HTTP дозволено лише для локальної розробки з явним дозволом.';

let allowLoopbackHttp = false;

/** Main process enables this only for an unpackaged, explicitly opted-in dev session. */
export function configureHttpTransport(options: { allowLoopbackHttp: boolean }): void {
  allowLoopbackHttp = options.allowLoopbackHttp;
}

function isAllowedServer(u: URL): boolean {
  if (u.username || u.password || u.search || u.hash) return false;
  if (u.protocol === 'https:') return true;
  return allowLoopbackHttp && u.protocol === 'http:' &&
    ['localhost', '127.0.0.1', '[::1]'].includes(u.hostname);
}

export function normalizeServerUrl(raw: string): string | null {
  const trimmed = raw.trim().replace(/\/+$/, '');
  try {
    const u = new URL(trimmed);
    if (!isAllowedServer(u)) return null;
    return u.origin + (u.pathname === '/' ? '' : u.pathname.replace(/\/+$/, ''));
  } catch {
    return null;
  }
}

export async function httpRequest<T>(
  baseUrl: string,
  path: string,
  opts: RequestOptions = {},
): Promise<HttpResponse<T>> {
  // Enforce at the transport too: saved configurations bypass the activation form.
  const normalizedBase = normalizeServerUrl(baseUrl);
  if (!normalizedBase || !path.startsWith('/') || path.startsWith('//') || path.includes('\\')) {
    return { status: 0, body: null, networkError: true, transportError: true, errorMessage: SERVER_URL_ERROR };
  }
  const target = new URL(normalizedBase + path);
  if (target.origin !== new URL(normalizedBase).origin) {
    return { status: 0, body: null, networkError: true, transportError: true, errorMessage: SERVER_URL_ERROR };
  }
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), opts.timeoutMs ?? DEFAULT_TIMEOUT_MS);
  try {
    const res = await fetch(target, {
      // Do not forward credentials or financial payloads to redirect destinations.
      redirect: 'error',
      method: opts.method ?? 'GET',
      headers: {
        Accept: 'application/json',
        ...(opts.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...opts.headers,
      },
      body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
      signal: ctrl.signal,
    });
    let body: T | null = null;
    const text = await res.text();
    if (text) {
      try {
        body = JSON.parse(text) as T;
      } catch {
        body = null;
      }
    }
    return { status: res.status, body, networkError: false };
  } catch (e) {
    if (!opts.probe) networkErrorListener?.();
    return { status: 0, body: null, networkError: true, errorMessage: (e as Error).message };
  } finally {
    clearTimeout(timer);
  }
}

export function kindForStatus(status: number): ErrorKind {
  if (status === 401) return 'unauthorized';
  if (status === 423) return 'locked';
  if (status === 409) return 'conflict';
  if (status >= 500) return 'server';
  return 'validation';
}

/** Server errors are `{ error: "..." }` (controllers) or ProblemDetails (`title`). */
export function toApiError<T>(res: HttpResponse<T>): ApiError {
  if (res.transportError) return { kind: 'validation', message: SERVER_URL_ERROR };
  if (res.networkError) {
    return { kind: 'network', message: 'Немає зв’язку з сервером. Перевірте мережу та адресу сервера.' };
  }
  const b = res.body as { error?: string; title?: string } | null;
  const serverMsg = b?.error ?? b?.title;
  const kind = kindForStatus(res.status);
  const fallback: Record<ErrorKind, string> = {
    network: '',
    unauthorized: 'Невірні дані або сесію завершено.',
    locked: 'Доступ тимчасово заблоковано.',
    conflict: 'Конфлікт стану. Оновіть дані та спробуйте знову.',
    validation: 'Запит відхилено сервером.',
    server: 'Помилка сервера. Спробуйте пізніше.',
  };
  return { kind, status: res.status, message: serverMsg ?? fallback[kind] };
}

export function toResult<T, U>(res: HttpResponse<T>, map: (body: T) => U): Result<U> {
  if (!res.networkError && res.status >= 200 && res.status < 300 && res.body !== null) {
    return { ok: true, data: map(res.body) };
  }
  if (!res.networkError && res.status >= 200 && res.status < 300) {
    return { ok: false, error: { kind: 'server', status: res.status, message: 'Порожня відповідь сервера.' } };
  }
  return { ok: false, error: toApiError(res) };
}
