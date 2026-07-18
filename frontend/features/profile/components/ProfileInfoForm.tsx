"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useUpdateProfile } from "../hooks/useProfile";
import { getRoleLabel } from "../types";
import { Btn } from "@/components/ui/Btn";

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

const readonlyInputStyle: React.CSSProperties = {
  ...inputStyle,
  borderColor: "#1F2937",
  color: "#4B5563",
  cursor: "default",
};

export function ProfileInfoForm() {
  const t = useTranslations("Dashboard.profile.infoForm");
  const tRoles = useTranslations("Dashboard.roles");
  const { data: me } = useMe();
  const update = useUpdateProfile();

  const [fullName, setFullName] = useState("");
  const [phone, setPhone]       = useState("");
  const [nameError, setNameError] = useState("");
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (me) {
      setFullName(me.fullName ?? "");
    }
  }, [me]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!fullName.trim()) { setNameError(t("nameRequiredError")); return; }
    setNameError("");

    await update.mutateAsync({ fullName: fullName.trim(), phone: phone.trim() || undefined });
    setSaved(true);
    setTimeout(() => setSaved(false), 3000);
  }

  return (
    <form onSubmit={handleSubmit}>
      {/* Avatar placeholder */}
      <div style={{ display: "flex", alignItems: "center", gap: 16, marginBottom: 24 }}>
        <div
          style={{
            width: 64, height: 64, borderRadius: "50%",
            background: "linear-gradient(135deg, #3B82F6, #6366F1)",
            display: "flex", alignItems: "center", justifyContent: "center",
            color: "#fff", fontSize: 22, fontWeight: 700, flexShrink: 0,
          }}
        >
          {me?.fullName
            ? me.fullName.split(" ").slice(0, 2).map((n) => n[0]).join("").toUpperCase()
            : "?"}
        </div>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>
            {me?.fullName ?? "—"}
          </div>
          <div style={{ color: "#4B5563", fontSize: 12, marginTop: 3 }}>
            {getRoleLabel(tRoles, me?.role)}
          </div>
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16, marginBottom: 16 }}>
        {/* Full name */}
        <div style={{ gridColumn: "1 / -1" }}>
          <label style={labelStyle}>
            {t("fullNameLabel")} <span style={{ color: "#EF4444" }}>*</span>
          </label>
          <input
            value={fullName}
            onChange={(e) => { setFullName(e.target.value); setNameError(""); }}
            placeholder={t("fullNamePlaceholder")}
            style={{ ...inputStyle, borderColor: nameError ? "#EF4444" : "#374151" }}
          />
          {nameError && (
            <p style={{ color: "#EF4444", fontSize: 11, marginTop: 4 }}>{nameError}</p>
          )}
        </div>

        {/* Email — readonly */}
        <div>
          <label style={labelStyle}>{t("emailLabel")}</label>
          <input
            value={me?.email ?? ""}
            readOnly
            style={readonlyInputStyle}
            title={t("emailReadonlyTitle")}
          />
          <p style={{ color: "#374151", fontSize: 11, marginTop: 4 }}>
            {t("emailHint")}
          </p>
        </div>

        {/* Phone */}
        <div>
          <label style={labelStyle}>{t("phoneLabel")}</label>
          <input
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder={t("phonePlaceholder")}
            style={inputStyle}
            type="tel"
          />
        </div>

        {/* Role — readonly */}
        <div>
          <label style={labelStyle}>{t("roleLabel")}</label>
          <input
            value={getRoleLabel(tRoles, me?.role)}
            readOnly
            style={readonlyInputStyle}
            title={t("roleReadonlyTitle")}
          />
        </div>

        {/* Store — readonly */}
        <div>
          <label style={labelStyle}>{t("storeLabel")}</label>
          <input
            value={me?.storeId ? t("storeValue", { id: me.storeId.slice(0, 6) }) : t("storeNotLinked")}
            readOnly
            style={readonlyInputStyle}
          />
        </div>
      </div>

      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <Btn type="submit" disabled={update.isPending}>
          {update.isPending ? t("savingButton") : t("saveButton")}
        </Btn>
        {saved && (
          <span style={{ color: "#4ADE80", fontSize: 13 }}>{t("savedMessage")}</span>
        )}
        {update.isError && (
          <span style={{ color: "#F87171", fontSize: 13 }}>
            {t("saveErrorMessage")}
          </span>
        )}
      </div>
    </form>
  );
}
