"use client";

import { useTranslations } from "next-intl";
import { X, Tag as TagIcon, Type } from "lucide-react";
import { useAudienceBuilderStore } from "../store/useAudienceBuilderStore";

/** Renders `store.terms` as removable chips (analysis §7.1: "chip показує введений термін ·
 * кнопка × · aria-label «видалити»"). Reads/writes the store directly — see store's own doc
 * comment for why. */
export function TermChips() {
  const t = useTranslations("Dashboard.audienceBuilder.termChips");
  const terms = useAudienceBuilderStore((s) => s.terms);
  const removeTerm = useAudienceBuilderStore((s) => s.removeTerm);

  if (terms.length === 0) return null;

  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
      {terms.map((term) => (
        <span
          key={term.id}
          style={{
            display: "inline-flex",
            alignItems: "center",
            gap: 6,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 999,
            padding: "5px 6px 5px 10px",
            color: "#E8EDF5",
            fontSize: 12.5,
          }}
        >
          {term.kind === "Category" ? <TagIcon size={11} color="#60A5FA" /> : <Type size={11} color="#6B7280" />}
          <span>{term.kind === "Category" ? term.categoryName : term.text}</span>
          {term.kind === "Category" && term.categoryItemCount != null && (
            <span style={{ color: "#4B5563" }}>· {t("categoryItemCount", { count: term.categoryItemCount })}</span>
          )}
          <button
            type="button"
            aria-label={t("removeLabel")}
            onClick={() => removeTerm(term.id)}
            style={{
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              width: 18,
              height: 18,
              borderRadius: "50%",
              border: "none",
              background: "#161B26",
              color: "#9CA3AF",
              cursor: "pointer",
              padding: 0,
            }}
          >
            <X size={11} />
          </button>
        </span>
      ))}
    </div>
  );
}
