"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { ChevronDown, History, CheckCircle2, Circle } from "lucide-react";
import type { PostCampaignSegmentListItemDto } from "../types";

interface Props {
  segments: PostCampaignSegmentListItemDto[];
  isLoading: boolean;
  selectedSegmentId: string | null;
  onSelect: (item: PostCampaignSegmentListItemDto) => void;
}

/**
 * Simple reopen-a-previous-segment dropdown (source doc §35.1: "збережені named segments" —
 * brief item 9 explicitly says a simple list/dropdown is enough for v1, no elaborate UI needed).
 * A button showing the currently selected segment (or a placeholder) that opens a popover listing
 * every segment from `GET segments`, newest first — clicking a row selects it immediately (no
 * separate "apply" step, unlike the store multi-selects elsewhere in this feature, since selecting
 * a segment is a single atomic choice, not a set being built up).
 */
export function SegmentsList({ segments, isLoading, selectedSegmentId, onSelect }: Props) {
  const t = useTranslations("Dashboard.postCampaign.segmentsList");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, []);

  const selected = segments.find((s) => s.id === selectedSegmentId) ?? null;
  const label = selected ? (selected.name || t("unnamed")) : t("placeholder");

  return (
    <div ref={ref} style={{ position: "relative" }}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        style={{
          display: "flex",
          alignItems: "center",
          gap: 8,
          background: open ? "#161B26" : "#0D1117",
          border: `1px solid ${open ? "#374151" : "#1F2937"}`,
          borderRadius: 8,
          padding: "8px 12px",
          cursor: "pointer",
          color: "#E8EDF5",
        }}
      >
        <History size={14} color="#6B7280" />
        <span style={{ fontSize: 13, maxWidth: 220, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{label}</span>
        <ChevronDown size={13} color="#6B7280" style={{ transform: open ? "rotate(180deg)" : "none", transition: "transform 0.15s" }} />
      </button>

      {open && (
        <div
          style={{
            position: "absolute",
            top: "calc(100% + 6px)",
            left: 0,
            minWidth: 340,
            maxWidth: 420,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
            zIndex: 200,
            overflow: "hidden",
          }}
        >
          <div style={{ padding: "8px 12px", color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.04em", borderBottom: "1px solid #1F2937" }}>
            {t("popoverTitle")}
          </div>
          <div style={{ maxHeight: 320, overflowY: "auto" }}>
            {isLoading ? (
              <div style={{ padding: "14px 12px", color: "#4B5563", fontSize: 12.5 }}>{t("loading")}</div>
            ) : segments.length === 0 ? (
              <div style={{ padding: "14px 12px", color: "#4B5563", fontSize: 12.5 }}>{t("empty")}</div>
            ) : (
              segments.map((s) => {
                const active = s.id === selectedSegmentId;
                return (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => {
                      onSelect(s);
                      setOpen(false);
                    }}
                    style={{
                      display: "flex",
                      width: "100%",
                      justifyContent: "space-between",
                      alignItems: "center",
                      gap: 10,
                      padding: "9px 12px",
                      background: active ? "#111827" : "transparent",
                      border: "none",
                      borderBottom: "1px solid #111827",
                      cursor: "pointer",
                      textAlign: "left",
                    }}
                  >
                    <div style={{ display: "flex", alignItems: "center", gap: 8, minWidth: 0 }}>
                      {s.isAnalyzed ? <CheckCircle2 size={13} color="#4ADE80" style={{ flexShrink: 0 }} /> : <Circle size={13} color="#6B7280" style={{ flexShrink: 0 }} />}
                      <span style={{ color: "#E8EDF5", fontSize: 13, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{s.name || t("unnamed")}</span>
                    </div>
                    <span style={{ color: "#6B7280", fontSize: 11, flexShrink: 0 }}>
                      {t("matchedCount", { count: s.matchedCount })} · {new Date(s.createdAt).toLocaleDateString(intlLocale)}
                    </span>
                  </button>
                );
              })
            )}
          </div>
        </div>
      )}
    </div>
  );
}
