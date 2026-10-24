"use client";

import type { ReactNode } from "react";
import { User, Lock, ShieldCheck, Send, Bell } from "lucide-react";
import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { ProfileInfoForm } from "@/features/profile/components/ProfileInfoForm";
import { ChangePasswordForm } from "@/features/profile/components/ChangePasswordForm";
import { TwoFactorSection } from "@/features/profile/components/TwoFactorSection";
import { TelegramLinkSection } from "@/features/profile/components/TelegramLinkSection";
import { useMe } from "@/features/auth/hooks/useAuth";
import { getRoleLabel } from "@/features/profile/types";
import styles from "./page.module.css";

export default function UserSettingsPage() {
  const t = useTranslations("Dashboard.settings.userSettingsPage");

  return (
    <PageContainer style={{ width: "100%", minWidth: 0, maxWidth: "none", padding: "24px clamp(16px, 2vw, 32px)" }}>
      <div className={styles.container}>
        <ProfileCard />
        <nav className={styles.navigation} aria-label={t("navigationLabel")}>
          <a href="#profile"><User size={15} aria-hidden="true" />{t("profileLabel")}</a>
          <a href="#account-security"><ShieldCheck size={15} aria-hidden="true" />{t("accountSecurityLabel")}</a>
          <a href="#telegram"><Bell size={15} aria-hidden="true" />{t("notificationsLabel")}</a>
        </nav>

        <div className={styles.groups}>
          <SettingsCard id="profile" title={t("profileLabel")} subtitle={t("profileSubtitle")} icon={<User size={18} />} className={styles.profile}>
            <ProfileInfoForm />
          </SettingsCard>

          <SettingsCard id="account-security" title={t("accountSecurityLabel")} subtitle={t("accountSecuritySubtitle")} icon={<ShieldCheck size={18} />} className={styles.security}>
            <div className={styles.securitySections}>
              <section id="password" className={styles.subsection} aria-labelledby="password-heading">
                <SubsectionHeading id="password-heading" title={t("passwordLabel")} subtitle={t("passwordSubtitle")} icon={<Lock size={16} />} />
                <ChangePasswordForm />
              </section>
              <section id="security" className={styles.subsection} aria-labelledby="mfa-heading">
                <SubsectionHeading id="mfa-heading" title={t("securityLabel")} subtitle={t("securitySubtitle")} icon={<ShieldCheck size={16} />} />
                <TwoFactorSection />
              </section>
            </div>
          </SettingsCard>

          <SettingsCard id="telegram" title={t("notificationsLabel")} subtitle={t("notificationsSubtitle")} icon={<Bell size={18} />} className={styles.notifications}>
            <SubsectionHeading id="telegram-channel-heading" title={t("telegramLabel")} subtitle={t("telegramSubtitle")} icon={<Send size={16} />} />
            <TelegramLinkSection />
          </SettingsCard>
        </div>
      </div>
    </PageContainer>
  );
}

function ProfileCard() {
  const t = useTranslations("Dashboard.settings.userSettingsPage");
  const tRoles = useTranslations("Dashboard.roles");
  const { data: me } = useMe();
  const initials = me?.fullName?.split(" ").filter(Boolean).slice(0, 2).map(n => n[0]).join("").toUpperCase() || "?";

  return <header className={styles.profileHeader}>
    <div className={styles.avatar} aria-hidden="true">{initials}</div>
    <div className={styles.identity}>
      <h1>{me?.fullName ?? "…"}</h1>
      <div className={styles.identityDetails}>
        <span className={styles.email}>{me?.email ?? ""}</span>
        <span className={styles.role}>{getRoleLabel(tRoles, me?.role)}</span>
      </div>
    </div>
    <span className={styles.personalLabel}>{t("personalLabel")}</span>
  </header>;
}

function SettingsCard({ id, title, subtitle, icon, className, children }: {
  id: string; title: string; subtitle: string; icon: ReactNode; className: string; children: ReactNode;
}) {
  return <section id={id} className={`${styles.card} ${className}`} aria-labelledby={`${id}-heading`}>
    <div className={styles.cardHeading}>
      <span className={styles.headingIcon} aria-hidden="true">{icon}</span>
      <div>
        <h2 id={`${id}-heading`}>{title}</h2>
        <p>{subtitle}</p>
      </div>
    </div>
    <div className={styles.cardBody}>{children}</div>
  </section>;
}

function SubsectionHeading({ id, title, subtitle, icon }: { id: string; title: string; subtitle: string; icon: ReactNode }) {
  return <div className={styles.subsectionHeading}>
    <span className={styles.headingIcon} aria-hidden="true">{icon}</span>
    <div><h3 id={id}>{title}</h3><p>{subtitle}</p></div>
  </div>;
}
