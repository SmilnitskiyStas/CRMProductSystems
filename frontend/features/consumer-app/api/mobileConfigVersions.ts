import { api } from "@/lib/api";
import type { MobileConfigPublishedResult, MobileConfigVersionSummary } from "../types";

const VERSIONS_PATH = "/api/v1/mobile/config/versions";
const PUBLISH_PATH = "/api/v1/mobile/config/publish";

/** GET /api/v1/mobile/config/versions — the tenant's full version history, newest-first. */
export async function fetchMobileConfigVersions(): Promise<MobileConfigVersionSummary[]> {
  return api.get<MobileConfigVersionSummary[]>(VERSIONS_PATH);
}

/**
 * POST /api/v1/mobile/config/versions/{versionId}/rollback — publishes a new version cloned from
 * the historical one, including its theme. 400 (validation/CannotRollbackToCurrentVersion), 404
 * (unknown version), and 409 (concurrent publish) are surfaced to the caller as a thrown `ApiError`
 * — see `extractDraftValidationErrors` in `./mobileConfigDraft.ts` for parsing the 400 case's
 * structured `{ errors: [...] }` body.
 */
export async function rollbackMobileConfigVersion(versionId: string): Promise<MobileConfigPublishedResult> {
  return api.post<MobileConfigPublishedResult>(`${VERSIONS_PATH}/${versionId}/rollback`);
}

/**
 * POST /api/v1/mobile/config/publish — publishes the tenant's current draft. 400 (no draft to
 * publish / validation failure) and 409 (concurrent publish) are surfaced as a thrown `ApiError` —
 * same structured-error shape as rollback above.
 */
export async function publishMobileConfigDraft(): Promise<MobileConfigPublishedResult> {
  return api.post<MobileConfigPublishedResult>(PUBLISH_PATH);
}
