"use client";

import { useMemo, useState } from "react";
import { ChevronDown, ChevronRight } from "lucide-react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import type { MovementDto } from "../api/movements";

const PAGE = 25;

interface Props {
  movements: MovementDto[];
  /** movementType -> color / label, same palette as the chart lines. */
  lineMeta: Record<string, { color: string; label: string }>;
  intlLocale: string;
  /** Movement under the pointer; the chart marks its date with a dot. */
  hoveredId: string | null;
  onHover: (id: string | null) => void;
}

const SIGN_PLUS = new Set(["receipt", "adjustment"]);

function DetailField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div style={{ minWidth: 0 }}>
      <div style={{ color: "#4B5563", fontSize: 10, marginBottom: 2, textTransform: "uppercase", letterSpacing: "0.04em" }}>
        {label}
      </div>
      <div style={{ color: "#E8EDF5", fontSize: 12, wordBreak: "break-word" }}>{children}</div>
    </div>
  );
}

/**
 * Product event log (stock movements). Hovering a row asks the chart to mark that movement's
 * date; clicking a row expands the full record: who confirmed it, where, batch, prices, notes.
 */
export function MovementLog({ movements, lineMeta, intlLocale, hoveredId, onHover }: Props) {
  const t = useTranslations("Dashboard.inventory.analytics");
  const tl = useTranslations("Dashboard.inventory.analytics.log");
  const [typeFilter, setTypeFilter] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [visible, setVisible] = useState(PAGE);

  const types = useMemo(() => Array.from(new Set(movements.map((m) => m.movementType))), [movements]);
  const filtered = useMemo(
    () => (typeFilter ? movements.filter((m) => m.movementType === typeFilter) : movements),
    [movements, typeFilter],
  );
  const shown = filtered.slice(0, visible);

  const fmtDateTime = (iso: string) =>
    new Date(iso).toLocaleString(intlLocale, {
      day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
    });
  const money = (v: number | null) =>
    v == null ? "—" : `${v.toLocaleString(intlLocale, { maximumFractionDigits: 2 })} ₴`;

  return (
    <div>
      <div style={{
        display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10, flexWrap: "wrap",
        marginBottom: 8, paddingBottom: 6, borderBottom: "1px solid #1F2937",
      }}>
        <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
          {t("eventLog")} <span style={{ color: "#374151" }}>· {filtered.length}</span>
        </span>

        <div style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
          {[null, ...types].map((type) => {
            const active = typeFilter === type;
            const color = type ? lineMeta[type]?.color ?? "#6B7280" : "#9CA3AF";
            return (
              <button
                key={type ?? "all"}
                onClick={() => { setTypeFilter(type); setVisible(PAGE); }}
                style={{
                  fontSize: 11, padding: "2px 9px", borderRadius: 999, cursor: "pointer",
                  background: active ? `${color}22` : "transparent",
                  border: `1px solid ${active ? color : "#1F2937"}`,
                  color: active ? "#E8EDF5" : "#6B7280",
                }}
              >
                {type ? lineMeta[type]?.label ?? type : tl("all")}
              </button>
            );
          })}
        </div>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
        {shown.map((m) => {
          const meta = lineMeta[m.movementType];
          const color = meta?.color ?? "#6B7280";
          const isPlus = SIGN_PLUS.has(m.movementType);
          const hovered = hoveredId === m.id;
          const expanded = expandedId === m.id;
          const store = m.fromStoreName && m.toStoreName
            ? `${m.fromStoreName} → ${m.toStoreName}`
            : m.fromStoreName ?? m.toStoreName ?? "—";

          return (
            <div
              key={m.id}
              onMouseEnter={() => onHover(m.id)}
              onMouseLeave={() => onHover(null)}
              style={{
                background: hovered ? "#0F1825" : "#0A0F1A",
                border: `1px solid ${hovered ? color : "#1F2937"}`,
                borderRadius: 7, transition: "background 0.1s, border-color 0.1s",
              }}
            >
              <div
                onClick={() => setExpandedId(expanded ? null : m.id)}
                style={{ display: "flex", alignItems: "center", gap: 10, padding: "7px 12px", cursor: "pointer" }}
              >
                {expanded ? <ChevronDown size={13} color="#4B5563" /> : <ChevronRight size={13} color="#4B5563" />}
                <div style={{ width: 8, height: 8, borderRadius: "50%", flexShrink: 0, background: color }} />

                {/* When it happened — date and time, always visible without expanding */}
                <span style={{
                  color: "#9CA3AF", fontSize: 11, flexShrink: 0, whiteSpace: "nowrap",
                  fontFamily: "monospace", minWidth: 118,
                }}>
                  {fmtDateTime(m.createdAt)}
                </span>

                <span style={{
                  fontSize: 11, padding: "1px 7px", borderRadius: 4, flexShrink: 0,
                  background: `${color}15`, color, fontWeight: 600,
                }}>
                  {meta?.label ?? m.movementType}
                </span>

                <span style={{
                  color: "#9CA3AF", fontSize: 11, flex: 1, minWidth: 0,
                  overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
                }}>
                  {store}
                  {m.performedByName && <span style={{ color: "#4B5563" }}> · {m.performedByName}</span>}
                </span>

                {m.quantityBefore != null && m.quantityAfter != null ? (
                  <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0, whiteSpace: "nowrap" }}>
                    <span style={{ fontFamily: "monospace" }}>{m.quantityBefore}</span>
                    <span style={{ margin: "0 4px" }}>→</span>
                    <span style={{ fontFamily: "monospace", color: "#38BDF8" }}>{m.quantityAfter}</span>
                  </span>
                ) : m.quantityAfter != null ? (
                  <span style={{ color: "#4B5563", fontSize: 11, flexShrink: 0 }}>
                    {t("balancePrefix")} <span style={{ fontFamily: "monospace", color: "#38BDF8" }}>{m.quantityAfter}</span>
                  </span>
                ) : null}

                <span style={{ fontFamily: "monospace", fontSize: 12, flexShrink: 0, color: isPlus ? "#4ADE80" : "#F87171" }}>
                  {isPlus ? `+${m.quantity}` : `−${Math.abs(m.quantity)}`}
                </span>

              </div>

              {expanded && (
                <div style={{
                  display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(170px, 1fr))", gap: "12px 20px",
                  padding: "12px 14px 14px 36px", borderTop: "1px solid #1F2937",
                }}>
                  <DetailField label={tl("when")}>{fmtDateTime(m.createdAt)}</DetailField>
                  <DetailField label={tl("performedBy")}>{m.performedByName ?? "—"}</DetailField>
                  <DetailField label={tl("store")}>{store}</DetailField>
                  <DetailField label={tl("quantity")}>
                    <span style={{ fontFamily: "monospace", color: isPlus ? "#4ADE80" : "#F87171" }}>
                      {isPlus ? `+${m.quantity}` : `−${Math.abs(m.quantity)}`}
                    </span>
                  </DetailField>
                  <DetailField label={tl("balance")}>
                    <span style={{ fontFamily: "monospace" }}>
                      {m.quantityBefore ?? "—"} → {m.quantityAfter ?? "—"}
                    </span>
                  </DetailField>
                  <DetailField label={tl("batch")}>
                    {m.batchNumber ?? "—"}
                    {m.batchExpiryDate && (
                      <span style={{ color: "#6B7280" }}>
                        {" "}· {tl("expires")} {new Date(m.batchExpiryDate).toLocaleDateString(intlLocale)}
                      </span>
                    )}
                  </DetailField>
                  <DetailField label={tl("unitPrice")}>{money(m.unitPrice)}</DetailField>
                  <DetailField label={tl("totalAmount")}>{money(m.totalAmount)}</DetailField>
                  <DetailField label={tl("document")}>
                    {m.referenceType ? (
                      <span>
                        {m.referenceType}
                        {m.referenceId && (
                          <span style={{ color: "#6B7280", fontFamily: "monospace", fontSize: 11 }}>
                            {" "}#{m.referenceId.slice(0, 8)}
                          </span>
                        )}
                      </span>
                    ) : "—"}
                  </DetailField>
                  {m.notes && <DetailField label={tl("notes")}>{m.notes}</DetailField>}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {filtered.length > shown.length && (
        <Btn variant="ghost" size="sm" fullWidth style={{ marginTop: 8 }} onClick={() => setVisible((v) => v + PAGE)}>
          {tl("showMore", { count: Math.min(PAGE, filtered.length - shown.length) })}
        </Btn>
      )}
    </div>
  );
}
