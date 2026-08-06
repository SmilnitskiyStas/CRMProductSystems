import { api } from "@/lib/api";
import { downloadFilePost } from "@/lib/download";
import type {
  ExplainPostCampaignResultDto,
  ExportPostCampaignCustomersRequest,
  PostCampaignAnalyzeRequest,
  PostCampaignAnalyzeResultDto,
  PostCampaignCustomerTableDto,
  PostCampaignCustomerTableParams,
  PostCampaignDailyTurnoverDto,
  PostCampaignImportParams,
  PostCampaignImportResultDto,
  PostCampaignMigrationDto,
  PostCampaignRfmActivityDto,
  PostCampaignSegmentListItemDto,
  PostCampaignSummaryDto,
} from "../types";

const BASE = "/api/marketing-analytics/post-campaign";

/** Repeated param: ?storeIds=a&storeIds=b — omitted entirely (all stores) when empty/null. Same
 * helper shape as `price-segments/api/priceSegments.ts`'s `buildStoreQs`. */
function buildStoreQs(storeIds: string[] | null | undefined, extra?: Record<string, string | number | boolean | undefined>): string {
  const qs = new URLSearchParams();
  for (const id of storeIds ?? []) qs.append("storeIds", id);
  if (extra) {
    for (const [k, v] of Object.entries(extra)) {
      if (v !== undefined) qs.set(k, String(v));
    }
  }
  const s = qs.toString();
  return s ? `?${s}` : "";
}

function timestamp(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

export const postCampaignApi = {
  listSegments: () => api.get<PostCampaignSegmentListItemDto[]>(`${BASE}/segments`),

  /** Always multipart/form-data, even for raw-text mode (text travels as the `rawText` field) —
   * mirrors `PostCampaignController.Import`'s own doc comment on why: the SAME endpoint must also
   * accept a binary file. Exactly one of `rawText`/`file` is set by the caller. */
  importSegment: (params: PostCampaignImportParams) => {
    const form = new FormData();
    if (params.file) form.append("file", params.file);
    if (params.rawText !== undefined) form.append("rawText", params.rawText);
    if (params.name) form.append("name", params.name);
    if (params.columnIndex !== undefined) form.append("columnIndex", String(params.columnIndex));
    return api.postForm<PostCampaignImportResultDto>(`${BASE}/segments/import`, form);
  },

  analyzeSegment: (segmentId: string, body: PostCampaignAnalyzeRequest) =>
    api.post<PostCampaignAnalyzeResultDto>(`${BASE}/segments/${segmentId}/analyze`, body),

  getSummary: (segmentId: string, storeIds: string[]) =>
    api.get<PostCampaignSummaryDto>(`${BASE}/segments/${segmentId}/summary${buildStoreQs(storeIds)}`),

  getDailyTurnover: (segmentId: string, storeIds: string[]) =>
    api.get<PostCampaignDailyTurnoverDto>(`${BASE}/segments/${segmentId}/daily-turnover${buildStoreQs(storeIds)}`),

  getRfmActivity: (segmentId: string, storeIds: string[]) =>
    api.get<PostCampaignRfmActivityDto>(`${BASE}/segments/${segmentId}/rfm-activity${buildStoreQs(storeIds)}`),

  getCustomers: (segmentId: string, params: PostCampaignCustomerTableParams, storeIds: string[]) =>
    api.get<PostCampaignCustomerTableDto>(
      `${BASE}/segments/${segmentId}/customers${buildStoreQs(storeIds, {
        page: params.page,
        pageSize: params.pageSize,
        sortBy: params.sortBy,
        sortDescending: params.sortDescending,
      })}`,
    ),

  getMigration: (segmentId: string, storeIds: string[]) =>
    api.get<PostCampaignMigrationDto>(`${BASE}/segments/${segmentId}/migration${buildStoreQs(storeIds)}`),

  /** POST, no body — filters travel via query string exactly like the GETs (same pattern as
   * price-segments' own /explain endpoints). Only ever called on explicit button click. */
  explainSegment: (segmentId: string, storeIds: string[]) =>
    api.post<ExplainPostCampaignResultDto>(`${BASE}/segments/${segmentId}/explain${buildStoreQs(storeIds)}`),

  exportCustomers: (segmentId: string, body: ExportPostCampaignCustomersRequest) =>
    downloadFilePost(`${BASE}/segments/${segmentId}/exports/customers`, body, `post_campaign_customers_${timestamp()}.xlsx`),

  /** No body at all — the unknown-tokens report has no filters, it just dumps whatever was
   * captured at import time (see PostCampaignController.ExportUnknownTokens). `downloadFilePost`
   * always sends a JSON body, so an empty object is passed rather than reaching for a GET-file
   * helper that doesn't exist in `lib/download.ts` for a POST-with-no-body case. */
  exportUnknownTokens: (segmentId: string) =>
    downloadFilePost(`${BASE}/segments/${segmentId}/exports/unknown-tokens`, {}, `post_campaign_import_errors_${timestamp()}.xlsx`),
};
