"use client";

import { useTranslations } from "next-intl";
import { parseOverlap, type BulkResult } from "../hooks/useBulkAddShifts";

const DAY_KEYS = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"] as const;

interface Props {
  result: BulkResult;
  userNames: Map<string, string>;
  onClose: () => void;
}

/**
 * Compact outcome of a bulk "workers x days" add: one line per worker listing the days that
 * were created and, separately, the days that were skipped with the localized reason (an
 * existing overlapping shift). Shown only when something failed — a fully successful add is
 * just a toast.
 */
export function BulkResultNotice({ result, userNames, onClose }: Props) {
  const t = useTranslations("Dashboard.schedules.weekGrid");
  const tDay = useTranslations("Dashboard.schedules.weekGrid.dayShort");

  function dayLabel(date: string) {
    const d = new Date(date); // YYYY-MM-DD parses as UTC
    return `${tDay(DAY_KEYS[(d.getUTCDay() + 6) % 7])} ${date.slice(8, 10)}.${date.slice(5, 7)}`;
  }

  const userIds = Array.from(new Set([...result.ok, ...result.failed.map((f) => f.item)].map((i) => i.userId)));

  return (
    <div
      style={{
        background: "#111827", border: "1px solid #374151", borderRadius: 10,
        padding: "10px 14px", marginBottom: 14, fontSize: 12, color: "#9CA3AF",
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", gap: 10, marginBottom: 6 }}>
        <span style={{ color: "#E8EDF5", fontWeight: 600 }}>
          {t("bulkSummary", { ok: result.ok.length, failed: result.failed.length })}
        </span>
        <button
          onClick={onClose}
          aria-label={t("bulkClose")}
          style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", fontSize: 14 }}
        >
          ✕
        </button>
      </div>
      {userIds.map((uid) => {
        const okDays = result.ok.filter((i) => i.userId === uid).map((i) => dayLabel(i.shiftDate));
        const failed = result.failed.filter((f) => f.item.userId === uid);
        return (
          <div key={uid} style={{ marginTop: 4 }}>
            <span style={{ color: "#E8EDF5", fontWeight: 500 }}>{userNames.get(uid) ?? "—"}: </span>
            {okDays.length > 0 && <span style={{ color: "#4ADE80" }}>✓ {okDays.join(", ")}</span>}
            {okDays.length > 0 && failed.length > 0 && " · "}
            {failed.length > 0 && (
              <span style={{ color: "#F87171" }}>
                ✗{" "}
                {failed
                  .map((f) => {
                    const o = parseOverlap(f.message);
                    return `${dayLabel(f.item.shiftDate)} (${o ? t("bulkOverlap", { start: o.start, end: o.end }) : t("bulkFailed")})`;
                  })
                  .join(", ")}
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
}
