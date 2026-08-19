import { api, ApiError } from "@/lib/api";
import type {
  MobileThemeDto,
  MobileThemeValidationError,
  UpdateMobileThemeRequest,
} from "../types";

const THEME_PATH = "/api/v1/mobile/theme";

/** GET /api/v1/mobile/theme — the tenant's current (or proposed-default) theme. */
export async function fetchMobileTheme(): Promise<MobileThemeDto> {
  return api.get<MobileThemeDto>(THEME_PATH);
}

/** PUT /api/v1/mobile/theme — full replace of every whitelisted field. */
export async function updateMobileTheme(body: UpdateMobileThemeRequest): Promise<MobileThemeDto> {
  return api.put<MobileThemeDto>(THEME_PATH, body);
}

/**
 * MobileThemeController's 400 body is `{ errors: [{ field, message }] }`
 * (`MobileConfigValidationError[]`) — a different shape than the rest of this codebase's flat
 * `{ error: string }` convention, which `lib/api.ts`'s `apiFetch` already flattens into
 * `ApiError.message` (losing the per-field structure). `ApiError.body` carries the raw parsed
 * JSON specifically so callers like this one can recover it. Returns `null` for anything that
 * isn't this shape (network errors, other 4xx/5xx, endpoints using the flat convention) so the
 * caller can fall back to a generic error message.
 */
export function extractThemeValidationErrors(err: unknown): MobileThemeValidationError[] | null {
  if (!(err instanceof ApiError) || err.status !== 400) return null;

  const body = err.body as { errors?: unknown } | undefined;
  if (!Array.isArray(body?.errors)) return null;

  const errors = body.errors.filter(
    (e): e is MobileThemeValidationError =>
      !!e &&
      typeof e === "object" &&
      typeof (e as Record<string, unknown>).field === "string" &&
      typeof (e as Record<string, unknown>).message === "string",
  );
  return errors.length > 0 ? errors : null;
}
