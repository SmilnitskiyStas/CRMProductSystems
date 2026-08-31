"use client";

import { useTranslations } from "next-intl";
import type { DeliveryCoverage, DeliveryCoverageEntry } from "../types";
import { useRegionLabel } from "../hooks/useRegions";
import { RegionMultiSelect } from "./RegionMultiSelect";

interface Props {
  value: DeliveryCoverage | null;
  onChange: (v: DeliveryCoverage) => void;
}

const EMPTY: DeliveryCoverage = { served: [], notServed: [], note: null };

const labelStyle: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 6,
  display: "block",
};

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

export function DeliveryCoverageEditor({ value, onChange }: Props) {
  const t = useTranslations("Dashboard.geo.coverageEditor");
  const coverage = value ?? EMPTY;
  const regionLabel = useRegionLabel();
  const servedCodes = coverage.served.map((s) => s.regionCode);

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
      (code) => byCode.get(code) ?? { regionCode: code, terms: null },
    );
    emit({ served });
  }

  function setTerms(code: string, terms: string) {
    const served = coverage.served.map((s) =>
      s.regionCode === code
        ? { ...s, terms: terms.trim() === "" ? null : terms }
        : s,
    );
    emit({ served });
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
      {/* Served regions */}
      <div>
        <label style={labelStyle}>{t("servedLabel")}</label>
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
              gap: 6,
              marginTop: 8,
            }}
          >
            {coverage.served.map((entry) => (
              <div
                key={entry.regionCode}
                style={{ display: "flex", alignItems: "center", gap: 8 }}
              >
                <span
                  style={{
                    color: "#9CA3AF",
                    fontSize: 12,
                    minWidth: 130,
                    flexShrink: 0,
                  }}
                >
                  {regionLabel(entry.regionCode)}
                </span>
                <input
                  type="text"
                  value={entry.terms ?? ""}
                  onChange={(e) => setTerms(entry.regionCode, e.target.value)}
                  placeholder={t("servedTermsPlaceholder")}
                  style={inputStyle}
                />
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Not served */}
      <div>
        <label style={labelStyle}>{t("notServedLabel")}</label>
        <RegionMultiSelect
          value={coverage.notServed}
          onChange={(codes) => emit({ notServed: codes })}
          disabledCodes={servedCodes}
        />
      </div>

      {/* General note */}
      <div>
        <label style={labelStyle}>{t("noteLabel")}</label>
        <textarea
          value={coverage.note ?? ""}
          onChange={(e) =>
            emit({ note: e.target.value.trim() === "" ? null : e.target.value })
          }
          placeholder={t("notePlaceholder")}
          rows={3}
          style={{ ...inputStyle, resize: "vertical" }}
        />
      </div>
    </div>
  );
}
