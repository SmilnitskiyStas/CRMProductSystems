"use client";

import { useTranslations, useLocale } from "next-intl";
import { Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useExportPostCampaignUnknownTokens } from "../hooks/usePostCampaign";
import type { PostCampaignImportResultDto } from "../types";

interface Props {
  result: PostCampaignImportResultDto;
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

function StatBox({ label, value, color }: { label: string; value: number; color?: string }) {
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  return (
    <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: "10px 14px", minWidth: 110 }}>
      <div style={{ color: "#4B5563", fontSize: 10.5, textTransform: "uppercase", letterSpacing: "0.04em" }}>{label}</div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
        {value.toLocaleString(intlLocale)}
      </div>
    </div>
  );
}

/**
 * Import validation summary (source doc §5.2/§8/§8.2): uploaded/matched/duplicate/unknown/invalid
 * counts, first-20 unknown + invalid token SAMPLES inline (the exact same ≤20 lists the server
 * stores on the segment — `PostCampaignSegment.UnknownTokensSample`/`InvalidTokensSample` — so the
 * downloadable report below never contains MORE rows than what's already shown here, only the
 * same ones in a file), and the export-errors button (`POST exports/unknown-tokens`). Brief's
 * explicit requirement: only `matchedCount` feeds every downstream denominator — this component
 * exists precisely so unknown/invalid tokens are never silently swallowed into that denominator
 * (source doc §8's core complaint about the competitor).
 */
export function ValidationSummary({ result }: Props) {
  const t = useTranslations("Dashboard.postCampaign.validationSummary");
  const { mutate, isPending } = useExportPostCampaignUnknownTokens();

  const hasIssues = result.unknownCount > 0 || result.invalidCount > 0;

  // TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20): the export
  // download only ever contains what's in these sample arrays (server-persisted at import time,
  // capped — see SegmentImportParser.InvalidSampleCap / PostCampaignService.UnknownSampleCap).
  // `unknownCount`/`invalidCount` are the segment's real, UNCAPPED totals, already present on
  // this same import response — so whether the export will be a complete list or a truncated
  // sample is knowable entirely from data already on screen, no extra request needed. Same
  // "never silently imply completeness" standard as CustomerTable's own pagination footer.
  const unknownShown = result.unknownTokensSample.length;
  const invalidShown = result.invalidTokensSample.length;
  const isExportTruncated = unknownShown < result.unknownCount || invalidShown < result.invalidCount;

  // TASK-478 fix, side effect: the persisted sample (and therefore the export) can now hold up to
  // ~500 of each kind instead of 20 — fine for a downloadable file, but this on-screen chip list
  // was sized for a max-20 quick glance. Keep the INLINE preview capped at the old 20 regardless
  // of how large the underlying sample got; the export button below still uses the full sample.
  const INLINE_PREVIEW_CAP = 20;
  const unknownPreview = result.unknownTokensSample.slice(0, INLINE_PREVIEW_CAP);
  const invalidPreview = result.invalidTokensSample.slice(0, INLINE_PREVIEW_CAP);
  const unknownHiddenOnScreen = unknownShown - unknownPreview.length;
  const invalidHiddenOnScreen = invalidShown - invalidPreview.length;

  function handleExportErrors() {
    mutate(result.segmentId, {
      onSuccess: () => toast.success(t("exportSuccessToast")),
      onError: (e) => toast.error(t("exportErrorToast", { message: errorMessage(e) })),
    });
  }

  return (
    <div style={{ background: "#0A0F1A", border: "1px solid #1F2937", borderRadius: 12, padding: 18, display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700 }}>{t("title")}</div>

      <div style={{ display: "flex", flexWrap: "wrap", gap: 10 }}>
        <StatBox label={t("uploaded")} value={result.uploadedCount} />
        <StatBox label={t("matched")} value={result.matchedCount} color="#4ADE80" />
        <StatBox label={t("duplicate")} value={result.duplicateCount} color="#9CA3AF" />
        <StatBox label={t("unknown")} value={result.unknownCount} color={result.unknownCount > 0 ? "#FBBF24" : undefined} />
        <StatBox label={t("invalid")} value={result.invalidCount} color={result.invalidCount > 0 ? "#F87171" : undefined} />
      </div>

      {hasIssues && (
        <>
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            {unknownPreview.length > 0 && (
              <div>
                <div style={{ color: "#FBBF24", fontSize: 11.5, fontWeight: 600, marginBottom: 4 }}>
                  {t("unknownSampleTitle", { count: unknownPreview.length })}
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 5 }}>
                  {unknownPreview.map((tok, i) => (
                    <code key={i} style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 5, padding: "2px 7px", fontSize: 11, color: "#D1B26B" }}>
                      {tok}
                    </code>
                  ))}
                </div>
                {unknownHiddenOnScreen > 0 && (
                  <p style={{ color: "#4B5563", fontSize: 10.5, margin: "4px 0 0" }}>{t("moreInExportOnly", { count: unknownHiddenOnScreen })}</p>
                )}
              </div>
            )}
            {invalidPreview.length > 0 && (
              <div>
                <div style={{ color: "#F87171", fontSize: 11.5, fontWeight: 600, marginBottom: 4 }}>
                  {t("invalidSampleTitle", { count: invalidPreview.length })}
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 5 }}>
                  {invalidPreview.map((tok, i) => (
                    <code key={i} style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 5, padding: "2px 7px", fontSize: 11, color: "#FCA5A5" }}>
                      {tok}
                    </code>
                  ))}
                </div>
                {invalidHiddenOnScreen > 0 && (
                  <p style={{ color: "#4B5563", fontSize: 10.5, margin: "4px 0 0" }}>{t("moreInExportOnly", { count: invalidHiddenOnScreen })}</p>
                )}
              </div>
            )}
          </div>

          <div>
            <Btn
              variant="ghost"
              size="sm"
              disabled={isPending}
              icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
              onClick={handleExportErrors}
            >
              {isPending ? t("exporting") : t("exportErrorsButton")}
            </Btn>
            <p style={{ color: "#4B5563", fontSize: 10.5, margin: "6px 0 0" }}>
              {isExportTruncated
                ? t("exportErrorsHintTruncated", { unknownShown, unknownCount: result.unknownCount, invalidShown, invalidCount: result.invalidCount })
                : t("exportErrorsHintComplete", { unknownCount: result.unknownCount, invalidCount: result.invalidCount })}
            </p>
          </div>
        </>
      )}
    </div>
  );
}
