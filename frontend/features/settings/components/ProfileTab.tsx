"use client";

import { useTranslations } from "next-intl";
import { ProfileInfoForm } from "@/features/profile/components/ProfileInfoForm";
import { ChangePasswordForm } from "@/features/profile/components/ChangePasswordForm";
import { TwoFactorSection } from "@/features/profile/components/TwoFactorSection";
import { TelegramLinkSection } from "@/features/profile/components/TelegramLinkSection";
import { LanguageSwitcher } from "@/features/profile/components/LanguageSwitcher";

const sectionStyle: React.CSSProperties = {
  paddingBottom: 28,
  marginBottom: 28,
  borderBottom: "1px solid #1F2937",
};

const sectionTitleStyle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 14,
  fontWeight: 600,
  margin: "0 0 4px",
};

const sectionSubtitleStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  margin: "0 0 20px",
};

export function ProfileTab() {
  const t = useTranslations("Dashboard.settings.profileTab");

  return (
    <div>
      {/* Section 1 — Personal info */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>{t("personalTitle")}</h3>
        <p style={sectionSubtitleStyle}>{t("personalSubtitle")}</p>
        <ProfileInfoForm />
      </div>

      {/* Section 2 — Password */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>{t("passwordTitle")}</h3>
        <p style={sectionSubtitleStyle}>{t("passwordSubtitle")}</p>
        <ChangePasswordForm />
      </div>

      {/* Section 3 — 2FA */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>{t("twoFaTitle")}</h3>
        <p style={sectionSubtitleStyle}>{t("twoFaSubtitle")}</p>
        <TwoFactorSection />
      </div>

      {/* Section 4 — Telegram */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>{t("telegramTitle")}</h3>
        <p style={sectionSubtitleStyle}>{t("telegramSubtitle")}</p>
        <TelegramLinkSection />
      </div>

      {/* Section 5 — Language (i18n Block 1, TASK-376) */}
      <div>
        <LanguageSwitcher />
      </div>
    </div>
  );
}
