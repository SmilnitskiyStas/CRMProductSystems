"use client";

import { useRegions } from "../hooks/useRegions";
import { groupRegions } from "../lib/regionLabel";

interface Props {
  value: string | null;
  onChange: (code: string | null) => void;
  placeholder?: string;
  /** When true (default) the "no region" option is selectable. */
  allowEmpty?: boolean;
}

const selectStyle: React.CSSProperties = {
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
  cursor: "pointer",
};

// Two non-breaking spaces to visually nest cities under their oblast in the
// native <option> list (real indentation is not stylable cross-browser).
const CITY_INDENT = "  ";

export function RegionSelect({
  value,
  onChange,
  placeholder,
  allowEmpty = true,
}: Props) {
  const { data: regions, isLoading } = useRegions();
  const groups = groupRegions(regions ?? []);

  return (
    <select
      value={value ?? ""}
      disabled={isLoading}
      onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
      style={selectStyle}
    >
      {allowEmpty ? (
        <option value="">{placeholder ?? "Усі регіони"}</option>
      ) : value == null ? (
        <option value="" disabled>
          {placeholder ?? "Оберіть регіон"}
        </option>
      ) : null}

      {groups.map(({ oblast, cities }) => (
        <optgroup key={oblast.code} label={oblast.nameUa}>
          <option value={oblast.code}>{oblast.nameUa}</option>
          {cities.map((city) => (
            <option key={city.code} value={city.code}>
              {CITY_INDENT}
              {city.nameUa}
            </option>
          ))}
        </optgroup>
      ))}
    </select>
  );
}
