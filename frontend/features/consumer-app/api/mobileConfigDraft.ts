import { api, ApiError } from "@/lib/api";
import type {
  MobileConfigDraftResponse,
  MobileConfigDraftValidationError,
  SaveMobileConfigDraftRequest,
} from "../types";

const DRAFT_PATH = "/api/v1/mobile/config/draft";

/** GET /api/v1/mobile/config/draft — the tenant's current draft, or `hasDraft: false`. */
export async function fetchMobileConfigDraft(): Promise<MobileConfigDraftResponse> {
  return api.get<MobileConfigDraftResponse>(DRAFT_PATH);
}

/** PUT /api/v1/mobile/config/draft — whole-document replace (read-modify-write is the caller's job). */
export async function saveMobileConfigDraft(
  body: SaveMobileConfigDraftRequest,
): Promise<MobileConfigDraftResponse> {
  return api.put<MobileConfigDraftResponse>(DRAFT_PATH, body);
}

/**
 * `MobileConfigDraftController`'s 400 body is `{ errors: [{ field, message }] }`, the same
 * structured shape `MobileThemeController` established — mirrors
 * `extractThemeValidationErrors` in `../api/mobileTheme.ts` verbatim, just against this
 * endpoint's error type. Returns `null` for anything that isn't this shape so the caller falls
 * back to a generic error message.
 */
export function extractDraftValidationErrors(err: unknown): MobileConfigDraftValidationError[] | null {
  if (!(err instanceof ApiError) || err.status !== 400) return null;

  const body = err.body as { errors?: unknown } | undefined;
  if (!Array.isArray(body?.errors)) return null;

  const errors = body.errors.filter(
    (e): e is MobileConfigDraftValidationError =>
      !!e &&
      typeof e === "object" &&
      typeof (e as Record<string, unknown>).field === "string" &&
      typeof (e as Record<string, unknown>).message === "string",
  );
  return errors.length > 0 ? errors : null;
}
