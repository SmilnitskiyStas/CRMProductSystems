"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { Search, Tag as TagIcon } from "lucide-react";
import { useAudienceBuilderStore } from "../store/useAudienceBuilderStore";
import { useAudienceCategorySearch } from "../hooks/useAudienceBuilder";

type SearchMode = "text" | "category";

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px",
  outline: "none",
  width: "100%",
};

function modeBtnStyle(active: boolean): React.CSSProperties {
  return {
    padding: "6px 12px",
    fontSize: 12,
    fontWeight: 600,
    borderRadius: 6,
    border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
    cursor: "pointer",
    background: active ? "#1D3461" : "#0D1117",
    color: active ? "#93C5FD" : "#6B7280",
  };
}

/**
 * "За назвою/ID" / "За категорією" toggle + input + Enter→chip (analysis §6/§7/§9). Text terms
 * match Item.Id/Barcodes/Name server-side (task log 429); category terms come from a live
 * typeahead showing each option's item count. Writes directly to the Zustand store
 * (`addTextTerm`/`addCategoryTerm`) — no props needed, this feature's leaf control components own
 * their own store reads/writes (see store's doc comment for why).
 */
export function TermSearchBar() {
  const t = useTranslations("Dashboard.audienceBuilder.termSearchBar");
  const terms = useAudienceBuilderStore((s) => s.terms);
  const addTextTerm = useAudienceBuilderStore((s) => s.addTextTerm);
  const addCategoryTerm = useAudienceBuilderStore((s) => s.addCategoryTerm);

  const [searchMode, setSearchMode] = useState<SearchMode>("text");
  const [value, setValue] = useState("");
  const [debounced, setDebounced] = useState("");
  const [open, setOpen] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

  const { data: suggestions = [], isFetching } = useAudienceCategorySearch(debounced, searchMode === "category" && open);

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, []);

  function handleModeChange(m: SearchMode) {
    setSearchMode(m);
    setValue("");
    setDebounced("");
    setOpen(false);
  }

  function handleInputChange(v: string) {
    setValue(v);
    if (searchMode === "category") {
      setOpen(true);
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = setTimeout(() => setDebounced(v), 300);
    }
  }

  function commitText() {
    if (!value.trim()) return;
    addTextTerm(value);
    setValue("");
  }

  function selectCategory(categoryId: string, name: string, itemCount: number) {
    addCategoryTerm(categoryId, name, itemCount);
    setValue("");
    setDebounced("");
    setOpen(false);
  }

  const placeholder =
    searchMode === "category" ? t("placeholderCategory") : terms.length === 0 ? t("placeholderFirstText") : t("placeholderNextText");

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      <div style={{ display: "flex", gap: 6 }}>
        <button type="button" style={modeBtnStyle(searchMode === "text")} onClick={() => handleModeChange("text")}>
          {t("modeText")}
        </button>
        <button type="button" style={modeBtnStyle(searchMode === "category")} onClick={() => handleModeChange("category")}>
          {t("modeCategory")}
        </button>
      </div>

      <div ref={wrapRef} style={{ position: "relative", maxWidth: 420 }}>
        <div style={{ position: "relative" }}>
          <Search size={14} color="#4B5563" style={{ position: "absolute", left: 10, top: "50%", transform: "translateY(-50%)" }} />
          <input
            value={value}
            onChange={(e) => handleInputChange(e.target.value)}
            onFocus={() => searchMode === "category" && setOpen(true)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && searchMode === "text") {
                e.preventDefault();
                commitText();
              }
            }}
            placeholder={placeholder}
            style={{ ...inputStyle, paddingLeft: 32 }}
          />
        </div>

        {searchMode === "category" && open && value.trim().length > 0 && (
          <div
            style={{
              position: "absolute",
              top: "calc(100% + 6px)",
              left: 0,
              right: 0,
              maxHeight: 260,
              overflowY: "auto",
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 10,
              boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
              zIndex: 200,
            }}
          >
            {isFetching ? (
              <div style={{ padding: "10px 12px", color: "#4B5563", fontSize: 12 }}>{t("searching")}</div>
            ) : suggestions.length === 0 ? (
              <div style={{ padding: "10px 12px", color: "#4B5563", fontSize: 12 }}>{t("noCategoryResults")}</div>
            ) : (
              suggestions.map((c) => (
                <button
                  key={c.categoryId}
                  type="button"
                  onClick={() => selectCategory(c.categoryId, c.name, c.itemCount)}
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    width: "100%",
                    padding: "9px 12px",
                    background: "transparent",
                    border: "none",
                    borderBottom: "1px solid #111827",
                    cursor: "pointer",
                    textAlign: "left",
                  }}
                >
                  <span style={{ display: "flex", alignItems: "center", gap: 8, color: "#E8EDF5", fontSize: 13 }}>
                    <TagIcon size={12} color="#6B7280" />
                    {c.name}
                  </span>
                  <span style={{ color: "#4B5563", fontSize: 11.5, flexShrink: 0, marginLeft: 8 }}>
                    {t("categoryItemCount", { count: c.itemCount })}
                  </span>
                </button>
              ))
            )}
          </div>
        )}
      </div>
    </div>
  );
}
