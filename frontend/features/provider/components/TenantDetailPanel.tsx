"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { X, LogIn, Save, ScrollText } from "lucide-react";
import {
  PLAN_COLORS, PLAN_LABELS, MODULE_LABELS,
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

function formatDate(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

export function TenantDetailPanel({ tenantId, onClose, onImpersonated, onViewLogs }: Props) {
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
      setImpersonateErr((err as Error)?.message ?? "Помилка imersonation");
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
          Деталі клієнта
        </div>
        <button
          onClick={onClose}
          style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
        >
          <X size={18} />
        </button>
      </div>

      {isLoading && (
        <div style={{ color: "#4B5563", fontSize: 13, padding: 24 }}>Завантаження…</div>
      )}

      {!isLoading && !tenant && (
        <div style={{ color: "#4B5563", fontSize: 13, padding: 24 }}>Не знайдено</div>
      )}

      {showAddUser && (
        <AddTenantUserModal
          tenantId={tenantId}
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
                {tenant.isActive ? "Активний" : "Деактивований"}
              </span>
            </div>
            <div style={{ color: "#4B5563", fontSize: 12 }}>{tenant.slug}</div>
          </div>

          {/* Info grid */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            {[
              { label: "Користувачів",     value: tenant.userCount },
              { label: "Магазинів",        value: tenant.storeCount },
              { label: "Протерм. партій",  value: tenant.expiredBatchCount, warn: tenant.expiredBatchCount > 0 },
              { label: "Зареєстровано",    value: formatDate(tenant.createdAt) },
              { label: "Остання активність", value: formatDate(tenant.lastActivityAt), span: true },
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
              <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600 }}>ПЛАН</div>
              {!editingPlan && (
                <button
                  onClick={() => startEditPlan(tenant)}
                  style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 12, cursor: "pointer" }}
                >
                  Змінити
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
                        {PLAN_LABELS[p]}
                      </button>
                    );
                  })}
                </div>
                <div style={{ display: "flex", gap: 8, marginTop: 4 }}>
                  <Btn size="sm" icon={<Save size={13} />} onClick={savePlan} disabled={updatePlan.isPending}>
                    {updatePlan.isPending ? "Збереження…" : "Зберегти"}
                  </Btn>
                  <Btn size="sm" variant="ghost" onClick={() => setEditingPlan(false)}>
                    Скасувати
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
                  {PLAN_LABELS[tenant.plan]}
                </span>
              </div>
            )}
          </div>

          {/* Modules section */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
              <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600 }}>МОДУЛІ</div>
              {!editingModules && (
                <button
                  onClick={() => startEditModules(tenant)}
                  style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 12, cursor: "pointer" }}
                >
                  Налаштувати
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
                          {MODULE_LABELS[m]}
                        </span>
                      </label>
                    );
                  })}
                </div>
                <div style={{ display: "flex", gap: 8, marginTop: 4 }}>
                  <Btn size="sm" icon={<Save size={13} />} onClick={saveModules} disabled={updateModules.isPending}>
                    {updateModules.isPending ? "Збереження…" : "Зберегти"}
                  </Btn>
                  <Btn size="sm" variant="ghost" onClick={() => setEditingModules(false)}>
                    Скасувати
                  </Btn>
                </div>
              </div>
            ) : (
              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {tenant.modules.length === 0 ? (
                  <span style={{ color: "#4B5563", fontSize: 13 }}>Модулі не підключено</span>
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
                    {MODULE_LABELS[m] ?? m}
                  </span>
                ))}
              </div>
            )}
          </div>

          {/* Admins */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
              <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600 }}>АДМІНІСТРАТОРИ</div>
              <button
                onClick={() => setShowAddUser(true)}
                style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 12, cursor: "pointer" }}
              >
                Додати
              </button>
            </div>

            {usersLoading ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>
            ) : tenantUsers.length === 0 ? (
              <div style={{ color: "#4B5563", fontSize: 13 }}>Адміністраторів не додано</div>
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
              ДІЇ
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 12 }}>
              Увійдіть від імені адміністратора цього клієнта на 60 хвилин. Сесія записується в журнал.
            </div>
            {impersonateErr && (
              <div style={{ color: "#F87171", fontSize: 12, marginBottom: 10 }}>{impersonateErr}</div>
            )}
            <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
              <Btn icon={<LogIn size={15} />} onClick={handleImpersonate} disabled={impersonating || !tenant.isActive}>
                {impersonating ? "Підключення…" : "Увійти як клієнт"}
              </Btn>

              <Btn variant="ghost" icon={<ScrollText size={15} />} onClick={() => onViewLogs(tenantId)}>
                Логи
              </Btn>
            </div>
            {!tenant.isActive && (
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 6 }}>
                Клієнт деактивований — вхід як клієнт недоступний
              </div>
            )}

            <div style={{ borderTop: "1px solid #1F2937", marginTop: 16, paddingTop: 16 }}>
              {!tenant.isActive && (
                <Btn variant="success" onClick={() => activate.mutate()} disabled={activate.isPending}>
                  {activate.isPending ? "Зміна…" : "Активувати"}
                </Btn>
              )}
              {tenant.isActive && (
                <Btn variant="danger" onClick={() => deactivate.mutate()} disabled={deactivate.isPending}>
                  {deactivate.isPending ? "Зміна…" : "Деактивувати"}
                </Btn>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
