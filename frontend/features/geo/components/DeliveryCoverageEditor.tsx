"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import type { DeliveryCoverage, DeliveryCoverageEntry } from "../types";
import { useRegionLabel } from "../hooks/useRegions";
import { RegionMultiSelect } from "./RegionMultiSelect";

interface Props {
  value: DeliveryCoverage | null;
  onChange: (v: DeliveryCoverage) => void;
}

const EMPTY: DeliveryCoverage = { served: [], notServed: [], note: null };

/** Structured per-region numeric fields (TASK-665). `note` is handled separately. */
type NumericEntryField = "deliveryDaysMin" | "deliveryDaysMax" | "minOrderAmount";
type EntryField = NumericEntryField | "note";

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  background: "#1F2937",
  border: "1px solid #374151",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px",
  outline: "none",
  fontFamily: "inherit",
};

const subLabelStyle: React.CSSProperties = {
  color: "#6B7280",
  fontSize: 11,
  marginBottom: 3,
  display: "block",
};

const sectionHeaderStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 8,
  background: "transparent",
  border: "none",
  padding: 0,
  margin: 0,
  cursor: "pointer",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 600,
  fontFamily: "inherit",
};

function SectionHeader({
  label,
  open,
  onToggle,
}: {
  label: string;
  open: boolean;
  onToggle: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-expanded={open}
      style={sectionHeaderStyle}
    >
      <span
        aria-hidden
        style={{
          display: "inline-block",
          transform: open ? "rotate(90deg)" : "none",
          transition: "transform 0.15s ease",
          fontSize: 10,
        }}
      >
        ▶
      </span>
      {label}
    </button>
  );
}

export function DeliveryCoverageEditor({ value, onChange }: Props) {
  const t = useTranslations("Dashboard.geo.coverageEditor");
  const coverage = value ?? EMPTY;
  const regionLabel = useRegionLabel();
  const servedCodes = coverage.served.map((s) => s.regionCode);

  const [servedOpen, setServedOpen] = useState(true);
  const [notServedOpen, setNotServedOpen] = useState(true);
  const [noteOpen, setNoteOpen] = useState(true);

  function emit(patch: Partial<DeliveryCoverage>) {
    onChange({
      served: patch.served ?? coverage.served,
      notServed: patch.notServed ?? coverage.notServed,
      note: patch.note !== undefined ? patch.note : coverage.note,
    });
  }

  function setServedCodes(codes: string[]) {
    const byCode = new Map(coverage.served.map((s) => [s.regionCode, s]));
    const served: DeliveryCoverageEntry[] = codes.map(
      (code) =>
        byCode.get(code) ?? {
          regionCode: code,
          deliveryDaysMin: null,
          deliveryDaysMax: null,
          minOrderAmount: null,
          note: null,
        },
    );
    emit({ served });
  }

  function setEntryField(code: string, field: EntryField, raw: string) {
    const served = coverage.served.map((s) => {
      if (s.regionCode !== code) return s;
      if (field === "note") {
        return { ...s, note: raw.trim() === "" ? null : raw };
      }
      const parsed = raw.trim() === "" ? null : Number(raw);
      const next =
        parsed != null && Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
      return { ...s, [field]: next };
    });
    emit({ served });
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
      {/* Served regions */}
      <div>
        <SectionHeader
          label={t("servedLabel")}
          open={servedOpen}
          onToggle={() => setServedOpen((v) => !v)}
        />
        {servedOpen && (
          <div style={{ marginTop: 8 }}>
            <RegionMultiSelect
              value={servedCodes}
              onChange={setServedCodes}
              disabledCodes={coverage.notServed}
            />
            {coverage.served.length > 0 && (
              <div
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: 12,
                  marginTop: 12,
                }}
              >
                {coverage.served.map((entry) => (
                  <div
                    key={entry.regionCode}
                    style={{
                      border: "1px solid #1F2937",
                      borderRadius: 8,
                      padding: "10px 12px",
                      display: "flex",
                      flexDirection: "column",
                      gap: 8,
                    }}
                  >
                    <span
                      style={{
                        color: "#E8EDF5",
                        fontSize: 12.5,
                        fontWeight: 600,
                      }}
                    >
                      {regionLabel(entry.regionCode)}
                    </span>

                    <div
                      style={{
                        display: "grid",
                        gridTemplateColumns: "1fr 1fr 1fr",
                        gap: 8,
                      }}
                    >
                      <div>
                        <label style={subLabelStyle}>{t("daysFromLabel")}</label>
                        <input
                          type="number"
                          min="0"
                          inputMode="numeric"
                          value={entry.deliveryDaysMin ?? ""}
                          onChange={(e) =>
                            setEntryField(
                              entry.regionCode,
                              "deliveryDaysMin",
                              e.target.value,
                            )
                          }
                          style={inputStyle}
                        />
                      </div>
                      <div>
                        <label style={subLabelStyle}>{t("daysToLabel")}</label>
                        <input
                          type="number"
                          min="0"
                          inputMode="numeric"
                          value={entry.deliveryDaysMax ?? ""}
                          onChange={(e) =>
                            setEntryField(
                              entry.regionCode,
                              "deliveryDaysMax",
                              e.target.value,
                            )
                          }
                          style={inputStyle}
                        />
                      </div>
                      <div>
                        <label style={subLabelStyle}>
                          {t("minOrderAmountLabel")}
                        </label>
                        <input
                          type="number"
                          min="0"
                          inputMode="decimal"
                          value={entry.minOrderAmount ?? ""}
                          onChange={(e) =>
                            setEntryField(
                              entry.regionCode,
                              "minOrderAmount",
                              e.target.value,
                            )
                          }
                          style={inputStyle}
                        />
                      </div>
                    </div>

                    <div>
                      <label style={subLabelStyle}>{t("regionNoteLabel")}</label>
                      <input
                        type="text"
                        value={entry.note ?? ""}
                        onChange={(e) =>
                          setEntryField(
                            entry.regionCode,
                            "note",
                            e.target.value,
                          )
                        }
                        placeholder={t("regionNotePlaceholder")}
                        style={inputStyle}
                      />
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Not served */}
      <div>
        <SectionHeader
          label={t("notServedLabel")}
          open={notServedOpen}
          onToggle={() => setNotServedOpen((v) => !v)}
        />
        {notServedOpen && (
          <div style={{ marginTop: 8 }}>
            <RegionMultiSelect
              value={coverage.notServed}
              onChange={(codes) => emit({ notServed: codes })}
              disabledCodes={servedCodes}
            />
          </div>
        )}
      </div>

      {/* General note */}
      <div>
        <SectionHeader
          label={t("noteLabel")}
          open={noteOpen}
          onToggle={() => setNoteOpen((v) => !v)}
        />
        {noteOpen && (
          <div style={{ marginTop: 8 }}>
            <textarea
              value={coverage.note ?? ""}
              onChange={(e) =>
                emit({
                  note: e.target.value.trim() === "" ? null : e.target.value,
                })
              }
              placeholder={t("notePlaceholder")}
              rows={3}
              style={{ ...inputStyle, resize: "vertical" }}
            />
          </div>
        )}
      </div>
    </div>
  );
}
