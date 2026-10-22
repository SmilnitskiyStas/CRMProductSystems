"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Plus, Pencil, Trash2, Shield } from "lucide-react";
import {
  useSupplierRoles,
  useCreateSupplierRole,
  useUpdateSupplierRole,
  useDeleteSupplierRole,
} from "../hooks/useSupplierCabinet";
import { ALL_SUPPLIER_PERMISSIONS } from "@/lib/supplierPermissions";
import type { SupplierRoleDto } from "../types";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { StatusPill } from "@/components/ui/StatusPill";
import { inputStyle } from "@/components/ui/fieldStyles";

export function RolesTab() {
  const t = useTranslations("Dashboard.supplierCabinet.rolesTab");
  const { data: roles, isLoading, isError } = useSupplierRoles();
  const [editRole, setEditRole] = useState<SupplierRoleDto | null>(null);
  const [creating, setCreating] = useState(false);
  const deleteRole = useDeleteSupplierRole();

  return (
    <div
      style={{
        background: "#111827", border: "1px solid #1F2937",
        borderRadius: 12, padding: "20px 24px",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20, gap: 12, flexWrap: "wrap" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <Shield size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            {t("title")}
          </h2>
          {roles && (
            <span style={{
              padding: "2px 8px", borderRadius: 10,
              background: "#1F2937", color: "#9CA3AF", fontSize: 11,
            }}>
              {roles.length}
            </span>
          )}
        </div>
        <Btn icon={<Plus size={14} />} onClick={() => setCreating(true)}>
          {t("newRoleButton")}
        </Btn>
      </div>

      {isLoading && (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
      )}

      {isError && (
        <div style={{ color: "#F87171", fontSize: 13 }}>
          {t("errorLoad")}
        </div>
      )}

      {!isLoading && !isError && (
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          {roles?.map((role) => (
            <div
              key={role.id}
              style={{
                display: "flex", alignItems: "flex-start", justifyContent: "space-between",
                background: "#0D1117",
                border: "1px solid #1F2937", borderRadius: 10,
                padding: "14px 18px", gap: 12,
              }}
            >
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
                  <span style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600 }}>
                    {role.displayName}
                  </span>
                  {role.isSystem && (
                    <StatusPill bg="#1F2937" text="#4B5563">
                      {t("systemBadge")}
                    </StatusPill>
                  )}
                  <StatusPill bg="#1E3A5F22" text="#60A5FA">
                    {role.baseRole === "supplier_admin" ? t("baseRoleLabels.supplier_admin") : role.baseRole}
                  </StatusPill>
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 5 }}>
                  {role.permissions.length === 0 ? (
                    <span style={{ color: "#374151", fontSize: 12 }}>{t("noPermissions")}</span>
                  ) : role.permissions.map((p) => (
                    <StatusPill key={p} bg="#1E3A5F" text="#93C5FD">
                      {t(`permissionLabels.${p}`)}
                    </StatusPill>
                  ))}
                </div>
              </div>
              {!role.isSystem && (
                <div style={{ display: "flex", gap: 4, flexShrink: 0, paddingTop: 2 }}>
                  <button
                    onClick={() => setEditRole(role)}
                    title={t("editTitle")}
                    style={{ background: "none", border: "none", color: "#4B5563", cursor: "pointer", padding: 5 }}
                    onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#60A5FA"; }}
                    onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                  >
                    <Pencil size={14} />
                  </button>
                  <button
                    onClick={() => {
                      if (confirm(t("deleteConfirm", { name: role.displayName }))) {
                        deleteRole.mutate(role.id);
                      }
                    }}
                    title={t("deleteTitle")}
                    style={{ background: "none", border: "none", color: "#4B5563", cursor: "pointer", padding: 5 }}
                    onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.color = "#F87171"; }}
                    onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.color = "#4B5563"; }}
                  >
                    <Trash2 size={14} />
                  </button>
                </div>
              )}
            </div>
          ))}

          {(!roles || roles.length === 0) && (
            <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noRoles")}</div>
          )}
        </div>
      )}

      {creating && <RoleFormModal onClose={() => setCreating(false)} />}
      {editRole && <RoleFormModal role={editRole} onClose={() => setEditRole(null)} />}
    </div>
  );
}

/* ─────────────────────────── Role form modal ─────────────────────────── */

interface RoleFormModalProps {
  role?: SupplierRoleDto;
  onClose: () => void;
}

function RoleFormModal({ role, onClose }: RoleFormModalProps) {
  const t = useTranslations("Dashboard.supplierCabinet.rolesTab.modal");
  const tPermissions = useTranslations("Dashboard.supplierCabinet.rolesTab");
  const create = useCreateSupplierRole();
  const update = useUpdateSupplierRole();
  const [displayName, setDisplayName] = useState(role?.displayName ?? "");
  const [permissions, setPermissions] = useState<Set<string>>(new Set(role?.permissions ?? []));
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!role;
  const isPending = create.isPending || update.isPending;
  const baseRole = "supplier_admin";

  function togglePerm(p: string) {
    setPermissions((prev) => {
      const next = new Set(prev);
      if (next.has(p)) next.delete(p);
      else next.add(p);
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    const req = { displayName, baseRole, permissions: [...permissions] };
    try {
      if (isEdit) await update.mutateAsync({ id: role.id, body: req });
      else await create.mutateAsync(req);
      onClose();
    } catch (err: unknown) {
      const e = err as { error?: string };
      setError(e?.error ?? t("errorSaveDefault"));
    }
  }

  return (
    <Modal title={isEdit ? t("titleEdit") : t("titleCreate")} onClose={onClose} width={480}>
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <input
            required
            type="text"
            placeholder={t("namePlaceholder")}
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            style={inputStyle}
          />

          <div>
            <div style={{ color: "#6B7280", fontSize: 12, marginBottom: 8 }}>{t("permissionsLabel")}</div>
            <div
              style={{
                background: "#0D1117", border: "1px solid #1F2937",
                borderRadius: 8, padding: "12px 14px",
                display: "flex", flexDirection: "column", gap: 7,
              }}
            >
              {ALL_SUPPLIER_PERMISSIONS.map((p) => (
                <label key={p} style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={permissions.has(p)}
                    onChange={() => togglePerm(p)}
                    style={{ width: 14, height: 14, cursor: "pointer", accentColor: "#3B82F6" }}
                  />
                  <span style={{ color: permissions.has(p) ? "#E8EDF5" : "#4B5563", fontSize: 13 }}>
                    {tPermissions(`permissionLabels.${p}`)}
                  </span>
                </label>
              ))}
            </div>
          </div>

          {error && (
            <div style={{
              color: "#F87171", fontSize: 13, padding: "8px 12px",
              background: "#1F0A0A", borderRadius: 8, border: "1px solid #7F1D1D",
            }}>
              {error}
            </div>
          )}

          <Btn type="submit" fullWidth disabled={isPending}>
            {isPending ? t("saving") : (isEdit ? t("saveChanges") : t("createRole"))}
          </Btn>
        </form>
    </Modal>
  );
}
