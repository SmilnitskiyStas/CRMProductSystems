"use client";

import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import {
  fetchMobileConfigVersions,
  publishMobileConfigDraft,
  rollbackMobileConfigVersion,
} from "../api/mobileConfigVersions";
import { MOBILE_CONFIG_DRAFT_KEY } from "./useMobileConfigDraft";
import { MOBILE_THEME_KEY } from "./useMobileTheme";

export const MOBILE_CONFIG_VERSIONS_KEY = ["mobile-config-versions"] as const;

/** GET /api/v1/mobile/config/versions — the tenant's full version history, newest-first. */
export function useMobileConfigVersions(enabled = true) {
  return useQuery({
    queryKey: MOBILE_CONFIG_VERSIONS_KEY,
    queryFn: fetchMobileConfigVersions,
    enabled,
    staleTime: 15_000,
  });
}

/**
 * A successful publish or rollback (`MobileConfigPublishService.PublishVersionAsync`, shared by
 * both) always both (a) supersedes the previous published version and archives it, and (b) clones
 * a brand-new draft row forward with `DraftVersionId` repointed at it — so besides the version
 * list itself, the draft and theme caches are stale too the instant either call succeeds. Without
 * this, AppBuilderCanvas/ThemeEditorSection/NavigationBuilderSection opened right after would keep
 * showing pre-publish/pre-rollback content until an unrelated refetch happened to occur (theme in
 * particular: rollback restores the historical version's theme onto the live `MobileTheme` row).
 */
function invalidatePublishSideEffects(qc: QueryClient) {
  qc.invalidateQueries({ queryKey: MOBILE_CONFIG_VERSIONS_KEY });
  qc.invalidateQueries({ queryKey: MOBILE_CONFIG_DRAFT_KEY });
  qc.invalidateQueries({ queryKey: MOBILE_THEME_KEY });
}

/** POST /api/v1/mobile/config/publish — publishes the tenant's current draft. */
export function usePublishMobileConfigDraft() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: publishMobileConfigDraft,
    onSuccess: () => invalidatePublishSideEffects(qc),
  });
}

/** POST /api/v1/mobile/config/versions/{versionId}/rollback. */
export function useRollbackMobileConfigVersion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (versionId: string) => rollbackMobileConfigVersion(versionId),
    onSuccess: () => invalidatePublishSideEffects(qc),
  });
}
