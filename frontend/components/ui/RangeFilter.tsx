"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";

export interface RangeFilterValue {
  min?: number;
  max?: number;
}

interface RangeFilterProps {
  min: number | undefined;
  max: number | undefined;
  onChange: (next: RangeFilterValue) => void;
  /** Leading label shown before the two inputs, e.g. "Кількість" — the inputs themselves
   * always use the generic "від"/"до" placeholders (Common namespace), so the composed
   * control reads "<placeholder> [від] – [до]". */
  placeholder?: string;
}

const inputStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 12px",
  outline: "none",
  width: 90,
};

function parseInput(raw: string): number | undefined {
  if (raw.trim() === "") return undefined;
  const n = Number(raw);
  return Number.isNaN(n) ? undefined : n;
}

/**
 * Controlled "від" / "до" numeric range pair, matching the inputStyle already duplicated
 * across the Stock/Inventory/Receipts/Transfers/Write-offs filter bars. Debounced (300ms),
 * mirroring the search-input debounce pattern used on every one of these pages, so it
 * doesn't fire a request per keystroke.
 *
 * Local input strings are kept separately from the debounced `min`/`max` props so an
 * in-progress keystroke is never clobbered by a parent re-render mid-typing. Empty string
 * parses to `undefined`, never `0` or `NaN` — callers must check results with `!= null`
 * (never a truthy check), since `0` is a valid bound.
 */
export function RangeFilter({ min, max, onChange, placeholder }: RangeFilterProps) {
  const t = useTranslations("Common");
  const [minInput, setMinInput] = useState(min !== undefined ? String(min) : "");
  const [maxInput, setMaxInput] = useState(max !== undefined ? String(max) : "");
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  function schedule(nextMinInput: string, nextMaxInput: string) {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => {
      onChange({ min: parseInput(nextMinInput), max: parseInput(nextMaxInput) });
    }, 300);
  }

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
      {placeholder && (
        <span style={{ color: "#6B7280", fontSize: 12, whiteSpace: "nowrap" }}>{placeholder}</span>
      )}
      <input
        type="number"
        inputMode="decimal"
        placeholder={t("rangeFrom")}
        value={minInput}
        onChange={(e) => {
          setMinInput(e.target.value);
          schedule(e.target.value, maxInput);
        }}
        style={inputStyle}
      />
      <span style={{ color: "#4B5563" }}>–</span>
      <input
        type="number"
        inputMode="decimal"
        placeholder={t("rangeTo")}
        value={maxInput}
        onChange={(e) => {
          setMaxInput(e.target.value);
          schedule(minInput, e.target.value);
        }}
        style={inputStyle}
      />
    </div>
  );
}
