"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useExportAudienceBuyers } from "../../hooks/useAudienceBuilder";
import { buildExportAudienceRequest } from "../../api/audienceBuilder";
import type { AudienceFilterState } from "../../types";

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * "Вивантажити чеки (XLSX)" — RECEIPT-level export (one row per receipt, so one participant can
 * appear on several rows), never a per-buyer summary (analysis §14.4/§27.2). The mandatory brief
 * requirement is to make that level explicit from the button/hint text alone, since the table
 * shown above it on screen IS buyer-level — the button text plus the caption underneath both call
 * out "чеки" specifically so the two never get confused.
 *
 * Owns its own export mutation + PII-unmask toggle (same self-contained pattern as price-segments'
 * ExportPriceAudienceButton) rather than routing through page.tsx — only needs the already-
 * assembled filter state as a prop.
 */
export function ExportReceiptsButton({ filter, canExportPii }: { filter: AudienceFilterState; canExportPii: boolean }) {
  const t = useTranslations("Dashboard.audienceBuilder.exportReceipts");
  const { mutate, isPending } = useExportAudienceBuyers();
  const [unmaskPii, setUnmaskPii] = useState(false);

  function handleClick() {
    mutate(buildExportAudienceRequest(filter, unmaskPii), {
      onSuccess: () => toast.success(t("successToast")),
      onError: (e) => toast.error(t("errorToast", { message: errorMessage(e) })),
    });
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 6 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        {canExportPii && (
          <label style={{ display: "flex", alignItems: "center", gap: 7, cursor: "pointer" }}>
            <input
              type="checkbox"
              checked={unmaskPii}
              onChange={(e) => setUnmaskPii(e.target.checked)}
              style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
            />
            <span style={{ color: "#9CA3AF", fontSize: 12 }}>{t("unmaskPiiLabel")}</span>
          </label>
        )}
        <Btn
          variant="ghost"
          size="sm"
          disabled={isPending}
          icon={isPending ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
          onClick={handleClick}
        >
          {isPending ? t("downloading") : t("button")}
        </Btn>
      </div>
      <span style={{ color: "#4B5563", fontSize: 11, maxWidth: 260, textAlign: "right" }}>{t("hint")}</span>
    </div>
  );
}
