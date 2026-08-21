"use client";

import { useRef } from "react";
import { useTranslations } from "next-intl";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import type { CsvImportResult } from "../types";

interface Props {
  isPending: boolean;
  result: CsvImportResult | null;
  error: string | null;
  onClose: () => void;
  onImport: (file: File) => void;
}

export function CsvImportDialog({
  isPending, result, error, onClose, onImport,
}: Props) {
  const t = useTranslations("Dashboard.sales.csvImport");
  const tCommon = useTranslations("Common");
  const fileRef = useRef<HTMLInputElement>(null);

  function submit() {
    const file = fileRef.current?.files?.[0];
    if (file) onImport(file);
  }

  return (
    <Modal title={t("title")} onClose={onClose}>
      <div style={{ display: "grid", gap: 14 }}>
        <div style={{ color: "#6B7280", fontSize: 12, lineHeight: 1.6 }}>
          {t("formatLabel")} <code style={{ color: "#9CA3AF" }}>barcode,date,quantity_sold[,quantity_end_of_day][,is_promo_day]</code>
          <br />{t("dateFormatLabel")} <code style={{ color: "#9CA3AF" }}>yyyy-MM-dd</code>{t("formatDescription")}
        </div>

        <input
          ref={fileRef}
          type="file"
          accept=".csv,text/csv"
          style={{ color: "#9CA3AF", fontSize: 13 }}
        />

        {error && <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>}

        {result && (
          <div style={{
            background: "#111827", border: "1px solid #1F2937",
            borderRadius: 8, padding: 12, fontSize: 13,
          }}>
            <div style={{ color: "#34D399" }}>{t("resultSummary", { created: result.created, updated: result.updated })}</div>
            {result.skipped > 0 && (
              <div style={{ color: "#FBBF24", marginTop: 4 }}>{t("resultSkipped", { count: result.skipped })}</div>
            )}
            {result.errors.length > 0 && (
              <ul style={{ color: "#F87171", margin: "8px 0 0", paddingLeft: 18, maxHeight: 160, overflowY: "auto" }}>
                {result.errors.map((e, i) => <li key={i}>{e}</li>)}
              </ul>
            )}
          </div>
        )}

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <Btn variant="ghost" type="button" onClick={onClose}>
            {result ? tCommon("close") : tCommon("cancel")}
          </Btn>
          <Btn type="button" onClick={submit} disabled={isPending}>
            {isPending ? t("importing") : t("import")}
          </Btn>
        </div>
      </div>
    </Modal>
  );
}
