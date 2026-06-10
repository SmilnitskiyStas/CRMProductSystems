"use client";

import { ProfileInfoForm } from "@/features/profile/components/ProfileInfoForm";
import { ChangePasswordForm } from "@/features/profile/components/ChangePasswordForm";
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
        <p style={sectionSubtitleStyle}>Ім'я та контактна інформація</p>
        <ProfileInfoForm />
      </div>

      {/* Section 2 — Password */}
      <div style={sectionStyle}>
        <h3 style={sectionTitleStyle}>Зміна пароля</h3>
        <p style={sectionSubtitleStyle}>Використовуйте надійний пароль від 8 символів</p>
        <ChangePasswordForm />
      </div>

      {/* Section 3 — Telegram */}
      <div>
        <h3 style={sectionTitleStyle}>Telegram</h3>
        <p style={sectionSubtitleStyle}>Прив'яжіть Telegram для особистих сповіщень</p>
        <TelegramLinkSection />
      </div>
    </div>
  );
}
