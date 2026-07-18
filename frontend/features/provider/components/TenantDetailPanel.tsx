"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations, useLocale } from "next-intl";
import { X, LogIn, Save, ScrollText } from "lucide-react";
import {
  PLAN_COLORS,
  ALL_MODULES, ALL_PLANS,
} from "../types";
import type { TenantDetailDto } from "../types";
import { useTenant, useUpdatePlan, useUpdateModules, useImpersonate, useTenantUsers, useActivateTenant, useDeactivateTenant } from "../hooks/useProvider";
import { AddTenantUserModal } from "./AddTenantUserModal";
import { setToken, getToken } from "@/lib/api";
import { ME_KEY } from "@/features/auth/hooks/useAuth";
import { Btn } from "@/components/ui/Btn";

interface Props {
  tenantId: string;
  onClose: () => void;
  onImpersonated: () => void;
  onViewLogs: (tenantId: string) => void;
}

function formatDate(iso: string | null, locale: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

export function TenantDetailPanel({ tenantId, onClose, onImpersonated, onViewLogs }: Props) {
  const t = useTranslations("Dashboard.provider.tenantDetailPanel");
  const tPlans = useTranslations("Dashboard.provider.plans");
  const tModules = useTranslations("Dashboard.provider.modules");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: tenant, isLoading } = useTenant(tenantId, true);
  const updatePlan    = useUpdatePlan(tenantId);
  const updateModules = useUpdateModules(tenantId);
  const impersonate   = useImpersonate();
  const activate      = useActivateTenant(tenantId);
  const deactivate    = useDeactivateTenant(tenantId);
  const queryClient   = useQueryClient();

  const [editingPlan,    setEditingPlan]    = useState(false);
  const [editingModules, setEditingModules] = useState(false);
  const [selectedPlan,   setSelectedPlan]   = useState<string>("");
  const [selectedMods,   setSelectedMods]   = useState<string[]>([]);
  const [impersonating,  setImpersonating]  = useState(false);
  const [impersonateErr, setImpersonateErr] = useState("");
  const [showAddUser,    setShowAddUser]    = useState(false);

  const { data: tenantUsers = [], isLoading: usersLoading } = useTenantUsers(tenantId);

  function startEditPlan(t: TenantDetailDto) {
    setSelectedPlan(t.plan);
    setEditingPlan(true);
  }

  function startEditModules(t: TenantDetailDto) {
    setSelectedMods([...t.modules]);
    setEditingModules(true);
  }

  async function savePlan() {
    await updatePlan.mutateAsync(selectedPlan);
    setEditingPlan(false);
  }

  async function saveModules() {
    await updateModules.mutateAsync(selectedMods);
    setEditingModules(false);
  }

  function toggleMod(m: string) {
    setSelectedMods((prev) =>
      prev.includes(m) ? prev.filter((x) => x !== m) : [...prev, m],
    );
  }

  async function handleImpersonate() {
    setImpersonateErr("");
    setImpersonating(true);
    try {
      const resp = await impersonate.mutateAsync(tenantId);
      if (typeof window !== "undefined") {
        const original = getToken();
        if (original) sessionStorage.setItem("sg_provider_token", original);
        // Persist banner state so DashboardLayout can show it after redirect
        sessionStorage.setItem("sg_impersonation", JSON.stringify({
          tenantName: resp.tenantName,
          tenantId: resp.tenantId,
        }));
        window.dispatchEvent(new Event("sg-impersonation-changed"));
      }
      setToken(resp.accessToken);
      // Await the actual network call so the new tenant role is in cache before navigation.
      // invalidateQueries only marks stale; refetchQueries awaits the response.
      await queryClient.refetchQueries({ queryKey: ME_KEY });
      onImpersonated();
    } catch (err) {
      setImpersonateErr((err as Error)?.message ?? t("errorImpersonateDefault"));
    } finally {
      setImpersonating(false);
    }
  }

  return (
    <div
      style={{
        width: 440,
        minWidth: 440,
        flexShrink: 0,
        height: "100vh",
        position: "sticky",
        top: 0,
        background: "#080D14",
        borderLeft: "1px solid #1F2937",
        display: "flex",
        flexDirection: "column",
        overflowY: "auto",
      }}
    >
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          padding: "20px 24px",
          borderBottom: "1px solid #1F2937",
          flexShrink: 0,
        }}
      >
        <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>
          {t("title")}
        </div>
        <button
          onClick={onClose}
          style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
        >
          <X size={18} />
        </button>
      </div>

      {isLoading && (
        <div style={{ color: "#4B5563", fontSize: 13, padding: 24 }}>{t("loading")}</div>
      )}

      {!isLoading && !tenant && (
        <div style={{ color: "#4B5563", fontSize: 13, padding: 24 }}>{t("notFound")}</div>
      )}

      {showAddUser && (
        <AddTenantUserModal
          tenantId={tenantId}
          businessType={tenant?.businessType}
          onClose={() => setShowAddUser(false)}
          onCreated={() => setShowAddUser(false)}
        />
      )}

      {tenant && (
        <div style={{ padding: "20px 24px", display: "flex", flexDirection: "column", gap: 20 }}>
          {/* Name + status */}
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
              <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700 }}>{tenant.name}</div>
              <span
                style={{
                  padding: "2px 8px",
                  borderRadius: 5,
                  fontSize: 11,
                  fontWeight: 600,
                  background: tenant.isActive ? "#052e16" : "#1F1211",
                  border: `1px solid ${tenant.isActive ? "#166534" : "#7F1D1D"}`,
                  color: tenant.isActive ? "#4ADE80" : "#F87171",
                }}
              >
                {tenant.isActive ? t("statusActive") : t("statusDeactivated")}
              </span>
            </div>
            <div style={{ color: "#4B5563", fontSize: 12 }}>{tenant.slug}</div>
          </div>

          {/* Info grid */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            {[
              { label: t("statUsers"),          value: tenant.userCount },
              { label: t("statStores"),         value: tenant.storeCount },
              { label: t("statExpiredBatches"), value: tenant.expiredBatchCount, warn: tenant.expiredBatchCount > 0 },
              { label: t("statRegistered"),     value: formatDate(tenant.createdAt, intlLocale) },
              { label: t("statLastActivity"),   value: formatDate(tenant.lastActivityAt, intlLocale), span: true },
            ].map((row) => (
              <div
                key={row.label}
                style={{
                  gridColumn: row.span ? "1 / -1" : undefined,
                  background: "#0D1117",
                  border: "1px solid #1F2937",
                  borderRadius: 8,
                  padding: "10px 14px",
                }}
              >
                <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 4 }}>{row.label}</div>
                <div style={{ color: (row as { warn?: boolean }).warn ? "#F87171" : "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
                  {row.value}
                </div>
              </div>
            ))}
          </div>

          {/* Plan section */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
              <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600 }}>{t("planSectionTitle")}</div>
              {!editingPlan && (
                <button
                  onClick={() => startEditPlan(tenant)}
                  style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 12, cursor: "pointer" }}
                >
                  {t("changeButton")}
                </button>
              )}
            </div>

            {editingPlan ? (
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                  {ALL_PLANS.map((p) => {
                    const c = PLAN_COLORS[p];
                    const active = selectedPlan === p;
                    return (
                      <button
                        key={p}
                        onClick={() => setSelectedPlan(p)}
                        style={{
                          padding: "6px 14px",
                          borderRadius: 7,
                          fontSize: 12,
                          fontWeight: 600,
                          cursor: "pointer",
                          background: active ? c.bg : "#111827",
                          border: `1px solid ${active ? c.border : "#374151"}`,
                          color: active ? c.text : "#6B7280",
                        }}
                      >
                        {tPlans(p)}
                      </button>
                    );
                  })}
                </div>
                <div style={{ display: "flex", gap: 8, marginTop: 4 }}>
                  <Btn size="sm" icon={<Save size={13} />} onClick={savePlan} disabled={updatePlan.isPending}>
                    {updatePlan.isPending ? t("saving") : t("saveButton")}
                  </Btn>
                  <Btn size="sm" variant="ghost" onClick={() => setEditingPlan(false)}>
                    {t("cancelButton")}
                  </Btn>
                </div>
              </div>
            ) : (
              <div>
                <span
                  style={{
                    display: "inline-block",
                    padding: "4px 12px",
                    borderRadius: 7,
                    fontSize: 13,
                    fontWeight: 600,
                    background: PLAN_COLORS[tenant.plan]?.bg,
                    border: `1px solid ${PLAN_COLORS[tenant.plan]?.border}`,
                    color: PLAN_COLORS[tenant.plan]?.text,
                  }}
                >
                  {tPlans(tenant.plan)}
                </span>
              </div>
            )}
          </div>

          {/* Modules section */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
              <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600 }}>{t("modulesSectionTitle")}</div>
              {!editingModules && (
                <button
                  onClick={() => startEditModules(tenant)}
                  style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 12, cursor: "pointer" }}
                >
                  {t("configureButton")}
                </button>
              )}
            </div>

            {editingModules ? (
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                  {ALL_MODULES.map((m) => {
                    const active = selectedMods.includes(m);
                    return (
                      <label
                        key={m}
                        style={{
                          display: "flex", alignItems: "center", gap: 10,
                          padding: "8px 12px",
                          borderRadius: 7,
                          background: active ? "#0F1F3D" : "#111827",
                          border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
                          cursor: "pointer",
                        }}
                      >
                        <input
                          type="checkbox"
                          checked={active}
                          onChange={() => toggleMod(m)}
                          style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
                        />
                        <span style={{ color: active ? "#93C5FD" : "#6B7280", fontSize: 13 }}>
                          {tModules(m)}
                        </span>
                      </label>
                    );
                  })}
                </div>
                <div style={{ display: "flex", gap: 8, marginTop: 4 }}>
                  <Btn size="sm" icon={<Save size={13} />} onClick={saveModules} disabled={updateModules.isPending}>
                    {updateModules.isPending ? t("saving") : t("saveButton")}
                  </Btn>
                  <Btn size="sm" variant="ghost" onClick={() => setEditingModules(false)}>
                    {t("cancelButton")}
                  </Btn>
                </div>
              </div>
            ) : (
              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {tenant.modules.length === 0 ? (
                  <span style={{ color: "#4B5563", fontSize: 13 }}>{t("noModulesConnected")}</span>
                ) : tenant.modules.map((m) => (
                  <span
                    key={m}
                    style={{
                      padding: "4px 10px",
                      borderRadius: 6,
                      fontSize: 12,
                      background: "#0F1F3D",
                      border: "1px solid #1E3A5F",
                      color: "#93C5FD",
                    }}
                  >
                    {tModules.has(m) ? tModules(m) : m}
                  </span>
                ))}
              </div>
            )}
          </div>

          {/* Admins */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
              <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600 }}>{t("adminsSectionTitle")}</div>
              <button
                onClick={() => setShowAddUser(true)}
                style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 12, cursor: "pointer" }}
              >
                {t("addButton")}
              </button>
            </div>

            {usersLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
            ) : tenantUsers.length === 0 ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noAdminsAdded")}</div>
            ) : (
              <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                {tenantUsers.map((user) => (
                  <div
                    key={user.id}
                    style={{
                      background: "#111827",
                      border: "1px solid #1F2937",
                      borderRadius: 8,
                      padding: "10px 14px",
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: 10,
                    }}
                  >
                    <div>
                      <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600, marginBottom: 2 }}>
                        {user.fullName}
                      </div>
                      <div style={{ color: "#6B7280", fontSize: 12 }}>{user.email}</div>
                    </div>
                    <span
                      style={{
                        padding: "2px 8px",
                        borderRadius: 5,
                        fontSize: 11,
                        fontWeight: 600,
                        background: "#1D3461",
                        border: "1px solid #3B82F6",
                        color: "#93C5FD",
                        whiteSpace: "nowrap",
                        flexShrink: 0,
                      }}
                    >
                      Admin
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Actions */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 8 }}>
              {t("actionsSectionTitle")}
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 12 }}>
              {t("impersonateHint")}
            </div>
            {impersonateErr && (
              <div style={{ color: "#F87171", fontSize: 12, marginBottom: 10 }}>{impersonateErr}</div>
            )}
            <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
              <Btn icon={<LogIn size={15} />} onClick={handleImpersonate} disabled={impersonating || !tenant.isActive}>
                {impersonating ? t("connecting") : t("impersonateButton")}
              </Btn>

              <Btn variant="ghost" icon={<ScrollText size={15} />} onClick={() => onViewLogs(tenantId)}>
                {t("logsButton")}
              </Btn>
            </div>
            {!tenant.isActive && (
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 6 }}>
                {t("deactivatedHint")}
              </div>
            )}

            <div style={{ borderTop: "1px solid #1F2937", marginTop: 16, paddingTop: 16 }}>
              {!tenant.isActive && (
                <Btn variant="success" onClick={() => activate.mutate()} disabled={activate.isPending}>
                  {activate.isPending ? t("changing") : t("activateButton")}
                </Btn>
              )}
              {tenant.isActive && (
                <Btn variant="danger" onClick={() => deactivate.mutate()} disabled={deactivate.isPending}>
                  {deactivate.isPending ? t("changing") : t("deactivateButton")}
                </Btn>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
