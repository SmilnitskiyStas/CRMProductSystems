"use client";

import { useState, useMemo } from "react";
import { useTranslations } from "next-intl";
import { useInviteProviderMember } from "../hooks/useProviderTeam";
import { useProviderRoles } from "../hooks/useProviderRoles";
import {
  ALL_PERMISSIONS,
  SYSTEM_ROLE_PERMISSIONS,
  resolvePermissions,
} from "@/lib/providerPermissions";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle } from "@/components/ui/fieldStyles";

interface Props {
  onClose: () => void;
}

export function InviteProviderMemberModal({ onClose }: Props) {
  const t = useTranslations("Dashboard.provider.inviteMemberModal");
  const tPermissions = useTranslations("Dashboard.provider.permissions");
  const tRoleSelector = useTranslations("Dashboard.provider.roleSelector");
  const invite = useInviteProviderMember();
  const { data: customRoles } = useProviderRoles();

  const [form, setForm] = useState({
    email: "",
    fullName: "",
    selectedRoleId: "__agent",   // "__admin" | "__agent" | custom role UUID
    password: "",
    confirmPassword: "",
  });

  // Per-user permission overrides on top of role defaults
  const [permOverride, setPermOverride] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);

  const isSystemRole = form.selectedRoleId.startsWith("__");

  // Resolve effective base permissions from selected role
  const basePermissions = useMemo(() => {
    if (form.selectedRoleId === "__admin") return SYSTEM_ROLE_PERMISSIONS["provider_admin"] ?? [];
    if (form.selectedRoleId === "__agent") return SYSTEM_ROLE_PERMISSIONS["provider_agent"] ?? [];
    const custom = customRoles?.find((r) => r.id === form.selectedRoleId);
    return custom?.permissions ?? [];
  }, [form.selectedRoleId, customRoles]);

  // Derive system role slug for the API
  const systemRole = useMemo(() => {
    if (form.selectedRoleId === "__admin") return "provider_admin";
    const custom = customRoles?.find((r) => r.id === form.selectedRoleId);
    return custom?.baseRole ?? "provider_agent";
  }, [form.selectedRoleId, customRoles]);

  const effectivePermissions = useMemo(
    () => resolvePermissions(basePermissions, permOverride),
    [basePermissions, permOverride]
  );

  // A permission is "from role" if it's in base, "from override" if added manually
  function getPermState(p: string): "role" | "override-add" | "override-remove" | "none" {
    const inBase = basePermissions.includes(p);
    const inEffective = effectivePermissions.includes(p);
    if (permOverride[p] === true)  return "override-add";
    if (permOverride[p] === false) return "override-remove";
    if (inBase) return "role";
    return "none";
  }

  function togglePerm(p: string) {
    const inBase  = basePermissions.includes(p);
    const current = permOverride[p];

    setPermOverride((prev) => {
      const next = { ...prev };
      if (inBase) {
        // toggle remove-override
        if (current === false) delete next[p];   // restore to role default
        else next[p] = false;                    // mark as removed
      } else {
        // toggle add-override
        if (current === true) delete next[p];    // restore to role default (no access)
        else next[p] = true;                     // grant manually
      }
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (form.password.length < 6) {
      setError(t("errorPasswordMinLength"));
      return;
    }
    if (form.password !== form.confirmPassword) {
      setError(t("errorPasswordMismatch"));
      return;
    }

    const overridePayload = Object.keys(permOverride).length > 0 ? permOverride : null;

    try {
      await invite.mutateAsync({
        email:             form.email,
        fullName:          form.fullName,
        role:              systemRole,
        password:          form.password,
        providerRoleId:    isSystemRole ? null : form.selectedRoleId,
        permissionsOverride: overridePayload,
      });
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? t("errorCreateDefault"));
    }
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={500}>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input required type="text" placeholder={t("fullNamePlaceholder")} value={form.fullName}
            onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))} style={inputStyle} />
          <input required type="email" placeholder={t("emailPlaceholder")} value={form.email}
            onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))} style={inputStyle} />

          {/* Role selector */}
          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 6 }}>{t("roleLabel")}</div>
            <select
              value={form.selectedRoleId}
              onChange={(e) => {
                setForm((f) => ({ ...f, selectedRoleId: e.target.value }));
                setPermOverride({}); // reset overrides on role change
              }}
              style={{ ...inputStyle, appearance: "none" }}
            >
              <optgroup label={tRoleSelector("systemRolesGroup")}>
                <option value="__admin">{tRoleSelector("baseRoleLabels.provider_admin")}</option>
                <option value="__agent">{tRoleSelector("baseRoleLabels.provider_agent")}</option>
              </optgroup>
              {customRoles && customRoles.filter((r) => !r.isSystem).length > 0 && (
                <optgroup label={tRoleSelector("customRolesGroup")}>
                  {customRoles.filter((r) => !r.isSystem).map((r) => (
                    <option key={r.id} value={r.id}>{r.displayName}</option>
                  ))}
                </optgroup>
              )}
            </select>
          </div>

          {/* Permissions */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 8, padding: "12px 14px" }}>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 10 }}>
              {t("permissionsLabel")}
              <span style={{ color: "#374151", marginLeft: 6 }}>
                {t("permissionsUncheckedHint")}
              </span>
            </div>
            {ALL_PERMISSIONS.map((p) => {
              const state = getPermState(p);
              const checked = effectivePermissions.includes(p);
              return (
                <label key={p} style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6, cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={() => togglePerm(p)}
                    style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                  />
                  <span style={{ color: checked ? "#E8EDF5" : "#4B5563", fontSize: 13, flex: 1 }}>
                    {tPermissions(p)}
                  </span>
                  {state === "role" && (
                    <span style={{ fontSize: 10, color: "#374151" }}>{t("fromRoleLabel")}</span>
                  )}
                  {state === "override-add" && (
                    <span style={{ fontSize: 10, color: "#4ADE80" }}>{t("overrideAddLabel")}</span>
                  )}
                  {state === "override-remove" && (
                    <span style={{ fontSize: 10, color: "#F87171" }}>{t("overrideRemoveLabel")}</span>
                  )}
                </label>
              );
            })}
          </div>

          <input required type="password" placeholder={t("passwordPlaceholder")} value={form.password}
            onChange={(e) => setForm((f) => ({ ...f, password: e.target.value }))} style={inputStyle} />
          <input required type="password" placeholder={t("confirmPasswordPlaceholder")} value={form.confirmPassword}
            onChange={(e) => setForm((f) => ({ ...f, confirmPassword: e.target.value }))} style={inputStyle} />

          {error && (
            <div style={{ color: "#F87171", fontSize: 13, padding: "8px 12px", background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D" }}>
              {error}
            </div>
          )}

          <Btn type="submit" fullWidth disabled={invite.isPending} style={{ marginTop: 4 }}>
            {invite.isPending ? t("creating") : t("submitButton")}
          </Btn>
        </form>
    </Modal>
  );
}
