"use client";

import { useEffect, useRef } from "react";
import { Switch } from "@/components/ui/switch";

export interface SimpleDateRange {
  from: Date;
  to: Date;
}

interface DateRangePickerProps {
  range: SimpleDateRange;
  onRangeChange: (range: SimpleDateRange) => void;
  compareEnabled: boolean;
  onCompareToggle: (enabled: boolean) => void;
  /** Preview/editable "previous period" range. Undefined until auto-derived or set by the caller. */
  compareRange?: SimpleDateRange;
  onCompareRangeChange?: (range: SimpleDateRange) => void;
}

const MS_PER_DAY = 86_400_000;

/**
 * Same shift algorithm as the backend (AnalyticsService):
 * compareTo = from - 1d, compareFrom = compareTo - (to - from).
 */
export function computePreviousPeriod(from: Date, to: Date): SimpleDateRange {
  const lengthMs = to.getTime() - from.getTime();
  const compareTo = new Date(from.getTime() - MS_PER_DAY);
  const compareFrom = new Date(compareTo.getTime() - lengthMs);
  return { from: compareFrom, to: compareTo };
}

export function toDateInputValue(d: Date): string {
  return d.toISOString().slice(0, 10);
}

export function parseDateInputValue(s: string): Date {
  const [y, m, day] = s.split("-").map(Number);
  return new Date(Date.UTC(y, (m ?? 1) - 1, day ?? 1));
}

const labelStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  marginBottom: 4,
  display: "block",
};

const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 10px",
  outline: "none",
};

const compareInputStyle: React.CSSProperties = {
  ...inputStyle,
  border: "1px solid #374151",
};

/**
 * Two date inputs for the primary range + an optional "compare with previous
 * period" toggle that reveals a second, independently editable range. When
 * the toggle flips on without an explicit compare range already set, the
 * previous period is auto-derived using the same shift algorithm as the
 * backend (see `computePreviousPeriod`).
 */
export function DateRangePicker({
  range,
  onRangeChange,
  compareEnabled,
  onCompareToggle,
  compareRange,
  onCompareRangeChange,
}: DateRangePickerProps) {
  const wasEnabled = useRef(compareEnabled);

  useEffect(() => {
    if (compareEnabled && !wasEnabled.current && !compareRange) {
      onCompareRangeChange?.(computePreviousPeriod(range.from, range.to));
    }
    wasEnabled.current = compareEnabled;
    // Only react to the toggle transition, not to every range/compareRange change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [compareEnabled]);

  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: 16, alignItems: "flex-end" }}>
      <div>
        <label style={labelStyle}>З дати</label>
        <input
          type="date"
          value={toDateInputValue(range.from)}
          max={toDateInputValue(range.to)}
          onChange={(e) => e.target.value && onRangeChange({ from: parseDateInputValue(e.target.value), to: range.to })}
          style={inputStyle}
        />
      </div>
      <div>
        <label style={labelStyle}>По дату</label>
        <input
          type="date"
          value={toDateInputValue(range.to)}
          min={toDateInputValue(range.from)}
          onChange={(e) => e.target.value && onRangeChange({ from: range.from, to: parseDateInputValue(e.target.value) })}
          style={inputStyle}
        />
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 8, paddingBottom: 7 }}>
        <Switch checked={compareEnabled} onCheckedChange={onCompareToggle} />
        <span style={{ color: "#9CA3AF", fontSize: 13 }}>Порівняти з попереднім періодом</span>
      </div>

      {compareEnabled && (
        <>
          <div>
            <label style={labelStyle}>Порівняння: з дати</label>
            <input
              type="date"
              value={compareRange ? toDateInputValue(compareRange.from) : ""}
              max={compareRange ? toDateInputValue(compareRange.to) : undefined}
              onChange={(e) =>
                e.target.value &&
                onCompareRangeChange?.({
                  from: parseDateInputValue(e.target.value),
                  to: compareRange?.to ?? range.from,
                })
              }
              style={compareInputStyle}
            />
          </div>
          <div>
            <label style={labelStyle}>Порівняння: по дату</label>
            <input
              type="date"
              value={compareRange ? toDateInputValue(compareRange.to) : ""}
              min={compareRange ? toDateInputValue(compareRange.from) : undefined}
              onChange={(e) =>
                e.target.value &&
                onCompareRangeChange?.({
                  from: compareRange?.from ?? range.from,
                  to: parseDateInputValue(e.target.value),
                })
              }
              style={compareInputStyle}
            />
          </div>
        </>
      )}
    </div>
  );
}
