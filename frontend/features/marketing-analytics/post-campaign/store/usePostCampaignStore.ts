"use client";

import { create } from "zustand";
import type {
  PostCampaignAnalyzeResultDto,
  PostCampaignImportMode,
  PostCampaignImportResultDto,
  PostCampaignSegmentListItemDto,
} from "../types";

/**
 * UI/session state for the import draft + which segment's report is on screen (CLAUDE.md:
 * "Zustand — тільки UI state"). Mirrors `audience-builder/store/useAudienceBuilderStore.ts`'s
 * draft-vs-committed shape (`isBuilt`), per this task's brief — the same mechanism here tracks TWO
 * distinct segment ids rather than one boolean, because the source doc's most important UX finding
 * (`docs/uployal/AUDIENCE_ANALYSIS.md` §7, "Чернетка та застосований сегмент") requires it:
 *
 * - `draftSegmentId` — the most recently IMPORTED segment (a new `PostCampaignSegment` row is
 *   created by every import call — there is no "update the same draft" endpoint). Importing again
 *   never touches `reportSegmentId`.
 * - `reportSegmentId` — the segment whose report tabs are actually fetched/displayed. Only ever
 *   changes when "Аналізувати сегмент"/"Оновити" succeeds, or when the user explicitly reopens an
 *   already-analyzed segment from the segments list. The gap between the two IS the "Є
 *   незастосовані зміни аудиторії" banner (`DraftAnalyzedBanner.tsx`).
 * - `reportVersion` — an opaque string (this feature uses `analyzedAt`) that changes every time
 *   `reportSegmentId`'s ANALYZED WINDOW changes, even when `reportSegmentId` itself stays the same
 *   (re-analyzing the same segment with new dates, source doc §10.3/§10.4's "Оновити"). The report
 *   GET endpoints take no date params at all — they just read whatever window is currently frozen
 *   on the segment row — so without a version bump in the React Query key, changing dates on an
 *   already-analyzed segment would never trigger a refetch (see `hooks/usePostCampaign.ts`'s own
 *   doc comment on every report query).
 *
 * Deliberately NOT persisted (no zustand/middleware `persist`) — same "fresh page load starts
 * clean" convention audience-builder's own store documents; segments themselves persist server-side
 * (`GET segments`), so nothing valuable is lost on a reload.
 */

interface PostCampaignState {
  // ── Import draft (pre-submit) ────────────────────────────────────────────────────────────
  inputMode: PostCampaignImportMode;
  rawText: string;
  file: File | null;
  /** Explicit column override for the CURRENTLY selected file (source doc §6.3's "preview, then
   * confirm/override" flow) — reset to null whenever a new file is chosen. */
  columnIndex: number | null;

  /** Last import response — drives the validation summary + (for tabular files) the column
   * preview picker. Cleared by `clearAll`. */
  importResult: PostCampaignImportResultDto | null;

  // ── Segment tracking (source doc §7) ─────────────────────────────────────────────────────
  draftSegmentId: string | null;
  reportSegmentId: string | null;
  reportVersion: string | null;

  // ── After-period — survives `clearAll` (source doc §9: "Очистити" keeps the selected period) ──
  afterStart: string; // yyyy-MM-dd
  afterEnd: string;

  setInputMode: (m: PostCampaignImportMode) => void;
  setRawText: (v: string) => void;
  setFile: (f: File | null) => void;
  setColumnIndex: (i: number | null) => void;
  setAfterRange: (start: string, end: string) => void;

  applyImportResult: (result: PostCampaignImportResultDto) => void;
  applyAnalyzeResult: (result: PostCampaignAnalyzeResultDto) => void;
  /** Reopening a segment from `SegmentsList` (brief item 9). A not-yet-analyzed segment only
   * becomes the draft (report stays whatever it was, per the same §7 rule) — only an already-
   * analyzed one also becomes the displayed report. */
  selectExistingSegment: (item: PostCampaignSegmentListItemDto) => void;

  /** "Очистити" (source doc §9) — clears draft AND applied segment, but explicitly preserves
   * `inputMode` and the after-period. */
  clearAll: () => void;
}

function defaultAfterEnd(): string {
  return new Date().toISOString().slice(0, 10);
}
function defaultAfterStart(): string {
  const d = new Date();
  d.setDate(d.getDate() - 29); // 30 calendar days inclusive, matching the source doc's own default control example
  return d.toISOString().slice(0, 10);
}

const DRAFT_RESET = {
  rawText: "",
  file: null as File | null,
  columnIndex: null as number | null,
  importResult: null as PostCampaignImportResultDto | null,
  draftSegmentId: null as string | null,
  reportSegmentId: null as string | null,
  reportVersion: null as string | null,
};

export const usePostCampaignStore = create<PostCampaignState>()((set) => ({
  inputMode: "list",
  afterStart: defaultAfterStart(),
  afterEnd: defaultAfterEnd(),
  ...DRAFT_RESET,

  setInputMode: (m) => set({ inputMode: m }),
  setRawText: (v) => set({ rawText: v }),
  setFile: (f) => set({ file: f, columnIndex: null }),
  setColumnIndex: (i) => set({ columnIndex: i }),
  setAfterRange: (afterStart, afterEnd) => set({ afterStart, afterEnd }),

  applyImportResult: (result) =>
    set({
      importResult: result,
      draftSegmentId: result.segmentId,
      // reportSegmentId/reportVersion deliberately untouched — the old report (if any) stays on
      // screen until the user explicitly re-analyzes (source doc §7).
    }),

  applyAnalyzeResult: (result) =>
    set({
      reportSegmentId: result.segmentId,
      reportVersion: result.analyzedAt,
      afterStart: result.afterStart,
      afterEnd: result.afterEnd,
    }),

  selectExistingSegment: (item) =>
    set({
      draftSegmentId: item.id,
      importResult: null,
      file: null,
      columnIndex: null,
      ...(item.isAnalyzed
        ? {
            reportSegmentId: item.id,
            reportVersion: item.analyzedAt,
            afterStart: item.afterStart ?? defaultAfterStart(),
            afterEnd: item.afterEnd ?? defaultAfterEnd(),
          }
        : { reportSegmentId: null, reportVersion: null }),
    }),

  clearAll: () => set({ ...DRAFT_RESET }),
}));
