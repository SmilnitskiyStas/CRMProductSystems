"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { X, LogIn, Save } from "lucide-react";
import {
  PLAN_COLORS, PLAN_LABELS, MODULE_LABELS,
  ALL_MODULES, ALL_PLANS,
} from "../types";
import type { TenantDetailDto } from "../types";
import { useTenant, useUpdatePlan, useUpdateModules, useImpersonate } from "../hooks/useProvider";
import { setToken, getToken } from "@/lib/api";
import { ME_KEY } from "@/features/auth/hooks/useAuth";

interface Props {
  tenantId: string;
  onClose: () => void;
  onImpersonated: () => void;
}

function formatDate(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

export function TenantDetailPanel({ tenantId, onClose, onImpersonated }: Props) {
  const { data: tenant, isLoading } = useTenant(tenantId, true);
  const updatePlan    = useUpdatePlan(tenantId);
  const updateModules = useUpdateModules(tenantId);
  const impersonate   = useImpersonate();
  const queryClient   = useQueryClient();

  const [editingPlan,    setEditingPlan]    = useState(false);
  const [editingModules, setEditingModules] = useState(false);
  const [selectedPlan,   setSelectedPlan]   = useState<string>("");
  const [selectedMods,   setSelectedMods]   = useState<string[]>([]);
  const [impersonating,  setImpersonating]  = useState(false);
  const [impersonateErr, setImpersonateErr] = useState("");

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
                  <button
                    onClick={savePlan}
                    disabled={updatePlan.isPending}
                    style={{
                      display: "flex", alignItems: "center", gap: 6,
                      padding: "7px 14px", borderRadius: 7,
                      background: "#1D3461", border: "1px solid #3B82F6",
                      color: "#93C5FD", fontSize: 12, cursor: "pointer",
                    }}
                  >
                    <Save size={13} />
                    {updatePlan.isPending ? "Збереження…" : "Зберегти"}
                  </button>
                  <button
                    onClick={() => setEditingPlan(false)}
                    style={{
                      padding: "7px 14px", borderRadius: 7,
                      background: "transparent", border: "1px solid #374151",
                      color: "#6B7280", fontSize: 12, cursor: "pointer",
                    }}
                  >
                    Скасувати
                  </button>
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
                  <button
                    onClick={saveModules}
                    disabled={updateModules.isPending}
                    style={{
                      display: "flex", alignItems: "center", gap: 6,
                      padding: "7px 14px", borderRadius: 7,
                      background: "#1D3461", border: "1px solid #3B82F6",
                      color: "#93C5FD", fontSize: 12, cursor: "pointer",
                    }}
                  >
                    <Save size={13} />
                    {updateModules.isPending ? "Збереження…" : "Зберегти"}
                  </button>
                  <button
                    onClick={() => setEditingModules(false)}
                    style={{
                      padding: "7px 14px", borderRadius: 7,
                      background: "transparent", border: "1px solid #374151",
                      color: "#6B7280", fontSize: 12, cursor: "pointer",
                    }}
                  >
                    Скасувати
                  </button>
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

          {/* Impersonation */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 8 }}>
              ВХІД ЯК КЛІЄНТ
            </div>
            <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 12 }}>
              Увійдіть від імені адміністратора цього клієнта на 60 хвилин. Сесія записується в журнал.
            </div>
            {impersonateErr && (
              <div style={{ color: "#F87171", fontSize: 12, marginBottom: 10 }}>{impersonateErr}</div>
            )}
            <button
              onClick={handleImpersonate}
              disabled={impersonating || !tenant.isActive}
              style={{
                display: "flex", alignItems: "center", gap: 8,
                padding: "9px 18px", borderRadius: 8,
                background: impersonating || !tenant.isActive ? "#1A1F2C" : "#1D3461",
                border: `1px solid ${impersonating || !tenant.isActive ? "#374151" : "#3B82F6"}`,
                color: impersonating || !tenant.isActive ? "#4B5563" : "#93C5FD",
                fontSize: 13, fontWeight: 600,
                cursor: impersonating || !tenant.isActive ? "default" : "pointer",
              }}
            >
              <LogIn size={15} />
              {impersonating ? "Підключення…" : "Увійти як клієнт"}
            </button>
            {!tenant.isActive && (
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 6 }}>
                Клієнт деактивований — вхід як клієнт недоступний
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
