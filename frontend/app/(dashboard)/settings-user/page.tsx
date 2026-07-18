"use client";

import { User, Lock, ShieldCheck, Send } from "lucide-react";
import { useTranslations } from "next-intl";
import { ProfileInfoForm } from "@/features/profile/components/ProfileInfoForm";
import { ChangePasswordForm } from "@/features/profile/components/ChangePasswordForm";
import { TwoFactorSection } from "@/features/profile/components/TwoFactorSection";
import { TelegramLinkSection } from "@/features/profile/components/TelegramLinkSection";
import { useMe } from "@/features/auth/hooks/useAuth";
import { getRoleLabel } from "@/features/profile/types";

type Section = "profile" | "password" | "security" | "telegram";

export default function UserSettingsPage() {
  const t = useTranslations("Dashboard.settings.userSettingsPage");

  const SECTIONS: { id: Section; label: string; icon: React.ReactNode; subtitle: string }[] = [
    {
      id: "profile",
      label: t("profileLabel"),
      icon: <User size={15} />,
      subtitle: t("profileSubtitle"),
    },
    {
      id: "password",
      label: t("passwordLabel"),
      icon: <Lock size={15} />,
      subtitle: t("passwordSubtitle"),
    },
    {
      id: "security",
      label: t("securityLabel"),
      icon: <ShieldCheck size={15} />,
      subtitle: t("securitySubtitle"),
    },
    {
      id: "telegram",
      label: t("telegramLabel"),
      icon: <Send size={15} />,
      subtitle: t("telegramSubtitle"),
    },
  ];

  return (
    <div style={{ padding: "28px 32px", maxWidth: 780 }}>
      {/* Header */}
      <PageHeader />

      {/* Sections */}
      <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>
        {SECTIONS.map((section) => (
          <SectionCard key={section.id} section={section} />
        ))}
      </div>
    </div>
  );
}

function PageHeader() {
  const t = useTranslations("Dashboard.settings.userSettingsPage");
  const tRoles = useTranslations("Dashboard.roles");
  const { data: me } = useMe();

  const initials = me?.fullName
    ? me.fullName.split(" ").slice(0, 2).map((n: string) => n[0]).join("").toUpperCase()
    : "?";

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 16,
        marginBottom: 32,
        padding: "20px 24px",
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
      }}
    >
      {/* Avatar */}
      <div
        style={{
          width: 56,
          height: 56,
          borderRadius: "50%",
          background: "linear-gradient(135deg, #3B82F6, #6366F1)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          color: "#fff",
          fontSize: 20,
          fontWeight: 700,
          flexShrink: 0,
          boxShadow: "0 0 0 3px rgba(59,130,246,0.15)",
        }}
      >
        {initials}
      </div>

      {/* Info */}
      <div style={{ flex: 1 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: "0 0 4px" }}>
          {me?.fullName ?? "…"}
        </h1>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <span style={{ color: "#4B5563", fontSize: 12 }}>{me?.email ?? ""}</span>
          <span style={{ color: "#1F2937" }}>·</span>
          <span
            style={{
              background: "#1A2235",
              border: "1px solid #1F2937",
              borderRadius: 20,
              padding: "2px 10px",
              color: "#3B82F6",
              fontSize: 11,
              fontWeight: 600,
            }}
          >
            {getRoleLabel(tRoles, me?.role)}
          </span>
        </div>
      </div>

      {/* Label */}
      <div style={{ textAlign: "right" }}>
        <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 500 }}>
          {t("personalLabel")}
        </div>
      </div>
    </div>
  );
}

function SectionCard({
  section,
}: {
  section: { id: Section; label: string; icon: React.ReactNode; subtitle: string };
}) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        overflow: "hidden",
      }}
    >
      {/* Section header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 10,
          padding: "16px 24px",
          borderBottom: "1px solid #1F2937",
          background: "#0A1020",
        }}
      >
        <span style={{ color: "#3B82F6" }}>{section.icon}</span>
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
            {section.label}
          </div>
          <div style={{ color: "#4B5563", fontSize: 11, marginTop: 1 }}>
            {section.subtitle}
          </div>
        </div>
      </div>

      {/* Section content */}
      <div style={{ padding: 24 }}>
        {section.id === "profile"  && <ProfileInfoForm />}
        {section.id === "password" && <ChangePasswordForm />}
        {section.id === "security" && <TwoFactorSection />}
        {section.id === "telegram" && <TelegramLinkSection />}
      </div>
    </div>
  );
}
