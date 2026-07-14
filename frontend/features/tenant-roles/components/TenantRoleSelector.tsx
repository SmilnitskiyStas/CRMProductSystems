"use client";

import { useState } from "react";
import { useTenantRoles, useAssignTenantRole } from "../hooks/useTenantRoles";
import { Btn } from "@/components/ui/Btn";
import type { UserDto } from "@/features/users/types";

interface Props {
  user: UserDto;
}

/**
 * Assigns/clears a TenantRole capability template on a user (ADR-020).
 * Caller must gate visibility to enterprise_admin+ — this component does not check
 * roles itself, since POST /api/users/:id/tenant-role is AtLeastEnterpriseAdmin-only
 * with no capability bypass (anti-escalation).
 */
export function TenantRoleSelector({ user }: Props) {
  const { data: roles, isLoading } = useTenantRoles(); // active only — archived templates aren't assignable
  const assign = useAssignTenantRole();

  const [value, setValue] = useState(user.tenantRoleId ?? "");
  const [saved, setSaved] = useState(false);
  const [err, setErr] = useState("");

  const dirty = value !== (user.tenantRoleId ?? "");

  async function handleSave() {
    setErr("");
    try {
      await assign.mutateAsync({ userId: user.id, tenantRoleId: value || null });
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
    } catch (e) {
      setErr((e as Error)?.message ?? "Помилка призначення шаблону");
    }
  }

  return (
    <div
      style={{
        marginBottom: 20,
        padding: "12px 14px",
        borderRadius: 8,
        background: "#0D1117",
        border: "1px solid #1F2937",
      }}
    >
      <div
        style={{
          color: "#9CA3AF", fontSize: 11, fontWeight: 600,
          textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 8,
        }}
      >
        Шаблон ролі (додаткові права)
      </div>

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
        <select
          value={value}
          onChange={(e) => { setValue(e.target.value); setSaved(false); }}
          disabled={isLoading}
          style={{
            flex: 1, minWidth: 180,
            background: "#111827", border: "1px solid #374151", borderRadius: 8,
            padding: "8px 10px", color: "#E8EDF5", fontSize: 13,
            outline: "none", cursor: "pointer", appearance: "none",
          }}
        >
          <option value="" style={{ background: "#111827" }}>— Без шаблону —</option>
          {roles?.map((r) => (
            <option key={r.id} value={r.id} style={{ background: "#111827" }}>
              {r.name}
            </option>
          ))}
        </select>
        <Btn size="sm" onClick={handleSave} disabled={!dirty || assign.isPending}>
          {assign.isPending ? "Збереження…" : saved ? "✓ Збережено" : "Призначити"}
        </Btn>
      </div>

      {roles && roles.length === 0 && (
        <div style={{ color: "#374151", fontSize: 12, marginTop: 6 }}>
          Активних шаблонів ще немає — створіть їх на вкладці «Шаблони ролей».
        </div>
      )}

      {err && <div style={{ color: "#F87171", fontSize: 12, marginTop: 6 }}>{err}</div>}
    </div>
  );
}
