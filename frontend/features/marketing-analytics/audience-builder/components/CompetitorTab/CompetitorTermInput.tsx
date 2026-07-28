"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Search, X } from "lucide-react";
import { useAudienceBuilderStore } from "../../store/useAudienceBuilderStore";

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px 9px 32px",
  outline: "none",
  width: "100%",
};

/**
 * "Конкурентний бренд / товар" text input + chips (analysis §15.2) — same Enter→chip interaction
 * as the main TermSearchBar, but text-only (the confirmed competitor behavior in the analysis is
 * always a brand/product text search, no category mode) and writes to `store.competitorTerms`
 * instead of `store.terms`. No separate chips file for this tab (unlike the main term-builder's
 * TermSearchBar/TermChips split) — chips are rendered inline here since this is the only consumer.
 */
export function CompetitorTermInput() {
  const t = useTranslations("Dashboard.audienceBuilder.competitorTermInput");
  const competitorTerms = useAudienceBuilderStore((s) => s.competitorTerms);
  const addCompetitorTerm = useAudienceBuilderStore((s) => s.addCompetitorTerm);
  const removeCompetitorTerm = useAudienceBuilderStore((s) => s.removeCompetitorTerm);
  const [value, setValue] = useState("");

  function commit() {
    if (!value.trim()) return;
    addCompetitorTerm(value);
    setValue("");
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      <div>
        <label style={{ color: "#4B5563", fontSize: 12, marginBottom: 4, display: "block" }}>{t("label")}</label>
        <div style={{ position: "relative", maxWidth: 420 }}>
          <Search size={14} color="#4B5563" style={{ position: "absolute", left: 10, top: "50%", transform: "translateY(-50%)" }} />
          <input
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                commit();
              }
            }}
            placeholder={competitorTerms.length === 0 ? t("placeholderFirst") : t("placeholderNext")}
            style={inputStyle}
          />
        </div>
      </div>

      {competitorTerms.length > 0 && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
          {competitorTerms.map((term) => (
            <span
              key={term.id}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                background: "#2D0A0A",
                border: "1px solid #7F1D1D",
                borderRadius: 999,
                padding: "5px 6px 5px 10px",
                color: "#F87171",
                fontSize: 12.5,
              }}
            >
              <span>{term.text}</span>
              <button
                type="button"
                aria-label={t("removeLabel")}
                onClick={() => removeCompetitorTerm(term.id)}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  width: 18,
                  height: 18,
                  borderRadius: "50%",
                  border: "none",
                  background: "rgba(0,0,0,0.25)",
                  color: "#F87171",
                  cursor: "pointer",
                  padding: 0,
                }}
              >
                <X size={11} />
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
