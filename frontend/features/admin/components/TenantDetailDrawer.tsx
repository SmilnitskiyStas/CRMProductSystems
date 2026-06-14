"use client";

import { useState } from "react";
import { X, Save, Users, Building2, Package, TrendingUp } from "lucide-react";
import { useTenant, useUpdatePlan, useUpdateModules, useActivateTenant, useDeactivateTenant } from "../hooks/useAdmin";
import {
  PLAN_COLORS, PLAN_LABELS, MODULE_LABELS,
  ALL_MODULES, ALL_PLANS,
} from "../types";
import type { TenantDto } from "../types";

interface Props {
  tenantId: string;
  onClose: () => void;
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric",
  });
}

function formatSales(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`;
  return String(n);
}

function UsageCard({ icon, label, value, color }: { icon: React.ReactNode; label: string; value: string | number; color: string }) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 8,
        padding: "12px 14px",
        display: "flex",
        alignItems: "center",
        gap: 12,
      }}
    >
      <div style={{ color, opacity: 0.8 }}>{icon}</div>
      <div>
        <div style={{ color, fontSize: 18, fontWeight: 700, lineHeight: 1 }}>{value}</div>
        <div style={{ color: "#4B5563", fontSize: 11, marginTop: 3 }}>{label}</div>
      </div>
    </div>
  );
}

export function TenantDetailDrawer({ tenantId, onClose }: Props) {
  const { data: tenant, isLoading } = useTenant(tenantId, true);
  const updatePlan    = useUpdatePlan(tenantId);
  const updateModules = useUpdateModules(tenantId);
  const activate      = useActivateTenant();
  const deactivate    = useDeactivateTenant();

  const [editingPlan,    setEditingPlan]    = useState(false);
  const [editingModules, setEditingModules] = useState(false);
  const [selectedPlan,   setSelectedPlan]   = useState("");
  const [selectedMods,   setSelectedMods]   = useState<string[]>([]);

  function startEditPlan(t: TenantDto) {
    setSelectedPlan(t.plan);
    setEditingPlan(true);
  }

  function startEditModules(t: TenantDto) {
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

  function handleToggleActive() {
    if (!tenant) return;
    if (tenant.isActive) {
      deactivate.mutate(tenantId);
    } else {
      activate.mutate(tenantId);
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
          Деталі тенанта
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
                {tenant.isActive ? "Активний" : "Деактивовано"}
              </span>
            </div>
            <div style={{ color: "#4B5563", fontSize: 12 }}>
              {tenant.slug} · Зареєстровано {formatDate(tenant.createdAt)}
            </div>
          </div>

          {/* Usage stats */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <UsageCard icon={<Users size={16} />}      label="Користувачів"   value={tenant.usage.usersCount}    color="#60A5FA" />
            <UsageCard icon={<Building2 size={16} />}  label="Магазинів"      value={tenant.usage.storesCount}   color="#A78BFA" />
            <UsageCard icon={<Package size={16} />}    label="Товарів"        value={tenant.usage.productsCount} color="#34D399" />
            <UsageCard icon={<TrendingUp size={16} />} label="Продажі 30д"    value={formatSales(tenant.usage.salesLast30Days)} color="#FBBF24" />
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

          {/* Activate / Deactivate */}
          <div style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}>
            <div style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, marginBottom: 10 }}>
              УПРАВЛІННЯ ДОСТУПОМ
            </div>
            <button
              onClick={handleToggleActive}
              disabled={activate.isPending || deactivate.isPending}
              style={{
                padding: "9px 18px",
                borderRadius: 8,
                fontSize: 13,
                fontWeight: 600,
                cursor: activate.isPending || deactivate.isPending ? "default" : "pointer",
                background: tenant.isActive ? "#1F1211" : "#052e16",
                border: `1px solid ${tenant.isActive ? "#7F1D1D" : "#166534"}`,
                color: tenant.isActive ? "#F87171" : "#4ADE80",
              }}
            >
              {activate.isPending || deactivate.isPending
                ? "Обробка…"
                : tenant.isActive
                ? "Деактивувати тенант"
                : "Активувати тенант"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
