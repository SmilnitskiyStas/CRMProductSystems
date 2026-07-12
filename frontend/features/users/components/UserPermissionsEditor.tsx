"use client";

import { useState } from "react";
import {
  useUpdatePermissions,
  useActivePermissionGrants,
  useGrantTemporaryPermission,
  useRevokeTemporaryPermission,
} from "../hooks/useUsers";
import { PAGES, ROLE_RANK, roleHasPageAccess, MAX_GRANT_DURATION_DAYS } from "../types";
import type { UserDto } from "../types";
import { Btn } from "@/components/ui/Btn";

interface Props {
  user: UserDto;
  editorRole: string;
}

type Override = "grant" | "deny" | "default";

function getInitialOverrides(user: UserDto): Record<string, Override> {
  const result: Record<string, Override> = {};
  for (const page of PAGES) {
    const perm = user.permissions?.[page.slug];
    result[page.slug] = perm === true ? "grant" : perm === false ? "deny" : "default";
  }
  return result;
}

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/** Format a Date as a `datetime-local` input value in the browser's local timezone. */
function toLocalDatetimeInputValue(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function formatGrantDate(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

export function UserPermissionsEditor({ user, editorRole }: Props) {
  const updatePerms = useUpdatePermissions(user.id);
  const [overrides, setOverrides] = useState<Record<string, Override>>(
    () => getInitialOverrides(user),
  );
  const [saved, setSaved] = useState(false);
  const [err,   setErr]   = useState("");

  // Editor can only edit users with lower rank
  const editorRank = ROLE_RANK[editorRole] ?? 0;
  const targetRank = ROLE_RANK[user.role]  ?? 0;
  const canEdit    = editorRank > targetRank;

  // ── Temporary permission grants (ADR-019, TASK-344) ─────────────────────────
  const grantsQuery = useActivePermissionGrants(user.id);
  const grantTemp   = useGrantTemporaryPermission(user.id);
  const revokeTemp  = useRevokeTemporaryPermission(user.id);
  const activeGrants = grantsQuery.data ?? [];

  const [tempPickerSlug, setTempPickerSlug] = useState<string | null>(null);
  const [tempExpiresAt,  setTempExpiresAt]  = useState("");
  const [tempErr,        setTempErr]        = useState("");

  const now = new Date();
  const minExpiresAt = toLocalDatetimeInputValue(new Date(now.getTime() + 60_000));
  const maxExpiresAt = toLocalDatetimeInputValue(new Date(now.getTime() + MAX_GRANT_DURATION_DAYS * 86_400_000));

  function activeGrantFor(slug: string) {
    return activeGrants.find((g) => g.permissionKey === slug);
  }

  function openTempPicker(slug: string) {
    setTempPickerSlug(slug);
    setTempErr("");
    setTempExpiresAt(toLocalDatetimeInputValue(new Date(now.getTime() + 24 * 60 * 60_000)));
  }

  function closeTempPicker() {
    setTempPickerSlug(null);
    setTempErr("");
  }

  async function handleGrantTemp(slug: string) {
    if (!tempExpiresAt) {
      setTempErr("Вкажіть дату й час закінчення.");
      return;
    }
    const expires = new Date(tempExpiresAt);
    const maxAllowed = new Date(now.getTime() + MAX_GRANT_DURATION_DAYS * 86_400_000);
    if (expires.getTime() <= now.getTime()) {
      setTempErr("Дата закінчення має бути в майбутньому.");
      return;
    }
    if (expires.getTime() > maxAllowed.getTime()) {
      setTempErr(`Максимальний термін — ${MAX_GRANT_DURATION_DAYS} днів.`);
      return;
    }
    try {
      await grantTemp.mutateAsync({ permissionKey: slug, expiresAt: expires.toISOString() });
      closeTempPicker();
    } catch (e) {
      setTempErr((e as Error)?.message ?? "Помилка надання тимчасового доступу");
    }
  }

  async function handleRevokeTemp(grantId: string) {
    if (!confirm("Достроково відкликати тимчасовий доступ?")) return;
    try {
      await revokeTemp.mutateAsync(grantId);
    } catch (e) {
      setTempErr((e as Error)?.message ?? "Помилка відкликання доступу");
    }
  }

  function setOverride(slug: string, value: Override) {
    setOverrides((prev) => ({ ...prev, [slug]: value }));
    setSaved(false);
    setErr("");
  }

  async function handleSave() {
    const payload: Record<string, boolean> = {};
    for (const [slug, override] of Object.entries(overrides)) {
      if (override === "grant") payload[slug] = true;
      if (override === "deny")  payload[slug] = false;
      // "default" → omit from payload (backend removes override)
    }
    try {
      await updatePerms.mutateAsync({ overrides: payload });
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (e) {
      setErr((e as Error)?.message ?? "Помилка збереження");
    }
  }

  async function handleResetAll() {
    if (!confirm("Скинути всі індивідуальні доступи? Буде застосовано стандартні доступи за роллю.")) return;
    try {
      await updatePerms.mutateAsync({ overrides: {} });
      setOverrides(Object.fromEntries(PAGES.map((p) => [p.slug, "default"])));
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (e) {
      setErr((e as Error)?.message ?? "Помилка скидання");
    }
  }

  const hasAnyOverride = Object.values(overrides).some((v) => v !== "default");

  return (
    <div>
      {/* Header */}
      <div style={{ marginBottom: 16 }}>
        <div style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
          Доступи до сторінок
        </div>
        <div style={{ color: "#4B5563", fontSize: 12 }}>
          {canEdit
            ? "Виберіть індивідуальний доступ. «За замовчуванням» — стандартний доступ за роллю."
            : "Перегляд доступів. Для редагування потрібна вища роль."}
        </div>
      </div>

      {/* Pages list */}
      <div style={{ display: "flex", flexDirection: "column", gap: 6, marginBottom: 16 }}>
        {PAGES.map((page) => {
          const roleDefault = roleHasPageAccess(user.role, page.slug);
          const override    = overrides[page.slug] ?? "default";
          const tempGrant   = activeGrantFor(page.slug);
          const pickerOpen  = tempPickerSlug === page.slug;

          return (
            <div
              key={page.slug}
              style={{
                display: "flex",
                flexDirection: "column",
                gap: 8,
                padding: "10px 12px",
                borderRadius: 8,
                background: tempGrant ? "#1A140F" : override !== "default" ? "#0F1A2E" : "#0D1117",
                border: `1px solid ${tempGrant ? "#78350F" : override === "grant" ? "#1E3A5F" : override === "deny" ? "#3B1A1A" : "#1F2937"}`,
              }}
            >
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                {/* Page info */}
                <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0, flexWrap: "wrap" }}>
                  <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                    {page.label}
                  </div>
                  <div
                    style={{
                      fontSize: 10,
                      padding: "1px 7px",
                      borderRadius: 4,
                      background: roleDefault ? "#052e16" : "#1c0a0a",
                      border: `1px solid ${roleDefault ? "#14532D" : "#450a0a"}`,
                      color: roleDefault ? "#4ADE80" : "#F87171",
                      fontWeight: 600,
                      whiteSpace: "nowrap",
                    }}
                  >
                    {roleDefault ? "Є за роллю" : "Нема за роллю"}
                  </div>
                  {tempGrant && (
                    <div
                      title={`Надав: ${tempGrant.grantedByName ?? "—"}`}
                      style={{
                        fontSize: 10,
                        padding: "1px 7px",
                        borderRadius: 4,
                        background: "#3a2408",
                        border: "1px solid #78350F",
                        color: "#FBBF24",
                        fontWeight: 600,
                        whiteSpace: "nowrap",
                      }}
                    >
                      ⏳ Тимчасово до {formatGrantDate(tempGrant.expiresAt)}
                    </div>
                  )}
                </div>

                {/* Override buttons */}
                {canEdit ? (
                  <div style={{ display: "flex", gap: 4, flexShrink: 0 }}>
                    {(["grant", "default", "deny"] as const).map((val) => {
                      const labels = { grant: "✓", default: "—", deny: "✕" };
                      const active = override === val;
                      const colors: Record<Override, { bg: string; border: string; color: string }> = {
                        grant:   { bg: "#052e16", border: "#166534", color: "#4ADE80" },
                        default: { bg: "#111827", border: "#374151", color: "#6B7280" },
                        deny:    { bg: "#2d0a0a", border: "#7F1D1D", color: "#F87171" },
                      };
                      const c = colors[val];
                      return (
                        <button
                          key={val}
                          onClick={() => setOverride(page.slug, val)}
                          title={val === "grant" ? "Надати доступ" : val === "deny" ? "Заборонити" : "За замовчуванням"}
                          style={{
                            width: 28, height: 28,
                            display: "flex", alignItems: "center", justifyContent: "center",
                            borderRadius: 6,
                            background: active ? c.bg : "transparent",
                            border: `1px solid ${active ? c.border : "#1F2937"}`,
                            color: active ? c.color : "#374151",
                            fontSize: 12,
                            fontWeight: 700,
                            cursor: "pointer",
                            transition: "all 0.1s",
                          }}
                        >
                          {labels[val]}
                        </button>
                      );
                    })}
                    <button
                      onClick={() => (pickerOpen ? closeTempPicker() : openTempPicker(page.slug))}
                      disabled={Boolean(tempGrant)}
                      title={tempGrant ? "Вже надано тимчасовий доступ" : "Надати тимчасовий доступ"}
                      style={{
                        height: 28,
                        padding: "0 8px",
                        display: "flex", alignItems: "center", justifyContent: "center", gap: 4,
                        borderRadius: 6,
                        background: pickerOpen ? "#3a2408" : "transparent",
                        border: `1px solid ${pickerOpen ? "#78350F" : "#1F2937"}`,
                        color: tempGrant ? "#374151" : pickerOpen ? "#FBBF24" : "#9CA3AF",
                        fontSize: 11,
                        fontWeight: 700,
                        cursor: tempGrant ? "not-allowed" : "pointer",
                        opacity: tempGrant ? 0.5 : 1,
                        whiteSpace: "nowrap",
                      }}
                    >
                      ⏱ Тимчасово
                    </button>
                  </div>
                ) : (
                  /* Read-only: show effective access */
                  <div style={{
                    fontSize: 12,
                    fontWeight: 600,
                    color: override === "grant" ? "#4ADE80"
                         : override === "deny"  ? "#F87171"
                         : "#4B5563",
                  }}>
                    {override === "grant" ? "Надано" : override === "deny" ? "Заборонено" : "За роллю"}
                  </div>
                )}
              </div>

              {/* Inline temporary-grant date picker */}
              {canEdit && pickerOpen && (
                <div style={{ display: "flex", alignItems: "flex-end", gap: 8, flexWrap: "wrap", paddingTop: 4, borderTop: "1px solid #1F2937" }}>
                  <div>
                    <label style={{ display: "block", color: "#6B7280", fontSize: 10, marginBottom: 3 }}>
                      Діє до (макс. {MAX_GRANT_DURATION_DAYS} днів)
                    </label>
                    <input
                      type="datetime-local"
                      value={tempExpiresAt}
                      min={minExpiresAt}
                      max={maxExpiresAt}
                      onChange={(e) => setTempExpiresAt(e.target.value)}
                      style={{
                        background: "#0D1117",
                        border: "1px solid #374151",
                        borderRadius: 6,
                        color: "#E8EDF5",
                        fontSize: 12,
                        padding: "5px 8px",
                        outline: "none",
                      }}
                    />
                  </div>
                  <Btn size="sm" onClick={() => handleGrantTemp(page.slug)} disabled={grantTemp.isPending}>
                    {grantTemp.isPending ? "Надання…" : "Надати"}
                  </Btn>
                  <Btn size="sm" variant="ghost" onClick={closeTempPicker}>
                    Скасувати
                  </Btn>
                  {tempErr && (
                    <div style={{ color: "#F87171", fontSize: 11, width: "100%" }}>{tempErr}</div>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Active temporary grants */}
      {activeGrants.length > 0 && (
        <div style={{ marginBottom: 16 }}>
          <div style={{ color: "#9CA3AF", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 8 }}>
            Активні тимчасові доступи
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {activeGrants.map((grant) => {
              const pageLabel = PAGES.find((p) => p.slug === grant.permissionKey)?.label ?? grant.permissionKey;
              return (
                <div
                  key={grant.id}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    gap: 10,
                    padding: "8px 12px",
                    borderRadius: 8,
                    background: "#1A140F",
                    border: "1px solid #78350F",
                  }}
                >
                  <div style={{ minWidth: 0 }}>
                    <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 500 }}>
                      {pageLabel} — до {formatGrantDate(grant.expiresAt)}
                    </div>
                    <div style={{ color: "#6B7280", fontSize: 11, marginTop: 2 }}>
                      Надав: {grant.grantedByName ?? "—"} · {formatGrantDate(grant.grantedAt)}
                    </div>
                  </div>
                  {canEdit && (
                    <button
                      onClick={() => handleRevokeTemp(grant.id)}
                      disabled={revokeTemp.isPending}
                      style={{
                        flexShrink: 0,
                        padding: "5px 10px",
                        borderRadius: 6,
                        background: "transparent",
                        border: "1px solid #7f1d1d",
                        color: "#F87171",
                        fontSize: 11,
                        fontWeight: 600,
                        cursor: "pointer",
                      }}
                    >
                      Відкликати
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Legend */}
      {canEdit && (
        <div style={{ display: "flex", gap: 12, marginBottom: 16, flexWrap: "wrap" }}>
          {[
            { sym: "✓", label: "Надати", color: "#4ADE80" },
            { sym: "—", label: "За замовчуванням", color: "#6B7280" },
            { sym: "✕", label: "Заборонити", color: "#F87171" },
            { sym: "⏱", label: "Тимчасово", color: "#FBBF24" },
          ].map((l) => (
            <div key={l.sym} style={{ display: "flex", alignItems: "center", gap: 4 }}>
              <span style={{ color: l.color, fontSize: 11, fontWeight: 700 }}>{l.sym}</span>
              <span style={{ color: "#4B5563", fontSize: 11 }}>{l.label}</span>
            </div>
          ))}
        </div>
      )}

      {/* Error */}
      {err && (
        <div style={{ color: "#F87171", fontSize: 12, marginBottom: 10 }}>{err}</div>
      )}

      {/* Actions */}
      {canEdit && (
        <div style={{ display: "flex", gap: 10 }}>
          <Btn onClick={handleSave} disabled={updatePerms.isPending} style={{ flex: 1, justifyContent: "center" }}>
            {updatePerms.isPending ? "Збереження…" : saved ? "✓ Збережено" : "Зберегти зміни"}
          </Btn>

          {hasAnyOverride && (
            <Btn variant="ghost" onClick={handleResetAll} disabled={updatePerms.isPending}>
              Скинути всі
            </Btn>
          )}
        </div>
      )}
    </div>
  );
}
