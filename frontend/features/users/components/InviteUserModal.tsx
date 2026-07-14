"use client";

import { useState } from "react";
import { useInviteUser } from "../hooks/useUsers";
import { ROLE_LABELS } from "@/features/profile/types";
import { Btn } from "@/components/ui/Btn";
import { useLegalEntities } from "@/features/legal-entities/hooks/useLegalEntities";
import { useTenantRoles, useAssignTenantRole } from "@/features/tenant-roles/hooks/useTenantRoles";
import { useMe } from "@/features/auth/hooks/useAuth";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";

// "staff" (ADR-020, v4.5) — minimal base tier, rank 0, grants nothing by itself.
// Added last: it's the newest and lowest-ranked role, kept out of the way of the
// existing operational roles admins pick most often.
const INVITE_ROLES = [
  "store_manager",
  "merchandiser",
  "storekeeper",
  "cashier",
  "staff",
] as const;

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

interface Props {
  onClose: () => void;
}

export function InviteUserModal({ onClose }: Props) {
  const invite = useInviteUser();
  const { data: legalEntities } = useLegalEntities();
  const activeLegalEntities = (legalEntities ?? []).filter((e) => e.isActive);

  // TenantRole template (ADR-020) — assigned via a second, separate call right after the
  // invite succeeds. This modal only ever renders for enterprise_admin+ (gated in
  // app/(dashboard)/users/page.tsx), but the check is repeated here defensively, mirroring
  // TenantRoleSelector/TenantRoleBadge — POST /api/users/:id/tenant-role is
  // AtLeastEnterpriseAdmin-only with no capability bypass.
  const { data: me } = useMe();
  const canManageTenantRole = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);
  const { data: tenantRoles, isLoading: tenantRolesLoading } = useTenantRoles(false, canManageTenantRole);
  const activeTenantRoles = tenantRoles ?? [];
  const assignTenantRole = useAssignTenantRole();

  const [email,        setEmail]        = useState("");
  const [fullName,     setFullName]     = useState("");
  const [role,         setRole]         = useState<string>(INVITE_ROLES[0]);
  const [roleTouched,  setRoleTouched]  = useState(false);
  const [password,     setPassword]     = useState("");
  const [legalEntityId, setLegalEntityId] = useState("");
  const [tenantRoleId, setTenantRoleId]  = useState("");
  const [errors,       setErrors]       = useState<Record<string, string>>({});

  // Set once the invite call succeeds — flips the primary button into a "close" action so
  // a template-assignment failure below can never lead to a duplicate invite attempt.
  const [createdUserId, setCreatedUserId] = useState<string | null>(null);
  const [partialError,  setPartialError]  = useState<string | null>(null);

  function validate() {
    const e: Record<string, string> = {};
    if (!email.trim() || !email.includes("@")) e.email    = "Введіть коректний email";
    if (!fullName.trim())                       e.fullName = "Введіть ім'я";
    if (password.length < 8)                    e.password = "Мінімум 8 символів";
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();

    // Phase 2: the user was already created but template assignment failed below —
    // the form's only job now is to close, never to re-submit the same invite.
    if (createdUserId) {
      onClose();
      return;
    }

    if (!validate()) return;

    const newUser = await invite.mutateAsync({
      email:    email.trim().toLowerCase(),
      fullName: fullName.trim(),
      role,
      password,
      legalEntityId: legalEntityId || null,
    });

    if (tenantRoleId) {
      try {
        await assignTenantRole.mutateAsync({ userId: newUser.id, tenantRoleId });
      } catch (err) {
        // The user was created successfully — that must stay visible and must not be
        // rolled back. Only the template assignment failed; let the admin finish that
        // part manually via TenantRoleSelector on the user's profile.
        setCreatedUserId(newUser.id);
        // Strip a trailing period from the server message — it already ends this
        // sentence, avoiding a "...archived TenantRole.. Призначте..." double-stop.
        const reason = ((err as Error)?.message ?? "Помилка").replace(/\.+$/, "");
        setPartialError(
          `Користувача створено, але не вдалося призначити шаблон ролі: ${reason}. Призначте вручну в профілі користувача.`,
        );
        return;
      }
    }

    onClose();
  }

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 300,
          backdropFilter: "blur(2px)",
        }}
      />

      {/* Modal */}
      <div
        style={{
          position: "fixed",
          top: "50%", left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(480px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 301,
          display: "flex",
          flexDirection: "column",
        }}
      >
        {/* Header */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: "18px 22px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
              Запросити користувача
            </h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: "3px 0 0" }}>
              Новий акаунт буде створено у вашому тенанті
            </p>
          </div>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937",
              borderRadius: 8, padding: "5px 9px",
              color: "#4B5563", fontSize: 16, cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} style={{ padding: 22 }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {/* Full name */}
            <div>
              <label style={labelStyle}>Повне ім'я *</label>
              <input
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder="Іван Петренко"
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, borderColor: errors.fullName ? "#EF4444" : "#374151" }}
              />
              {errors.fullName && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.fullName}</p>
              )}
            </div>

            {/* Email */}
            <div>
              <label style={labelStyle}>Email *</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="ivan@example.com"
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, borderColor: errors.email ? "#EF4444" : "#374151" }}
              />
              {errors.email && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.email}</p>
              )}
            </div>

            {/* Role */}
            <div>
              <label style={labelStyle}>Роль *</label>
              <select
                value={role}
                onChange={(e) => { setRole(e.target.value); setRoleTouched(true); }}
                disabled={Boolean(createdUserId)}
                style={{
                  ...inputStyle,
                  appearance: "none",
                  cursor: "pointer",
                }}
              >
                {INVITE_ROLES.map((r) => (
                  <option key={r} value={r} style={{ background: "#0D1117" }}>
                    {ROLE_LABELS[r] ?? r}
                  </option>
                ))}
              </select>
            </div>

            {/* TenantRole template (ADR-020) — optional, assigned via a second API call
                right after invite succeeds; independent from the base Role above. */}
            {canManageTenantRole && (
              <div>
                <label style={labelStyle}>Шаблон ролі (необов'язково)</label>
                <select
                  value={tenantRoleId}
                  onChange={(e) => {
                    const next = e.target.value;
                    setTenantRoleId(next);
                    // Nudge the base role to the "no default access" tier when a template
                    // is picked and the admin hasn't touched Role themselves — the two
                    // fields stay independent either way (ADR-020), this only sets a more
                    // sensible starting point for the common "template-only" hire.
                    if (next && !roleTouched) setRole("staff");
                  }}
                  disabled={tenantRolesLoading || Boolean(createdUserId)}
                  style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
                >
                  <option value="" style={{ background: "#0D1117" }}>— Без шаблону —</option>
                  {activeTenantRoles.map((tr) => (
                    <option key={tr.id} value={tr.id} style={{ background: "#0D1117" }}>
                      {tr.name}
                    </option>
                  ))}
                </select>
                <p style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>
                  Додає розширені можливості поверх базової ролі — керується в розділі
                  «Шаблони ролей».
                </p>
                {!tenantRolesLoading && activeTenantRoles.length === 0 && (
                  <p style={{ color: "#374151", fontSize: 11, marginTop: 4 }}>
                    Активних шаблонів ще немає.
                  </p>
                )}
              </div>
            )}

            {/* Legal entity */}
            <div>
              <label style={labelStyle}>Юридична особа (необов'язково)</label>
              <select
                value={legalEntityId}
                onChange={(e) => setLegalEntityId(e.target.value)}
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
              >
                <option value="" style={{ background: "#0D1117" }}>— Не вказано —</option>
                {activeLegalEntities.map((entity) => (
                  <option key={entity.id} value={entity.id} style={{ background: "#0D1117" }}>
                    {entity.legalName}
                  </option>
                ))}
              </select>
            </div>

            {/* Temporary password */}
            <div>
              <label style={labelStyle}>Тимчасовий пароль *</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Мінімум 8 символів"
                autoComplete="new-password"
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, borderColor: errors.password ? "#EF4444" : "#374151" }}
              />
              {errors.password && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.password}</p>
              )}
            </div>
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, marginTop: 22 }}>
            <Btn
              type="submit"
              disabled={invite.isPending || assignTenantRole.isPending}
              style={{ flex: 1, justifyContent: "center" }}
            >
              {createdUserId
                ? "Закрити"
                : invite.isPending
                ? "Створення…"
                : assignTenantRole.isPending
                ? "Призначення шаблону…"
                : "Запросити"}
            </Btn>
            {!createdUserId && (
              <Btn type="button" variant="ghost" onClick={onClose}>
                Скасувати
              </Btn>
            )}
          </div>

          {partialError && (
            <p
              style={{
                color: "#FBBF24",
                fontSize: 12,
                marginTop: 10,
                padding: "8px 10px",
                background: "#1F170A",
                border: "1px solid #78350F",
                borderRadius: 8,
              }}
            >
              {partialError}
            </p>
          )}

          {invite.isError && !createdUserId && (
            <p style={{ color: "#F87171", fontSize: 12, marginTop: 10 }}>
              {(invite.error as Error)?.message ?? "Помилка створення"}
            </p>
          )}
        </form>
      </div>
    </>
  );
}
