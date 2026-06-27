"use client";

import { useState } from "react";
import { useUpdatePermissions } from "../hooks/useUsers";
import { PAGES, ROLE_RANK, roleHasPageAccess } from "../types";
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

          return (
            <div
              key={page.slug}
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                padding: "10px 12px",
                borderRadius: 8,
                background: override !== "default" ? "#0F1A2E" : "#0D1117",
                border: `1px solid ${override === "grant" ? "#1E3A5F" : override === "deny" ? "#3B1A1A" : "#1F2937"}`,
              }}
            >
              {/* Page info */}
              <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
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
          );
        })}
      </div>

      {/* Legend */}
      {canEdit && (
        <div style={{ display: "flex", gap: 12, marginBottom: 16 }}>
          {[
            { sym: "✓", label: "Надати", color: "#4ADE80" },
            { sym: "—", label: "За замовчуванням", color: "#6B7280" },
            { sym: "✕", label: "Заборонити", color: "#F87171" },
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
