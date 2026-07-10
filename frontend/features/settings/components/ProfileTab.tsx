"use client";

import { ProfileInfoForm } from "@/features/profile/components/ProfileInfoForm";
import { ChangePasswordForm } from "@/features/profile/components/ChangePasswordForm";
import { TwoFactorSection } from "@/features/profile/components/TwoFactorSection";
import { TelegramLinkSection } from "@/features/profile/components/TelegramLinkSection";

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
  return (
    <div>
      {/* Section 1 — Personal info */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>Особисті дані</h3>
        <p style={sectionSubtitleStyle}>Ім&apos;я та контактна інформація</p>
        <ProfileInfoForm />
      </div>

      {/* Section 2 — Password */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>Зміна пароля</h3>
        <p style={sectionSubtitleStyle}>Мінімум 12 символів, літери та цифри</p>
        <ChangePasswordForm />
      </div>

      {/* Section 3 — 2FA */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>Двофакторна автентифікація</h3>
        <p style={sectionSubtitleStyle}>Додатковий рівень захисту входу в акаунт</p>
        <TwoFactorSection />
      </div>

      {/* Section 4 — Telegram */}
      <div>
        <h3 style={sectionTitleStyle}>Telegram</h3>
        <p style={sectionSubtitleStyle}>Прив&apos;яжіть Telegram для особистих сповіщень</p>
        <TelegramLinkSection />
      </div>
    </div>
  );
}
