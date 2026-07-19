"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useInviteUser } from "../hooks/useUsers";
import { useSetUserLocations } from "../hooks/useUserLocations";
import { getRoleLabel, ROLE_KEYS } from "@/features/profile/types";
import { ROLE_RANK, isSingleLocationRole } from "../types";
import { Btn } from "@/components/ui/Btn";
import { useLegalEntities } from "@/features/legal-entities/hooks/useLegalEntities";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useTenantRoles, useAssignTenantRole } from "@/features/tenant-roles/hooks/useTenantRoles";
import { useMe } from "@/features/auth/hooks/useAuth";
import { hasRole, AT_LEAST_ENTERPRISE_ADMIN } from "@/lib/roles";

/**
 * Preferred default role when the inviter's rank allows it (TASK-392c) — keeps the previous
 * default (store_manager was INVITE_ROLES[0]) instead of always defaulting to the highest
 * rank the inviter happens to hold, which would be a poor/risky default for e.g.
 * enterprise_admin. Falls back to whatever IS invitable when it isn't (e.g. a rank-0
 * capability-only inviter, who can only ever invite "staff").
 */
const PREFERRED_DEFAULT_ROLE = "store_manager";

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
  const t = useTranslations("Dashboard.users.inviteModal");
  const tRoles = useTranslations("Dashboard.roles");
  const invite = useInviteUser();
  const setUserLocations = useSetUserLocations();
  const { data: legalEntities } = useLegalEntities();
  const activeLegalEntities = (legalEntities ?? []).filter((e) => e.isActive);
  const { data: locations } = useLocations();
  const activeLocations = (locations ?? []).filter((l) => l.isActive);

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

  // Dynamic, rank-filtered invite role list (TASK-392c) — replaces the previous hardcoded
  // INVITE_ROLES array, which silently capped every inviter at "store_manager and below"
  // no matter their own rank (an enterprise_admin could never invite another enterprise_admin
  // or a network_manager from this modal). Mirrors the backend's own gate exactly
  // (UserService.InviteAsync: requestedRank > actingRank is rejected, so "<=" is allowed).
  // "provider" is excluded — it has no tenant context and is never a role you invite a
  // teammate into.
  const myRank = ROLE_RANK[me?.role ?? ""] ?? 0;
  const invitableRoles = ROLE_KEYS.filter((r) => r !== "provider" && (ROLE_RANK[r] ?? 0) <= myRank);

  const [email,        setEmail]        = useState("");
  const [fullName,     setFullName]     = useState("");
  const [role,         setRole]         = useState<string>(
    invitableRoles.includes(PREFERRED_DEFAULT_ROLE) ? PREFERRED_DEFAULT_ROLE : invitableRoles[0] ?? "staff",
  );
  const [roleTouched,  setRoleTouched]  = useState(false);
  const [password,     setPassword]     = useState("");
  const [legalEntityId, setLegalEntityId] = useState("");
  const [tenantRoleId, setTenantRoleId]  = useState("");
  // Store-scoped assignment (TASK-392c, Feature 2 Stage 1): manualStoreId is the fallback
  // single-store picker, only shown/used when the inviter has no home store of their own
  // (me.storeId null) to silently auto-assign; territoryIds is the network_manager
  // multi-select, applied via a separate PUT after the invite succeeds.
  const [manualStoreId, setManualStoreId] = useState("");
  const [territoryIds, setTerritoryIds]   = useState<string[]>([]);
  const [errors,       setErrors]       = useState<Record<string, string>>({});

  // Set once the invite call succeeds — flips the primary button into a "close" action so
  // a template-assignment failure below can never lead to a duplicate invite attempt.
  const [createdUserId, setCreatedUserId] = useState<string | null>(null);
  const [partialError,  setPartialError]  = useState<string | null>(null);

  const singleLocationRole = isSingleLocationRole(role);
  const needsStorePicker = singleLocationRole && !me?.storeId;
  const effectiveStoreId = singleLocationRole ? me?.storeId ?? (manualStoreId || null) : null;

  function toggleTerritory(id: string) {
    setTerritoryIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function validate() {
    const e: Record<string, string> = {};
    if (!email.trim() || !email.includes("@")) e.email    = t("emailInvalidError");
    if (!fullName.trim())                       e.fullName = t("fullNameRequiredError");
    if (password.length < 8)                    e.password = t("passwordMinError");
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
      storeId: effectiveStoreId,
      legalEntityId: legalEntityId || null,
    });

    // network_manager territory (TASK-392c): a second call, same "already created, never
    // retry the invite" discipline as the TenantRole assignment below. Skipped entirely when
    // the admin didn't pick any store — an empty PUT would be a no-op anyway (a fresh user
    // has no user_locations rows yet).
    if (role === "network_manager" && territoryIds.length > 0) {
      try {
        await setUserLocations.mutateAsync({ userId: newUser.id, locationIds: territoryIds });
      } catch (err) {
        setCreatedUserId(newUser.id);
        const reason = ((err as Error)?.message ?? t("createErrorFallback")).replace(/\.+$/, "");
        setPartialError(t("partialErrorMessageLocations", { reason }));
        return;
      }
    }

    if (tenantRoleId) {
      try {
        await assignTenantRole.mutateAsync({ userId: newUser.id, tenantRoleId });
      } catch (err) {
        // The user was created successfully — that must stay visible and must not be
        // rolled back. Only the template assignment failed; let the admin finish that
        // part manually via TenantRoleSelector on the user's profile.
        setCreatedUserId(newUser.id);
        // Strip a trailing period from the server message — it already ends this
        // sentence, avoiding a "...archived TenantRole.. Assign it..." double-stop.
        const reason = ((err as Error)?.message ?? t("createErrorFallback")).replace(/\.+$/, "");
        setPartialError(t("partialErrorMessage", { reason }));
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
              {t("modalTitle")}
            </h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: "3px 0 0" }}>
              {t("modalSubtitle")}
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
              <label style={labelStyle}>{t("fullNameLabel")}</label>
              <input
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder={t("fullNamePlaceholder")}
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, borderColor: errors.fullName ? "#EF4444" : "#374151" }}
              />
              {errors.fullName && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.fullName}</p>
              )}
            </div>

            {/* Email */}
            <div>
              <label style={labelStyle}>{t("emailLabel")}</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder={t("emailPlaceholder")}
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, borderColor: errors.email ? "#EF4444" : "#374151" }}
              />
              {errors.email && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.email}</p>
              )}
            </div>

            {/* Role */}
            <div>
              <label style={labelStyle}>{t("roleLabel")}</label>
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
                {invitableRoles.map((r) => (
                  <option key={r} value={r} style={{ background: "#0D1117" }}>
                    {getRoleLabel(tRoles, r)}
                  </option>
                ))}
              </select>
            </div>

            {/* Store (single) — fallback picker, only when the inviter has no home store of
                their own to silently auto-assign (TASK-392c). Roles above store_manager
                (network_manager/enterprise_admin) never show this — see the two blocks below. */}
            {needsStorePicker && (
              <div>
                <label style={labelStyle}>{t("storeLabel")}</label>
                <select
                  value={manualStoreId}
                  onChange={(e) => setManualStoreId(e.target.value)}
                  disabled={Boolean(createdUserId)}
                  style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
                >
                  <option value="" style={{ background: "#0D1117" }}>{t("storeNoneOption")}</option>
                  {activeLocations.map((loc) => (
                    <option key={loc.id} value={loc.id} style={{ background: "#0D1117" }}>
                      {loc.name}
                    </option>
                  ))}
                </select>
              </div>
            )}

            {/* Territory (multi) — network_manager only (TASK-392c). Applied via a separate
                PUT /api/users/:id/locations right after the invite succeeds (useSetUserLocations). */}
            {role === "network_manager" && (
              <div>
                <label style={labelStyle}>{t("territoryLabel")}</label>
                {activeLocations.length === 0 ? (
                  <p style={{ color: "#4B5563", fontSize: 11 }}>{t("territoryEmptyHint")}</p>
                ) : (
                  <div
                    style={{
                      display: "flex", flexDirection: "column", gap: 6,
                      maxHeight: 160, overflowY: "auto",
                      background: "#0D1117", border: "1px solid #374151",
                      borderRadius: 8, padding: 10,
                    }}
                  >
                    {activeLocations.map((loc) => (
                      <label
                        key={loc.id}
                        style={{
                          display: "flex", alignItems: "center", gap: 8,
                          fontSize: 13, color: "#E8EDF5", cursor: "pointer",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={territoryIds.includes(loc.id)}
                          onChange={() => toggleTerritory(loc.id)}
                          disabled={Boolean(createdUserId)}
                        />
                        {loc.name}
                      </label>
                    ))}
                  </div>
                )}
                <p style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>
                  {t("territoryHint")}
                </p>
              </div>
            )}

            {/* TenantRole template (ADR-020) — optional, assigned via a second API call
                right after invite succeeds; independent from the base Role above. */}
            {canManageTenantRole && (
              <div>
                <label style={labelStyle}>{t("tenantRoleLabel")}</label>
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
                  <option value="" style={{ background: "#0D1117" }}>{t("tenantRoleNoneOption")}</option>
                  {activeTenantRoles.map((tr) => (
                    <option key={tr.id} value={tr.id} style={{ background: "#0D1117" }}>
                      {tr.name}
                    </option>
                  ))}
                </select>
                <p style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>
                  {t("tenantRoleHint")}
                </p>
                {!tenantRolesLoading && activeTenantRoles.length === 0 && (
                  <p style={{ color: "#374151", fontSize: 11, marginTop: 4 }}>
                    {t("tenantRoleNoneAvailable")}
                  </p>
                )}
              </div>
            )}

            {/* Legal entity */}
            <div>
              <label style={labelStyle}>{t("legalEntityLabel")}</label>
              <select
                value={legalEntityId}
                onChange={(e) => setLegalEntityId(e.target.value)}
                disabled={Boolean(createdUserId)}
                style={{ ...inputStyle, appearance: "none", cursor: "pointer" }}
              >
                <option value="" style={{ background: "#0D1117" }}>{t("legalEntityNoneOption")}</option>
                {activeLegalEntities.map((entity) => (
                  <option key={entity.id} value={entity.id} style={{ background: "#0D1117" }}>
                    {entity.legalName}
                  </option>
                ))}
              </select>
            </div>

            {/* Temporary password */}
            <div>
              <label style={labelStyle}>{t("passwordLabel")}</label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder={t("passwordPlaceholder")}
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
              disabled={invite.isPending || setUserLocations.isPending || assignTenantRole.isPending}
              style={{ flex: 1, justifyContent: "center" }}
            >
              {createdUserId
                ? t("closeButton")
                : invite.isPending
                ? t("creatingButton")
                : setUserLocations.isPending
                ? t("assigningLocationsButton")
                : assignTenantRole.isPending
                ? t("assigningButton")
                : t("submitButton")}
            </Btn>
            {!createdUserId && (
              <Btn type="button" variant="ghost" onClick={onClose}>
                {t("cancelButton")}
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
              {(invite.error as Error)?.message ?? t("createErrorFallback")}
            </p>
          )}
        </form>
      </div>
    </>
  );
}
