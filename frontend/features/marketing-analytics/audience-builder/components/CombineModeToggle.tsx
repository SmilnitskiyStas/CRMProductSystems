"use client";

import { useTranslations } from "next-intl";
import { useAudienceBuilderStore } from "../store/useAudienceBuilderStore";
import type { AudienceCombineMode } from "../types";

const MODES: AudienceCombineMode[] = ["Any", "All"];

/**
 * OR/AND toggle between terms — "Будь-який товар" / "Усі товари" (analysis §8). Self-contained:
 * renders nothing at all when `terms.length <= 1` (design doc: appears only once it's meaningful),
 * so callers just always mount it rather than duplicating the length check at every call site.
 */
export function CombineModeToggle() {
  const t = useTranslations("Dashboard.audienceBuilder.combineModeToggle");
  const terms = useAudienceBuilderStore((s) => s.terms);
  const mode = useAudienceBuilderStore((s) => s.mode);
  const setMode = useAudienceBuilderStore((s) => s.setMode);

  if (terms.length <= 1) return null;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      <span style={{ color: "#4B5563", fontSize: 12 }}>{t("label")}</span>
      <div style={{ display: "flex", gap: 4 }}>
        {MODES.map((m) => {
          const active = mode === m;
          return (
            <button
              key={m}
              type="button"
              onClick={() => setMode(m)}
              style={{
                padding: "7px 14px",
                fontSize: 12,
                fontWeight: 600,
                borderRadius: 6,
                border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                cursor: "pointer",
                background: active ? "#1D3461" : "#0D1117",
                color: active ? "#93C5FD" : "#6B7280",
              }}
            >
              {t(m === "Any" ? "any" : "all")}
            </button>
          );
        })}
      </div>
    </div>
  );
}
