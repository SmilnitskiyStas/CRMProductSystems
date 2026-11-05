"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import { useInviteCabinetStaff, useSupplierRoles } from "../hooks/useSupplierCabinet";

interface Props {
  onClose: () => void;
}

export function InviteStaffModal({ onClose }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.inviteStaffModal");
  const invite = useInviteCabinetStaff();
  const { data: roles } = useSupplierRoles();

  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [password, setPassword] = useState("");
  const [supplierRoleId, setSupplierRoleId] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  function validate() {
    const e: Record<string, string> = {};
    if (!email.trim() || !email.includes("@")) e.email = t("validationEmail");
    if (!fullName.trim()) e.fullName = t("validationFullName");
    if (password.length < 8) e.password = t("validationPassword");
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    await invite.mutateAsync({
      email: email.trim().toLowerCase(),
      fullName: fullName.trim(),
      password,
      supplierRoleId: supplierRoleId || undefined,
    });
    onClose();
  }

  return (
    <Modal title={t("title")} onClose={onClose} width={480}>
      <p style={{ color: "#4B5563", fontSize: 12, margin: "-12px 0 16px" }}>{t("subtitle")}</p>
        <form onSubmit={handleSubmit}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            {/* Full name */}
            <div>
              <label style={labelStyle}>{t("fullNameLabel")}</label>
              <input
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                placeholder={t("fullNamePlaceholder")}
                style={{ ...inputStyle, borderColor: errors.fullName ? "#EF4444" : "#1F2937" }}
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
                style={{ ...inputStyle, borderColor: errors.email ? "#EF4444" : "#1F2937" }}
              />
              {errors.email && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.email}</p>
              )}
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
                style={{ ...inputStyle, borderColor: errors.password ? "#EF4444" : "#1F2937" }}
              />
              {errors.password && (
                <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{errors.password}</p>
              )}
            </div>

            {/* Role */}
            <div>
              <label style={labelStyle}>{t("roleLabel")}</label>
              <select
                value={supplierRoleId}
                onChange={(e) => setSupplierRoleId(e.target.value)}
                style={{ ...inputStyle, appearance: "none" }}
              >
                <option value="">{t("roleNoneOption")}</option>
                {roles?.map((role) => (
                  <option key={role.id} value={role.id}>
                    {role.displayName}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Actions */}
          <div style={{ display: "flex", gap: 10, marginTop: 22 }}>
            <Btn type="submit" disabled={invite.isPending} style={{ flex: 1, justifyContent: "center" }}>
              {invite.isPending ? t("creating") : t("inviteButton")}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
          </div>

          {invite.isError && (
            <p style={{ color: "#F87171", fontSize: 12, marginTop: 10 }}>
              {(invite.error as Error)?.message ?? t("errorDefault")}
            </p>
          )}
        </form>
    </Modal>
  );
}
