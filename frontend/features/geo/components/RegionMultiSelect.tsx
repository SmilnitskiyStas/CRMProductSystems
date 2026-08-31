"use client";

import { useRegions } from "../hooks/useRegions";
import { groupRegions } from "../lib/regionLabel";

interface Props {
  value: string[];
  onChange: (codes: string[]) => void;
  /** Codes rendered but not toggleable (e.g. codes already claimed by the opposite set). */
  disabledCodes?: string[];
}

const boxStyle: React.CSSProperties = {
  maxHeight: 240,
  overflowY: "auto",
  padding: "8px 12px",
  background: "#0F1623",
  border: "1px solid #1F2937",
  borderRadius: 8,
};

const hintStyle: React.CSSProperties = {
  padding: "10px 12px",
  background: "#0F1623",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#6B7280",
  fontSize: 12,
};

export function RegionMultiSelect({ value, onChange, disabledCodes = [] }: Props) {
  const { data: regions, isLoading } = useRegions();
  const groups = groupRegions(regions ?? []);
  const selected = new Set(value);
  const disabled = new Set(disabledCodes);

  function toggle(code: string) {
    const next = new Set(selected);
    if (next.has(code)) next.delete(code);
    else next.add(code);
    onChange([...next]);
  }

  if (isLoading) {
    return <div style={hintStyle}>Завантаження регіонів…</div>;
  }
  if (groups.length === 0) {
    return <div style={hintStyle}>виберіть область/місто</div>;
  }

  return (
    <div>
      {value.length === 0 && (
        <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>
          виберіть область/місто
        </div>
      )}
      <div style={boxStyle}>
        {groups.map(({ oblast, cities }) => (
          <div key={oblast.code} style={{ marginBottom: 8 }}>
            {[oblast, ...cities].map((region) => {
              const isOblast = region.kind === "oblast";
              const isDisabled = disabled.has(region.code);
              return (
                <label
                  key={region.code}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 8,
                    padding: "3px 0",
                    paddingLeft: isOblast ? 0 : 18,
                    fontSize: 13,
                    fontWeight: isOblast ? 600 : 400,
                    color: isDisabled ? "#4B5563" : "#E8EDF5",
                    cursor: isDisabled ? "not-allowed" : "pointer",
                  }}
                >
                  <input
                    type="checkbox"
                    checked={selected.has(region.code)}
                    disabled={isDisabled}
                    onChange={() => toggle(region.code)}
                  />
                  {region.nameUa}
                </label>
              );
            })}
          </div>
        ))}
      </div>
    </div>
  );
}
